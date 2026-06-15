using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AisStream.Api.Data;
using AisStream.Api.Subscriptions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AisStream.Api.Auth;

/// <summary>Issues JWTs that carry the user's id and current subscription tier.</summary>
public class TokenService
{
    public const string TierClaim = "tier";

    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public (string Token, DateTime ExpiresAt) CreateToken(ApplicationUser user, IEnumerable<string>? roles = null)
    {
        var expiresAt = DateTime.UtcNow.AddHours(_options.ExpiryHours);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(TierClaim, user.Tier.ToString()),
        };

        foreach (var role in roles ?? Enumerable.Empty<string>())
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>Reads the tier from a principal's claims, defaulting to Free for guests.</summary>
    public static SubscriptionTier TierOf(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(TierClaim)?.Value;
        return Enum.TryParse<SubscriptionTier>(raw, out var tier) ? tier : SubscriptionTier.Free;
    }
}
