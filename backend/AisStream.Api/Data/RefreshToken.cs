namespace AisStream.Api.Data;

/// <summary>
/// A long-lived refresh token (rotated on use) that lets a client obtain a new short-lived
/// access token without re-entering credentials. Stored server-side so it can be revoked.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }
    public string Token { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
