namespace AisStream.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "AisStream.Api";
    public string Audience { get; set; } = "AisStream.Client";

    /// <summary>Signing key. MUST be overridden in production via configuration/secret.</summary>
    public string Key { get; set; } = "dev-only-insecure-signing-key-change-me-please-32+chars";

    public int ExpiryHours { get; set; } = 24;
}
