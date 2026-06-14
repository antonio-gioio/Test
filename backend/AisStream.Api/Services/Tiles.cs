using AisStream.Api.Subscriptions;

namespace AisStream.Api.Services;

public readonly record struct Bounds(double LatMin, double LonMin, double LatMax, double LonMax)
{
    public double AreaSqDegrees => Math.Max(0, LatMax - LatMin) * Math.Max(0, LonMax - LonMin);

    public bool Contains(double lat, double lon) =>
        lat >= LatMin && lat <= LatMax && lon >= LonMin && lon <= LonMax;
}

/// <summary>
/// Maps lat/lon to fixed 1°x1° tiles. Vessel updates are fanned out to per-tile
/// SignalR groups so a connection only receives traffic for the area it is watching.
/// Group names are additionally namespaced by refresh cadence so a Free-tier viewer
/// and a Pro-tier viewer of the same tile get different update rates.
/// </summary>
public static class Tiles
{
    public const double TileSizeDegrees = 1.0;

    public static (int Lat, int Lon) TileOf(double lat, double lon) =>
        ((int)Math.Floor(lat / TileSizeDegrees), (int)Math.Floor(lon / TileSizeDegrees));

    public static string GroupName(RefreshCadence cadence, int tileLat, int tileLon) =>
        $"t:{(cadence == RefreshCadence.Fast ? "f" : "s")}:{tileLat}:{tileLon}";

    /// <summary>Enumerates every tile the bounds touch, capped to avoid runaway joins.</summary>
    public static IEnumerable<(int Lat, int Lon)> TilesCovering(Bounds bounds, int maxTiles = 4096)
    {
        var (latMin, lonMin) = TileOf(bounds.LatMin, bounds.LonMin);
        var (latMax, lonMax) = TileOf(bounds.LatMax, bounds.LonMax);

        var count = 0;
        for (var lat = latMin; lat <= latMax; lat++)
        {
            for (var lon = lonMin; lon <= lonMax; lon++)
            {
                if (++count > maxTiles)
                {
                    yield break;
                }

                yield return (lat, lon);
            }
        }
    }
}
