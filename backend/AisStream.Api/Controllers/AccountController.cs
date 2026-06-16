using System.Security.Claims;
using AisStream.Api.Auth;
using AisStream.Api.Data;
using AisStream.Api.Models;
using AisStream.Api.Services;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly TokenService _tokenService;
    private readonly VesselStore _store;
    private readonly bool _selfServiceTier;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        TokenService tokenService,
        VesselStore store,
        IOptions<BillingOptions> billing,
        IWebHostEnvironment env)
    {
        _userManager = userManager;
        _db = db;
        _tokenService = tokenService;
        _store = store;
        _selfServiceTier = billing.Value.SelfServiceAllowed(env.IsProduction());
    }

    public record AccountResponse(
        string Email, string Tier, bool IsAdmin, object Limits, IReadOnlyList<long> FollowedMmsis);
    public record ChangeTierRequest(SubscriptionTier Tier);
    public record TokenResponse(string Token, DateTime ExpiresAt, string Tier);

    [HttpGet("me")]
    public async Task<ActionResult<AccountResponse>> Me()
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        var followed = await FollowedMmsisAsync(user.Id);
        var isAdmin = await _userManager.IsInRoleAsync(user, Roles.Admin);
        return Ok(Describe(user, isAdmin, followed));
    }

    /// <summary>
    /// Self-service tier change for demos/testing. Disabled in Production (where the tier is
    /// set by an admin or the billing webhook), so users cannot grant themselves a paid plan.
    /// </summary>
    [HttpPost("tier")]
    public async Task<ActionResult<TokenResponse>> ChangeTier(ChangeTierRequest request)
    {
        if (!_selfServiceTier)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Self-service tier changes are disabled. Upgrade through billing.",
            });
        }

        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        user.Tier = request.Tier;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expiresAt) = _tokenService.CreateToken(user, roles);
        return Ok(new TokenResponse(token, expiresAt, user.Tier.ToString()));
    }

    /// <summary>Current live positions of the caller's followed vessels (from the warm cache).</summary>
    [HttpGet("followed")]
    public async Task<ActionResult<IReadOnlyList<Vessel>>> Followed()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var mmsis = await FollowedMmsisAsync(userId);
        var vessels = mmsis
            .Select(m => _store.TryGet(m, out var v) ? v : null)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList();
        return Ok(vessels);
    }

    [HttpPut("followed/{mmsi:long}")]
    public async Task<ActionResult<IReadOnlyList<long>>> Follow(long mmsi)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);
        var limits = TierLimits.For(user?.Tier ?? SubscriptionTier.Free);

        var current = await _db.FollowedVessels.Where(f => f.UserId == userId).ToListAsync();
        if (current.All(f => f.Mmsi != mmsi))
        {
            if (current.Count >= limits.MaxFollowedVessels)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = $"Your plan can follow up to {limits.MaxFollowedVessels} vessels.",
                });
            }

            _db.FollowedVessels.Add(new FollowedVessel { UserId = userId, Mmsi = mmsi });
            await _db.SaveChangesAsync();
        }

        return Ok(await FollowedMmsisAsync(userId));
    }

    [HttpDelete("followed/{mmsi:long}")]
    public async Task<ActionResult<IReadOnlyList<long>>> Unfollow(long mmsi)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await _db.FollowedVessels.Where(f => f.UserId == userId && f.Mmsi == mmsi).ExecuteDeleteAsync();
        return Ok(await FollowedMmsisAsync(userId));
    }

    private Task<List<long>> FollowedMmsisAsync(string userId) =>
        _db.FollowedVessels.Where(f => f.UserId == userId).Select(f => f.Mmsi).ToListAsync();

    private Task<ApplicationUser?> CurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? Task.FromResult<ApplicationUser?>(null) : _userManager.FindByIdAsync(userId);
    }

    private static AccountResponse Describe(ApplicationUser user, bool isAdmin, IReadOnlyList<long> followed)
    {
        var limits = TierLimits.For(user.Tier);
        return new AccountResponse(
            user.Email!,
            user.Tier.ToString(),
            isAdmin,
            new
            {
                maxViewportAreaSqDegrees = double.IsInfinity(limits.MaxViewportAreaSqDegrees)
                    ? (double?)null
                    : limits.MaxViewportAreaSqDegrees,
                maxTrackHistoryHours = limits.MaxTrackHistory.TotalHours,
                refreshCadence = limits.Cadence.ToString(),
                maxFollowedVessels = limits.MaxFollowedVessels == int.MaxValue
                    ? (int?)null
                    : limits.MaxFollowedVessels,
            },
            followed);
    }
}
