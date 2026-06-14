using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using AisStream.Api.Services;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;

namespace AisStream.Api.Ingestion.Providers;

/// <summary>
/// Digitraffic (Finnish Transport Infrastructure Agency) — free, open AIS over MQTT for the
/// Baltic / Finnish waters. Subscribes to vessel location and metadata topics, normalizing
/// the GeoJSON-style payloads. No credentials required.
/// </summary>
public class DigitrafficProvider : IAisProvider
{
    private readonly DigitrafficOptions _options;
    private readonly ILogger<DigitrafficProvider> _logger;

    public DigitrafficProvider(IOptions<DigitrafficOptions> options, ILogger<DigitrafficProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Digitraffic (Finland)";

    public async IAsyncEnumerable<VesselUpdate> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<VesselUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();

        client.ApplicationMessageReceivedAsync += e =>
        {
            var update = Parse(e.ApplicationMessage.Topic, e.ApplicationMessage.PayloadSegment);
            if (update is not null)
            {
                channel.Writer.TryWrite(update);
            }

            return Task.CompletedTask;
        };
        client.DisconnectedAsync += _ =>
        {
            channel.Writer.TryComplete();
            return Task.CompletedTask;
        };

        var clientOptions = new MqttClientOptionsBuilder()
            .WithWebSocketServer(o => o.WithUri(_options.Url))
            .WithCleanSession()
            .Build();

        await client.ConnectAsync(clientOptions, cancellationToken);
        await client.SubscribeAsync("vessels-v2/+/location", cancellationToken: cancellationToken);
        await client.SubscribeAsync("vessels-v2/+/metadata", cancellationToken: cancellationToken);
        _logger.LogInformation("Subscribed to Digitraffic MQTT AIS topics");

        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            await client.DisconnectAsync();
        }
    }

    private VesselUpdate? Parse(string topic, ReadOnlyMemory<byte> payload)
    {
        // Topic: vessels-v2/{mmsi}/location | vessels-v2/{mmsi}/metadata
        var parts = topic.Split('/');
        if (parts.Length < 3 || !long.TryParse(parts[1], out var mmsi))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (parts[2] == "location")
            {
                double? lat = null, lon = null;
                if (root.TryGetProperty("geometry", out var geom) &&
                    geom.TryGetProperty("coordinates", out var coords) &&
                    coords.ValueKind == JsonValueKind.Array && coords.GetArrayLength() >= 2)
                {
                    lon = coords[0].GetDouble();
                    lat = coords[1].GetDouble();
                }

                root.TryGetProperty("properties", out var props);
                var heading = Num(props, "heading");
                return new VesselUpdate
                {
                    Mmsi = mmsi,
                    Latitude = lat,
                    Longitude = lon,
                    SpeedOverGround = Num(props, "sog"),
                    CourseOverGround = Num(props, "cog"),
                    TrueHeading = heading is null or >= 511 ? null : heading,
                    NavigationalStatus = (int?)Num(props, "navStat"),
                };
            }

            // metadata
            return new VesselUpdate
            {
                Mmsi = mmsi,
                Name = Str(root, "name"),
                Destination = Str(root, "destination"),
                CallSign = Str(root, "callSign"),
                ShipType = root.TryGetProperty("shipType", out var st) && st.TryGetInt32(out var code)
                    ? ShipTypes.Describe(code)
                    : null,
            };
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Skipping unparseable Digitraffic message on {Topic}", topic);
            return null;
        }
    }

    private static double? Num(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static string? Str(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;
}
