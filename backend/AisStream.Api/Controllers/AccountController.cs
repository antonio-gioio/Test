using System.Security.Claims;
using AisStream.Api.Auth;
using AisStream.Api.Data;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly TokenService _tokenService;

    public AccountController(UserManager<ApplicationUser> userManager, AppDbContext db, TokenService tokenService)
    {
        _userManager = userManager;
        _db = db;
        _tokenService = tokenService;
    }

    public record AccountResponse(string Email, string Tier, object Limits, IReadOnlyList<long> FollowedMmsis);
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

        var followed = await _db.FollowedVessels
            .Where(f => f.UserId == user.Id)
            .Select(f => f.Mmsi)
            .ToListAsync();

        return Ok(Describe(user, followed));
    }

    /// <summary>
    /// Changes the caller's subscription tier and returns a fresh token carrying the new
    /// tier claim. In a real product this would be driven by a billing webhook, not the
    /// client; it is exposed here so the tiers can be exercised end-to-end.
    /// </summary>
    [HttpPost("tier")]
    public async Task<ActionResult<TokenResponse>> ChangeTier(ChangeTierRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        user.Tier = request.Tier;
        await _userManager.UpdateAsync(user);

        var (token, expiresAt) = _tokenService.CreateToken(user);
        return Ok(new TokenResponse(token, expiresAt, user.Tier.ToString()));
    }

    private Task<ApplicationUser?> CurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? Task.FromResult<ApplicationUser?>(null) : _userManager.FindByIdAsync(userId);
    }

    private static AccountResponse Describe(ApplicationUser user, IReadOnlyList<long> followed)
    {
        var limits = TierLimits.For(user.Tier);
        return new AccountResponse(
            user.Email!,
            user.Tier.ToString(),
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
