using System.Text.Json;
using AisStream.Api.Auth;
using AisStream.Api.Data;
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
    private readonly AisStreamOptions _options;

    public VesselsController(
        VesselStore store,
        AppDbContext db,
        IDistributedCache cache,
        IOptions<AisStreamOptions> options)
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

        var envelope = VesselMapping.GeometryFactory.ToGeometry(
            new Envelope(bounds.LonMin, bounds.LonMax, bounds.LatMin, bounds.LatMax));
        var cutoff = DateTimeOffset.UtcNow - maxAge;

        var vessels = await _db.Vessels
            .AsNoTracking()
            .Where(v => v.LastUpdate >= cutoff && envelope.Contains(v.Location))
            .Select(v => VesselMapping.ToDto(v))
            .ToListAsync();

        return Ok(vessels);
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

    /// <summary>Grid cell size in degrees for a Leaflet zoom level (~64px clusters).</summary>
    private static double ClusterCellDegrees(int zoom)
    {
        var z = Math.Clamp(zoom, 0, 20);
        return 360.0 / (256.0 * Math.Pow(2, z)) * 64.0;
    }

    /// <summary>Historical track for a vessel, limited to the caller's tier history window.</summary>
    [HttpGet("{mmsi:long}/track")]
    public async Task<ActionResult<object>> GetTrack(long mmsi, [FromQuery] double? hours)
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

        return Ok(new { mmsi, windowHours = requested.TotalHours, points });
    }

    [HttpGet("/api/status")]
    public object GetStatus() => new
    {
        Mode = string.IsNullOrWhiteSpace(_options.ApiKey) ? "simulation" : "live",
        VesselCount = _store.Count,
        Tier = TokenService.TierOf(User).ToString(),
    };
}
