using AisStream.Api.Auth;
using AisStream.Api.Data;
using AisStream.Api.Models;
using AisStream.Api.Services;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly AisStreamOptions _options;

    public VesselsController(VesselStore store, AppDbContext db, IOptions<AisStreamOptions> options)
    {
        _store = store;
        _db = db;
        _options = options.Value;
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
