using AisStream.Api.Auth;
using AisStream.Api.Data;
using AisStream.Api.Services;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Controllers;

/// <summary>Admin-only management surface. Requires the Admin role (see DataSeeder).</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly VesselStore _store;
    private readonly AuditService _audit;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        VesselStore store,
        AuditService audit)
    {
        _userManager = userManager;
        _db = db;
        _store = store;
        _audit = audit;
    }

    public record UserRow(string Id, string Email, string Tier, bool IsAdmin, int FollowedCount, int WatchAreas);
    public record SetTierRequest(SubscriptionTier Tier);

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<UserRow>>> Users([FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var users = await _userManager.Users
            .OrderBy(u => u.Email)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync();

        var followCounts = await _db.FollowedVessels
            .GroupBy(f => f.UserId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var areaCounts = await _db.WatchAreas
            .GroupBy(w => w.UserId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var rows = new List<UserRow>(users.Count);
        foreach (var u in users)
        {
            var isAdmin = await _userManager.IsInRoleAsync(u, Roles.Admin);
            rows.Add(new UserRow(
                u.Id, u.Email!, u.Tier.ToString(), isAdmin,
                followCounts.GetValueOrDefault(u.Id), areaCounts.GetValueOrDefault(u.Id)));
        }

        return Ok(rows);
    }

    [HttpPost("users/{id}/tier")]
    public async Task<IActionResult> SetTier(string id, SetTierRequest request)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        user.Tier = request.Tier;
        await _userManager.UpdateAsync(user);
        await _audit.RecordAsync(User, "SetTier", $"{user.Email} -> {request.Tier}");
        return NoContent();
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (await _userManager.IsInRoleAsync(user, Roles.Admin))
        {
            return BadRequest(new { error = "Admin accounts cannot be deleted from the dashboard." });
        }

        await _userManager.DeleteAsync(user);
        await _audit.RecordAsync(User, "DeleteUser", user.Email);
        return NoContent();
    }

    [HttpGet("audit")]
    public async Task<ActionResult<object>> Audit([FromQuery] int take = 100)
    {
        var entries = await _db.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .Take(Math.Clamp(take, 1, 500))
            .Select(a => new { a.Timestamp, a.Actor, a.Action, a.Detail })
            .ToListAsync();
        return Ok(entries);
    }

    [HttpGet("stats")]
    public async Task<ActionResult<object>> Stats()
    {
        var usersByTier = await _userManager.Users
            .GroupBy(u => u.Tier)
            .Select(g => new { Tier = g.Key, Count = g.Count() })
            .ToListAsync();

        return Ok(new
        {
            totalUsers = await _userManager.Users.CountAsync(),
            usersByTier = usersByTier.ToDictionary(x => x.Tier.ToString(), x => x.Count),
            watchAreas = await _db.WatchAreas.CountAsync(),
            followedVessels = await _db.FollowedVessels.CountAsync(),
            vesselsInDatabase = await _db.Vessels.CountAsync(),
            trackPoints = await _db.TrackPoints.CountAsync(),
            vesselsInCache = _store.Count,
        });
    }
}
