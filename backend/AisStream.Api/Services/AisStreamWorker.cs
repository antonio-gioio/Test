using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AisStream.Api.Models;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Services;

/// <summary>
/// Connects to the aisstream.io WebSocket feed, subscribes with the configured
/// API key and bounding boxes, and feeds incoming AIS messages into the
/// vessel store and the SignalR broadcaster. Reconnects with backoff on failure.
/// </summary>
public class AisStreamWorker : BackgroundService
{
    private readonly AisStreamOptions _options;
    private readonly VesselStore _store;
    private readonly VesselBroadcaster _broadcaster;
    private readonly ILogger<AisStreamWorker> _logger;

    public AisStreamWorker(
        IOptions<AisStreamOptions> options,
        VesselStore store,
        VesselBroadcaster broadcaster,
        ILogger<AisStreamWorker> logger)
    {
        _options = options.Value;
        _store = store;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning(
                "No AisStream:ApiKey configured - the live aisstream.io feed is disabled. " +
                "Get a free key at https://aisstream.io and set it via configuration or the " +
                "AISSTREAM__APIKEY environment variable. Running in simulation mode instead.");
            return;
        }

        var backoff = TimeSpan.FromSeconds(2);
        var maxBackoff = TimeSpan.FromSeconds(60);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StreamOnceAsync(stoppingToken);
                backoff = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AIS stream connection failed; reconnecting in {Backoff}", backoff);
            }

            await Task.Delay(backoff, stoppingToken);
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, maxBackoff.Ticks));
        }
    }

    private async Task StreamOnceAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        _logger.LogInformation("Connecting to {Url}", _options.Url);
        await socket.ConnectAsync(new Uri(_options.Url), cancellationToken);

        var subscription = JsonSerializer.Serialize(new
        {
            APIKey = _options.ApiKey,
            BoundingBoxes = _options.BoundingBoxes,
            FilterMessageTypes = _options.MessageTypes,
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
                    throw new WebSocketException(
                        $"Server closed connection: {result.CloseStatus} {result.CloseStatusDescription}");
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            HandleMessage(message.GetBuffer().AsSpan(0, (int)message.Length));
        }
    }

    private void HandleMessage(ReadOnlySpan<byte> payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload.ToArray());
            var root = doc.RootElement;

            if (!root.TryGetProperty("MessageType", out var typeElement) ||
                !root.TryGetProperty("MetaData", out var meta) ||
                !root.TryGetProperty("Message", out var body))
            {
                return;
            }

            if (!meta.TryGetProperty("MMSI", out var mmsiElement) || !mmsiElement.TryGetInt64(out var mmsi))
            {
                return;
            }

            var shipName = meta.TryGetProperty("ShipName", out var nameElement)
                ? nameElement.GetString()?.Trim()
                : null;

            switch (typeElement.GetString())
            {
                case "PositionReport" when body.TryGetProperty("PositionReport", out var report):
                    ApplyPositionReport(mmsi, shipName, report);
                    break;
                case "ShipStaticData" when body.TryGetProperty("ShipStaticData", out var staticData):
                    ApplyStaticData(mmsi, shipName, staticData);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Skipping unparseable AIS message");
        }
    }

    private void ApplyPositionReport(long mmsi, string? shipName, JsonElement report)
    {
        var latitude = GetDouble(report, "Latitude");
        var longitude = GetDouble(report, "Longitude");
        if (latitude is null || longitude is null)
        {
            return;
        }

        var heading = GetDouble(report, "TrueHeading");
        var vessel = _store.Upsert(mmsi, v =>
        {
            v.Latitude = latitude.Value;
            v.Longitude = longitude.Value;
            v.SpeedOverGround = GetDouble(report, "Sog");
            v.CourseOverGround = GetDouble(report, "Cog");
            // 511 is the AIS sentinel for "heading not available".
            v.TrueHeading = heading is null or >= 511 ? null : heading;
            v.NavigationalStatus = (int?)GetDouble(report, "NavigationalStatus");
            if (!string.IsNullOrEmpty(shipName))
            {
                v.Name = shipName;
            }

            v.LastUpdate = DateTimeOffset.UtcNow;
        });

        _broadcaster.Enqueue(vessel);
    }

    private void ApplyStaticData(long mmsi, string? shipName, JsonElement staticData)
    {
        _store.Upsert(mmsi, v =>
        {
            if (!string.IsNullOrEmpty(shipName))
            {
                v.Name = shipName;
            }

            if (staticData.TryGetProperty("Type", out var typeElement) &&
                typeElement.TryGetInt32(out var shipType))
            {
                v.ShipType = ShipTypes.Describe(shipType) ?? v.ShipType;
            }

            if (staticData.TryGetProperty("Destination", out var destination))
            {
                var value = destination.GetString()?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    v.Destination = value;
                }
            }

            if (staticData.TryGetProperty("CallSign", out var callSign))
            {
                var value = callSign.GetString()?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    v.CallSign = value;
                }
            }

            v.LastUpdate = DateTimeOffset.UtcNow;
        });
        // Static data carries no position; the next position report will broadcast it.
    }

    private static double? GetDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
