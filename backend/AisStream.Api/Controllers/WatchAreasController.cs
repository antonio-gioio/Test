using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AisStream.Api.Data;
using AisStream.Api.Models;
using AisStream.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Controllers;

/// <summary>User geofences. Vessels entering a watch area trigger a live SignalR "AreaAlert".</summary>
[ApiController]
[Route("api/watch-areas")]
[Authorize]
public class WatchAreasController : ControllerBase
{
    private const int MaxAreasPerUser = 20;

    private readonly AppDbContext _db;
    private readonly VesselStore _store;

    public WatchAreasController(AppDbContext db, VesselStore store)
    {
        _db = db;
        _store = store;
    }

    public record AreaRequest(
        [Required] string Name, double LatMin, double LonMin, double LatMax, double LonMax);
    public record AreaResponse(int Id, string Name, double LatMin, double LonMin, double LatMax, double LonMax);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AreaResponse>>> List()
    {
        var userId = UserId();
        var areas = await _db.WatchAreas
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.Name)
            .Select(w => new AreaResponse(w.Id, w.Name, w.LatMin, w.LonMin, w.LatMax, w.LonMax))
            .ToListAsync();
        return Ok(areas);
    }

    [HttpPost]
    public async Task<ActionResult<AreaResponse>> Create(AreaRequest request)
    {
        var userId = UserId();
        if (await _db.WatchAreas.CountAsync(w => w.UserId == userId) >= MaxAreasPerUser)
        {
            return BadRequest(new { error = $"You can have at most {MaxAreasPerUser} watch areas." });
        }

        var area = new WatchArea
        {
            UserId = userId,
            Name = request.Name.Trim(),
            LatMin = Math.Min(request.LatMin, request.LatMax),
            LatMax = Math.Max(request.LatMin, request.LatMax),
            LonMin = Math.Min(request.LonMin, request.LonMax),
            LonMax = Math.Max(request.LonMin, request.LonMax),
        };
        _db.WatchAreas.Add(area);
        await _db.SaveChangesAsync();

        return Ok(new AreaResponse(area.Id, area.Name, area.LatMin, area.LonMin, area.LatMax, area.LonMax));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = UserId();
        var removed = await _db.WatchAreas.Where(w => w.Id == id && w.UserId == userId).ExecuteDeleteAsync();
        return removed > 0 ? NoContent() : NotFound();
    }

    /// <summary>Vessels currently inside any of the caller's watch areas (from the warm cache).</summary>
    [HttpGet("matches")]
    public async Task<ActionResult<IReadOnlyList<Vessel>>> Matches()
    {
        var userId = UserId();
        var areas = await _db.WatchAreas.Where(w => w.UserId == userId).ToListAsync();
        if (areas.Count == 0)
        {
            return Ok(Array.Empty<Vessel>());
        }

        var matches = _store.Snapshot()
            .Where(v => areas.Any(a => a.Contains(v.Latitude, v.Longitude)))
            .ToList();
        return Ok(matches);
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
