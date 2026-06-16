namespace AisStream.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "AisStream.Api";
    public string Audience { get; set; } = "AisStream.Client";

    /// <summary>Signing key. MUST be overridden in production via configuration/secret.</summary>
    public string Key { get; set; } = "dev-only-insecure-signing-key-change-me-please-32+chars";

    /// <summary>Access-token lifetime (short-lived; renew via the refresh token).</summary>
    public int ExpiryHours { get; set; } = 2;

    /// <summary>Refresh-token lifetime in days.</summary>
    public int RefreshDays { get; set; } = 30;
}
