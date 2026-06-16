using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AisStream.Api.Ingestion.Providers;

/// <summary>
/// MarineTraffic — paid, global AIS (terrestrial + satellite). Free trial via account credits.
/// This is a request/response API (PS07 "exportvessels"), so the provider polls the configured
/// bounding box on an interval rather than streaming. Uses the JSON-object response protocol.
/// </summary>
public class MarineTrafficProvider : IAisProvider
{
    private readonly ProviderSettings _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger _logger;

    public MarineTrafficProvider(ProviderSettings settings, IHttpClientFactory httpFactory, ILogger logger)
    {
        _settings = settings;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public string Name => $"MarineTraffic ({_settings.Name})";

    public async IAsyncEnumerable<VesselUpdate> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var http = _httpFactory.CreateClient();
        var box = _settings.BoundingBoxes.FirstOrDefault();
        var baseUrl = _settings.Url ?? "https://services.marinetraffic.com/api/exportvessels/v:8";

        while (!cancellationToken.IsCancellationRequested)
        {
            var url = $"{baseUrl}/{_settings.ApiKey}/protocol:jsono/msgtype:extended";
            if (box is { Length: 2 })
            {
                url += $"/minlat:{box[0][0]}/maxlat:{box[1][0]}/minlon:{box[0][1]}/maxlon:{box[1][1]}";
            }

            var batch = await FetchAsync(http, url, cancellationToken);
            foreach (var update in batch)
            {
                yield return update;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _settings.PollSeconds)), cancellationToken);
        }
    }

    private async Task<List<VesselUpdate>> FetchAsync(HttpClient http, string url, CancellationToken ct)
    {
        var results = new List<VesselUpdate>();
        try
        {
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (!TryLong(row, "MMSI", out var mmsi))
                {
                    continue;
                }

                results.Add(new VesselUpdate
                {
                    Mmsi = mmsi,
                    Latitude = Num(row, "LAT"),
                    Longitude = Num(row, "LON"),
                    SpeedOverGround = Num(row, "SPEED"),
                    CourseOverGround = Num(row, "COURSE"),
                    TrueHeading = Num(row, "HEADING"),
                    Name = Str(row, "SHIPNAME"),
                    Destination = Str(row, "DESTINATION"),
                    CallSign = Str(row, "CALLSIGN"),
                    ShipType = Str(row, "TYPE_NAME"),
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MarineTraffic poll failed");
        }

        return results;
    }

    // MarineTraffic returns numeric fields as JSON strings; parse leniently.
    private static double? Num(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && double.TryParse(AsString(v), out var d) ? d : null;

    private static bool TryLong(JsonElement row, string name, out long value)
    {
        value = 0;
        return row.TryGetProperty(name, out var v) && long.TryParse(AsString(v), out value);
    }

    private static string? Str(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) ? AsString(v) : null;

    private static string? AsString(JsonElement v) =>
        v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
}
