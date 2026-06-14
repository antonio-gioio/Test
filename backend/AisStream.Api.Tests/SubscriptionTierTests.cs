using AisStream.Api.Subscriptions;

namespace AisStream.Api.Tests;

public class SubscriptionTierTests
{
    [Fact]
    public void Free_has_the_tightest_limits()
    {
        var free = TierLimits.For(SubscriptionTier.Free);

        Assert.Equal(4, free.MaxViewportAreaSqDegrees);
        Assert.Equal(TimeSpan.FromHours(1), free.MaxTrackHistory);
        Assert.Equal(RefreshCadence.Slow, free.Cadence);
        Assert.Equal(3, free.MaxFollowedVessels);
    }

    [Fact]
    public void Pro_is_faster_and_wider_than_Free()
    {
        var pro = TierLimits.For(SubscriptionTier.Pro);

        Assert.True(pro.MaxViewportAreaSqDegrees > TierLimits.For(SubscriptionTier.Free).MaxViewportAreaSqDegrees);
        Assert.Equal(RefreshCadence.Fast, pro.Cadence);
    }

    [Fact]
    public void Enterprise_is_effectively_unlimited()
    {
        var ent = TierLimits.For(SubscriptionTier.Enterprise);

        Assert.True(double.IsPositiveInfinity(ent.MaxViewportAreaSqDegrees));
        Assert.Equal(int.MaxValue, ent.MaxFollowedVessels);
        Assert.Equal(TimeSpan.FromDays(30), ent.MaxTrackHistory);
    }

    [Fact]
    public void Unknown_tier_falls_back_to_Free()
    {
        var limits = TierLimits.For((SubscriptionTier)999);
        Assert.Equal(SubscriptionTier.Free, limits.Tier);
    }
}
