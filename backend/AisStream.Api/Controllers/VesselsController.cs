using System.Text.Json;
using AisStream.Api.Auth;
using AisStream.Api.Data;
using AisStream.Api.Ingestion;
using AisStream.Api.Models;
using AisStream.Api.Services;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace AisStream.Api.Controllers;

[ApiController]
[Route("api/vessels")]
[AllowAnonymous]
public class VesselsController : ControllerBase
{
    private readonly VesselStore _store;
    private readonly AppDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly IngestionOptions _options;

    public VesselsController(
        VesselStore store,
        AppDbContext db,
        IDistributedCache cache,
        IOptions<IngestionOptions> options)
    {
        _store = store;
        _db = db;
        _cache = cache;
        _options = options.Value;
    }

    /// <summary>
    /// Global vessel search by name (case-insensitive) or MMSI, across all tracked vessels —
    /// not just the current viewport. Returns up to 25 recent matches.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<Vessel>>> Search([FromQuery] string q)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length < 2)
        {
            return Ok(Array.Empty<Vessel>());
        }

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(_options.VesselTtlMinutes);
        var pattern = $"%{term}%";
        var query = _db.Vessels.AsNoTracking().Where(v => v.LastUpdate >= cutoff);

        query = long.TryParse(term, out var mmsi)
            ? query.Where(v => v.Mmsi == mmsi || (v.Name != null && EF.Functions.ILike(v.Name, pattern)))
            : query.Where(v => v.Name != null && EF.Functions.ILike(v.Name, pattern));

        var results = await query
            .OrderBy(v => v.Name)
            .Take(25)
            .Select(v => VesselMapping.ToDto(v))
            .ToListAsync();

