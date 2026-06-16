using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using AisStream.Api.Auth;
using AisStream.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TokenService _tokenService;
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwt;
    private readonly Services.IEmailSender _email;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        TokenService tokenService,
        AppDbContext db,
        IOptions<JwtOptions> jwt,
        Services.IEmailSender email)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _db = db;
        _jwt = jwt.Value;
        _email = email;
    }

    public record AuthRequest([Required, EmailAddress] string Email, [Required, MinLength(8)] string Password);
    public record AuthResponse(string Token, DateTime ExpiresAt, string RefreshToken, string Email, string Tier);
    public record RefreshRequest([Required] string RefreshToken);

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(AuthRequest request)
    {
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        return await IssueAsync(user);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(AuthRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        return await IssueAsync(user);
    }

    /// <summary>Exchanges a valid refresh token for a new access token, rotating the refresh token.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request)
    {
        var existing = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken);

        if (existing is null || !existing.IsActive)
        {
            return Unauthorized(new { error = "Invalid or expired refresh token." });
        }

        existing.RevokedAt = DateTimeOffset.UtcNow; // rotate
        return await IssueAsync(existing.User);
    }

    public record ForgotPasswordRequest([Required, EmailAddress] string Email);
    public record ResetPasswordRequest(
        [Required, EmailAddress] string Email, [Required] string Token, [Required, MinLength(8)] string NewPassword);

    /// <summary>Emails a password-reset token. Always returns 200 to avoid leaking which emails exist.</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            await _email.SendAsync(request.Email, "Reset your AIS Tracker password",
                $"Use this token to reset your password:\n\n{token}");
        }

        return Ok(new { message = "If that email exists, a reset link has been sent." });
    }

    /// <summary>Resets the password using a token from the reset email; revokes existing sessions.</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return BadRequest(new { error = "Invalid reset request." });
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        // Invalidate existing refresh tokens after a password reset.
        await _db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow));

        return Ok(new { message = "Password reset successfully." });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request)
    {
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == request.RefreshToken);
        if (token is not null && token.RevokedAt is null)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }

    private async Task<ActionResult<AuthResponse>> IssueAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var (token, expiresAt) = _tokenService.CreateToken(user, roles);

        var refresh = new RefreshToken
        {
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            UserId = user.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshDays),
        };
        _db.RefreshTokens.Add(refresh);
        await _db.SaveChangesAsync();

        return Ok(new AuthResponse(token, expiresAt, refresh.Token, user.Email!, user.Tier.ToString()));
    }
}
