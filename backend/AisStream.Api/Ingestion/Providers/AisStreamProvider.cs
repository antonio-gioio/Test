using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AisStream.Api.Services;

namespace AisStream.Api.Ingestion.Providers;

/// <summary>
/// aisstream.io — free, terrestrial AIS over WebSocket. Subscribes with the API key and the
/// configured bounding boxes, then streams PositionReport and ShipStaticData messages.
/// </summary>
public class AisStreamProvider : IAisProvider
{
    private static readonly string[] MessageTypes = ["PositionReport", "ShipStaticData"];

    private readonly ProviderSettings _settings;
    private readonly ILogger _logger;

    public AisStreamProvider(ProviderSettings settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string Name => $"aisstream.io ({_settings.Name})";

    public async IAsyncEnumerable<VesselUpdate> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var url = _settings.Url ?? "wss://stream.aisstream.io/v0/stream";
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), cancellationToken);

        var subscription = JsonSerializer.Serialize(new
        {
            APIKey = _settings.ApiKey,
            BoundingBoxes = _settings.BoundingBoxes,
            FilterMessageTypes = MessageTypes,
        });
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(subscription),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

        _logger.LogInformation("Subscribed to aisstream.io feed");

        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var update = Parse(message.GetBuffer().AsMemory(0, (int)message.Length));
            if (update is not null)
            {
                yield return update;
            }
        }
    }

    private VesselUpdate? Parse(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (!root.TryGetProperty("MessageType", out var typeElement) ||
                !root.TryGetProperty("MetaData", out var meta) ||
                !root.TryGetProperty("Message", out var body) ||
                !meta.TryGetProperty("MMSI", out var mmsiElement) ||
                !mmsiElement.TryGetInt64(out var mmsi))
            {
                return null;
            }

            var shipName = meta.TryGetProperty("ShipName", out var n) ? n.GetString() : null;

            return typeElement.GetString() switch
            {
                "PositionReport" when body.TryGetProperty("PositionReport", out var r) =>
                    PositionUpdate(mmsi, shipName, r),
                "ShipStaticData" when body.TryGetProperty("ShipStaticData", out var s) =>
                    StaticUpdate(mmsi, shipName, s),
                _ => null,
            };
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Skipping unparseable aisstream message");
            return null;
        }
    }

    private static VesselUpdate? PositionUpdate(long mmsi, string? name, JsonElement r)
    {
        var lat = Num(r, "Latitude");
        var lon = Num(r, "Longitude");
        if (lat is null || lon is null)
        {
            return null;
        }

        var heading = Num(r, "TrueHeading");
        return new VesselUpdate
        {
            Mmsi = mmsi,
            Latitude = lat,
            Longitude = lon,
            SpeedOverGround = Num(r, "Sog"),
            CourseOverGround = Num(r, "Cog"),
            TrueHeading = heading is null or >= 511 ? null : heading, // 511 = not available
            NavigationalStatus = (int?)Num(r, "NavigationalStatus"),
            Name = name,
        };
    }

    private static VesselUpdate StaticUpdate(long mmsi, string? name, JsonElement s)
    {
        double? length = null, width = null;
        if (s.TryGetProperty("Dimension", out var dim) && dim.ValueKind == JsonValueKind.Object)
        {
            length = Add(Num(dim, "A"), Num(dim, "B")); // bow + stern
            width = Add(Num(dim, "C"), Num(dim, "D")); // port + starboard
        }

        return new VesselUpdate
        {
            Mmsi = mmsi,
            Name = name,
            ShipType = s.TryGetProperty("Type", out var t) && t.TryGetInt32(out var code)
                ? ShipTypes.Describe(code)
                : null,
            Destination = s.TryGetProperty("Destination", out var d) ? d.GetString() : null,
            CallSign = s.TryGetProperty("CallSign", out var c) ? c.GetString() : null,
            Imo = s.TryGetProperty("ImoNumber", out var imo) && imo.TryGetInt64(out var imoVal) && imoVal > 0
                ? imoVal
                : null,
            Length = length,
            Width = width,
            Draught = Num(s, "MaximumStaticDraught"),
            Eta = FormatEta(s),
        };
    }

    private static double? Add(double? a, double? b) =>
        a is null && b is null ? null : (a ?? 0) + (b ?? 0);

    private static string? FormatEta(JsonElement s)
    {
        if (!s.TryGetProperty("Eta", out var eta) || eta.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var month = (int?)Num(eta, "Month");
        var day = (int?)Num(eta, "Day");
        var hour = (int?)Num(eta, "Hour");
        var minute = (int?)Num(eta, "Minute");
        if (month is null or 0 || day is null or 0)
        {
            return null;
        }

        return $"{month:00}-{day:00} {hour ?? 0:00}:{minute ?? 0:00} UTC";
    }

    private static double? Num(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
