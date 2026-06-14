using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Identity;

namespace AisStream.Api.Data;

/// <summary>Application user with a subscription tier and a list of followed vessels.</summary>
public class ApplicationUser : IdentityUser
{
    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Free;

    public List<FollowedVessel> FollowedVessels { get; set; } = new();
}

/// <summary>A vessel a user has chosen to follow (join row).</summary>
public class FollowedVessel
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;
    public long Mmsi { get; set; }
    public DateTimeOffset FollowedAt { get; set; } = DateTimeOffset.UtcNow;
}
