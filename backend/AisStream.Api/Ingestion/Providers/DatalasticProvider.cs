using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Ingestion.Providers;

/// <summary>
/// Datalastic — paid, global AIS REST API with a free trial. Request/response, so the provider
/// polls the configured search circle ("vessel_inarea") on an interval.
/// </summary>
public class DatalasticProvider : IAisProvider
{
    private readonly DatalasticOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<DatalasticProvider> _logger;

    public DatalasticProvider(
        IOptions<DatalasticOptions> options,
        IHttpClientFactory httpFactory,
        ILogger<DatalasticProvider> logger)
    {
        _options = options.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public string Name => "Datalastic";

    public async IAsyncEnumerable<VesselUpdate> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var http = _httpFactory.CreateClient();

        while (!cancellationToken.IsCancellationRequested)
        {
            var url = $"{_options.BaseUrl}/vessel_inarea?api-key={_options.ApiKey}" +
                      $"&lat={_options.Latitude}&lon={_options.Longitude}&radius={_options.RadiusKm}";

            var batch = await FetchAsync(http, url, cancellationToken);
            foreach (var update in batch)
            {
                yield return update;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _options.PollSeconds)), cancellationToken);
        }
    }

    private async Task<List<VesselUpdate>> FetchAsync(HttpClient http, string url, CancellationToken ct)
    {
        var results = new List<VesselUpdate>();
        try
        {
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("vessels", out var vessels) ||
                vessels.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var v in vessels.EnumerateArray())
            {
                if (!v.TryGetProperty("mmsi", out var mmsiEl) ||
                    !long.TryParse(mmsiEl.ToString(), out var mmsi))
                {
                    continue;
                }

                results.Add(new VesselUpdate
                {
                    Mmsi = mmsi,
                    Latitude = Num(v, "lat"),
                    Longitude = Num(v, "lon"),
                    SpeedOverGround = Num(v, "speed"),
                    CourseOverGround = Num(v, "course"),
                    TrueHeading = Num(v, "heading"),
                    Name = Str(v, "name"),
                    Destination = Str(v, "destination"),
                    CallSign = Str(v, "callsign"),
                    ShipType = Str(v, "type"),
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Datalastic poll failed");
        }

        return results;
    }

    private static double? Num(JsonElement v, string name) =>
        v.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;

    private static string? Str(JsonElement v, string name) =>
        v.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