        return Ok(results);
    }

    /// <summary>Live fleet statistics from the warm cache: totals and breakdowns by type/status.</summary>
    [HttpGet("stats")]
    public ActionResult<object> Stats()
    {
        var vessels = _store.Snapshot(TimeSpan.FromMinutes(_options.VesselTtlMinutes));
        var moving = vessels.Count(v => (v.SpeedOverGround ?? 0) > 0.5);

        return Ok(new
        {
            total = vessels.Count,
            moving,
            stopped = vessels.Count - moving,
            byShipType = vessels
                .GroupBy(v => v.ShipType ?? "Unknown")
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count()),
            withDestination = vessels.Count(v => !string.IsNullOrEmpty(v.Destination)),
        });
    }

    /// <summary>Exports the vessels in a viewport as CSV (tier-gated, same as the JSON endpoint).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] double latMin,
        [FromQuery] double lonMin,
        [FromQuery] double latMax,
        [FromQuery] double lonMax)
    {
        var bounds = new Bounds(latMin, lonMin, latMax, lonMax);
        var limits = TierLimits.For(TokenService.TierOf(User));
        if (bounds.AreaSqDegrees > limits.MaxViewportAreaSqDegrees)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Requested area exceeds your plan limit." });
        }

        var vessels = await QueryBoundsAsync(bounds);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("mmsi,name,latitude,longitude,speedKn,courseDeg,shipType,destination,lastUpdate");
        foreach (var v in vessels)
        {
            sb.Append(v.Mmsi).Append(',')
              .Append(Csv(v.Name)).Append(',')
              .Append(v.Latitude.ToString("0.#####")).Append(',')
              .Append(v.Longitude.ToString("0.#####")).Append(',')
              .Append(v.SpeedOverGround?.ToString("0.#") ?? "").Append(',')
              .Append(v.CourseOverGround?.ToString("0") ?? "").Append(',')
              .Append(Csv(v.ShipType)).Append(',')
              .Append(Csv(v.Destination)).Append(',')
              .Append(v.LastUpdate.ToString("o"))
              .Append('\n');
        }

        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "vessels.csv");
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    /// <summary>
    /// Vessels inside the given bounds, gated by the caller's tier viewport-area limit.
    /// Backed by the PostGIS GIST index via an envelope query so it stays fast at scale.
    /// Without bounds, returns the (Free-capped) warm cache snapshot.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Vessel>>> Get(
        [FromQuery] double? latMin,
        [FromQuery] double? lonMin,
        [FromQuery] double? latMax,
        [FromQuery] double? lonMax)
    {
        var maxAge = TimeSpan.FromMinutes(_options.VesselTtlMinutes);
        if (latMin is null || lonMin is null || latMax is null || lonMax is null)
        {
            return Ok(_store.Snapshot(maxAge).Take(500).ToList());
        }

        var bounds = new Bounds(latMin.Value, lonMin.Value, latMax.Value, lonMax.Value);
        var limits = TierLimits.For(TokenService.TierOf(User));
        if (bounds.AreaSqDegrees > limits.MaxViewportAreaSqDegrees)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = $"Requested area exceeds the {limits.Tier} plan limit of " +
                        $"{limits.MaxViewportAreaSqDegrees:0} square degrees.",
            });
        }

        return Ok(await QueryBoundsAsync(bounds));
    }

    private async Task<List<Vessel>> QueryBoundsAsync(Bounds bounds)
    {
        var envelope = VesselMapping.GeometryFactory.ToGeometry(
            new Envelope(bounds.LonMin, bounds.LonMax, bounds.LatMin, bounds.LatMax));
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(_options.VesselTtlMinutes);

        return await _db.Vessels
            .AsNoTracking()
            .Where(v => v.LastUpdate >= cutoff && envelope.Contains(v.Location))
            .Select(v => VesselMapping.ToDto(v))
            .ToListAsync();
    }

    /// <summary>
    /// Aggregated vessel clusters for a viewport, for low-zoom / wide-area views where
    /// rendering every ship would be unusable. Vessels are snapped to a grid (cell size
    /// derived from the map zoom) by PostGIS and returned as centroid + count per cell.
    /// Available at any tier since it returns only aggregates, not individual positions.
    /// </summary>
    [HttpGet("clusters")]
    public async Task<ActionResult<object>> GetClusters(
        [FromQuery] double latMin,
        [FromQuery] double lonMin,
        [FromQuery] double latMax,
        [FromQuery] double lonMax,
        [FromQuery] int zoom)
    {
        var cell = ClusterCellDegrees(zoom);
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(_options.VesselTtlMinutes);

        // Short-TTL cache keyed by coarse bounds + zoom: at scale, many users panning the
        // same area share one PostGIS aggregation instead of each triggering a query.
        var cacheKey = $"clusters:{latMin:F1}:{lonMin:F1}:{latMax:F1}:{lonMax:F1}:{zoom}";
        var cached = await _cache.GetStringAsync(cacheKey);
        if (cached is not null)
        {
            return Content(cached, "application/json");
        }

        const string sql = """
            SELECT count(*)::bigint AS count,
                   ST_Y(ST_Centroid(ST_Collect("Location"))) AS lat,
                   ST_X(ST_Centroid(ST_Collect("Location"))) AS lon
            FROM "Vessels"
            WHERE "LastUpdate" >= @cutoff
              AND "Location" && ST_MakeEnvelope(@lonMin, @latMin, @lonMax, @latMax, 4326)
            GROUP BY ST_SnapToGrid("Location", @cell)
            """;

        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        AddParam(command, "cutoff", cutoff);
        AddParam(command, "lonMin", lonMin);
        AddParam(command, "latMin", latMin);
        AddParam(command, "lonMax", lonMax);
        AddParam(command, "latMax", latMax);
        AddParam(command, "cell", cell);

        await _db.Database.OpenConnectionAsync();
        var clusters = new List<object>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                clusters.Add(new
                {
                    count = reader.GetInt64(0),
                    latitude = reader.GetDouble(1),
                    longitude = reader.GetDouble(2),
                });
            }
        }

        var payload = JsonSerializer.Serialize(new { cellDegrees = cell, clusters });
        await _cache.SetStringAsync(cacheKey, payload, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(8),
        });
        return Content(payload, "application/json");
    }

    private static void AddParam(System.Data.Common.DbCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        command.Parameters.Add(p);
    }

    /// <summary>
    /// Historical fleet snapshot: each vessel's most recent position at or before <paramref name="at"/>
    /// within the bounds (for time-scrubbing / playback). Clamped to the caller's tier history
    /// window and viewport-area limit; backed by the PostGIS GIST index on track points.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<object>> GetHistory(
        [FromQuery] double latMin,
        [FromQuery] double lonMin,
        [FromQuery] double latMax,
        [FromQuery] double lonMax,
        [FromQuery] DateTimeOffset at)
    {
        var bounds = new Bounds(latMin, lonMin, latMax, lonMax);
        var limits = TierLimits.For(TokenService.TierOf(User));
        if (bounds.AreaSqDegrees > limits.MaxViewportAreaSqDegrees)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Requested area exceeds your plan limit." });
        }

        var now = DateTimeOffset.UtcNow;
        var earliest = now - limits.MaxTrackHistory;
        if (at > now) at = now;
        if (at < earliest) at = earliest;

        const string sql = """
            SELECT DISTINCT ON (t."Mmsi")
                   t."Mmsi" AS mmsi,
                   ST_Y(t."Location") AS lat,
                   ST_X(t."Location") AS lon,
                   t."SpeedOverGround" AS sog,
                   t."CourseOverGround" AS cog,
                   v."Name" AS name,
                   v."ShipType" AS shiptype
            FROM "TrackPoints" t
            LEFT JOIN "Vessels" v ON v."Mmsi" = t."Mmsi"
            WHERE t."Timestamp" <= @at AND t."Timestamp" >= @earliest
              AND t."Location" && ST_MakeEnvelope(@lonMin, @latMin, @lonMax, @latMax, 4326)
            ORDER BY t."Mmsi", t."Timestamp" DESC
            """;

        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        AddParam(command, "at", at);
        AddParam(command, "earliest", earliest);
        AddParam(command, "lonMin", bounds.LonMin);
        AddParam(command, "latMin", bounds.LatMin);
        AddParam(command, "lonMax", bounds.LonMax);
        AddParam(command, "latMax", bounds.LatMax);

        await _db.Database.OpenConnectionAsync();
        var vessels = new List<object>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                vessels.Add(new
                {
                    mmsi = reader.GetInt64(0),
                    latitude = reader.GetDouble(1),
                    longitude = reader.GetDouble(2),
                    speedOverGround = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3),
                    courseOverGround = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4),
                    name = reader.IsDBNull(5) ? null : reader.GetString(5),
                    shipType = reader.IsDBNull(6) ? null : reader.GetString(6),
                });
            }
        }

        return Ok(new { at, count = vessels.Count, vessels });
    }

    /// <summary>Grid cell size in degrees for a Leaflet zoom level (~64px clusters).</summary>
    private static double ClusterCellDegrees(int zoom)
    {
        var z = Math.Clamp(zoom, 0, 20);
        return 360.0 / (256.0 * Math.Pow(2, z)) * 64.0;
    }

    /// <summary>
    /// Historical track for a vessel, limited to the caller's tier history window. Pass
    /// format=geojson to get a GeoJSON LineString Feature (e.g. for GIS tools / download).
    /// </summary>
    [HttpGet("{mmsi:long}/track")]
    public async Task<ActionResult<object>> GetTrack(long mmsi, [FromQuery] double? hours, [FromQuery] string? format)
    {
        var limits = TierLimits.For(TokenService.TierOf(User));
        var requested = hours is null ? limits.MaxTrackHistory : TimeSpan.FromHours(hours.Value);
        if (requested > limits.MaxTrackHistory)
        {
            requested = limits.MaxTrackHistory;
        }

        var cutoff = DateTimeOffset.UtcNow - requested;
        var points = await _db.TrackPoints
            .AsNoTracking()
            .Where(t => t.Mmsi == mmsi && t.Timestamp >= cutoff)
            .OrderBy(t => t.Timestamp)
            .Select(t => new
            {
                latitude = t.Location.Y,
                longitude = t.Location.X,
                speedOverGround = t.SpeedOverGround,
                courseOverGround = t.CourseOverGround,
                timestamp = t.Timestamp,
            })
            .ToListAsync();

        if (string.Equals(format, "geojson", StringComparison.OrdinalIgnoreCase))
        {
            var geojson = new
            {
                type = "Feature",
                properties = new { mmsi, windowHours = requested.TotalHours },
                geometry = new
                {
                    type = "LineString",
                    coordinates = points.Select(p => new[] { p.longitude, p.latitude }).ToArray(),
                },
            };
            return File(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(geojson)),
                "application/geo+json", $"track-{mmsi}.geojson");
        }

        return Ok(new { mmsi, windowHours = requested.TotalHours, points });
    }

    [HttpGet("/api/status")]
    public async Task<object> GetStatus()
    {
        var providers = await _db.Integrations
            .Where(i => i.Enabled)
            .Select(i => i.Provider)
            .ToListAsync();
        var live = providers.Any(p => p != Ingestion.AisProviderType.Simulator);

        return new
        {
            Mode = live ? "live" : "simulation",
            Providers = providers.Select(p => p.ToString()).Distinct().ToArray(),
            VesselCount = _store.Count,
            Tier = TokenService.TierOf(User).ToString(),
        };
    }
}
