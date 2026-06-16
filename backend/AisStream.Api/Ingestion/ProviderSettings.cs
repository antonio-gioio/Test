using System.Text.Json;
using AisStream.Api.Data;

namespace AisStream.Api.Ingestion;

/// <summary>Resolved, provider-agnostic settings for one integration (parsed from the DB row).</summary>
public record ProviderSettings(
    string Name,
    string? ApiKey,
    string? Url,
    double[][][] BoundingBoxes,
    long[] MmsiFilter,
    int PollSeconds,
    double CenterLat,
    double CenterLon,
    double RadiusKm)
{
    private static readonly double[][][] World = [[[-90, -180], [90, 180]]];

    public static ProviderSettings From(Integration i)
    {
        var boxes = Parse<double[][][]>(i.BoundingBoxesJson) ?? World;
        var mmsis = Parse<long[]>(i.MmsiFilterJson) ?? Array.Empty<long>();
        return new ProviderSettings(
            i.Name,
            string.IsNullOrWhiteSpace(i.ApiKey) ? null : i.ApiKey,
            string.IsNullOrWhiteSpace(i.Url) ? null : i.Url,
            boxes.Length > 0 ? boxes : World,
            mmsis,
            i.PollSeconds <= 0 ? 60 : i.PollSeconds,
            i.CenterLat,
            i.CenterLon,
            i.RadiusKm <= 0 ? 100 : i.RadiusKm);
    }

    /// <summary>True if no MMSI filter is set or the MMSI is in the allow-list.</summary>
    public bool Allows(long mmsi) => MmsiFilter.Length == 0 || Array.IndexOf(MmsiFilter, mmsi) >= 0;

    private static T? Parse<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
