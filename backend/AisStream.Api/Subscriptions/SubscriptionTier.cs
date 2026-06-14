namespace AisStream.Api.Subscriptions;

/// <summary>Paid subscription levels. Each maps to a set of <see cref="TierLimits"/>.</summary>
public enum SubscriptionTier
{
    Free = 0,
    Pro = 1,
    Enterprise = 2,
}

/// <summary>How often updates are pushed to a connection, derived from its tier.</summary>
public enum RefreshCadence
{
    Slow, // Free
    Fast, // Pro / Enterprise
}

/// <summary>
/// The concrete capabilities granted by a <see cref="SubscriptionTier"/>. These are
/// enforced server-side so a client cannot request more area, history, or refresh than
/// its plan allows.
/// </summary>
public record TierLimits(
    SubscriptionTier Tier,
    double MaxViewportAreaSqDegrees,
    TimeSpan MaxTrackHistory,
    RefreshCadence Cadence,
    int MaxFollowedVessels)
{
    public static TierLimits For(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.Pro => new TierLimits(
            tier,
            MaxViewportAreaSqDegrees: 100,
            MaxTrackHistory: TimeSpan.FromHours(24),
            Cadence: RefreshCadence.Fast,
            MaxFollowedVessels: 50),
        SubscriptionTier.Enterprise => new TierLimits(
            tier,
            MaxViewportAreaSqDegrees: double.PositiveInfinity,
            MaxTrackHistory: TimeSpan.FromDays(30),
            Cadence: RefreshCadence.Fast,
            MaxFollowedVessels: int.MaxValue),
        _ => new TierLimits(
            SubscriptionTier.Free,
            MaxViewportAreaSqDegrees: 4,
            MaxTrackHistory: TimeSpan.FromHours(1),
            Cadence: RefreshCadence.Slow,
            MaxFollowedVessels: 3),
    };
}
