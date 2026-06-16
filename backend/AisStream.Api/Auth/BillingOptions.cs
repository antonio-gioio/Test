namespace AisStream.Api.Auth;

/// <summary>
/// Controls how subscription tiers can change. By default, self-service tier switching is
/// only allowed outside Production (handy for demos/testing); in Production the tier must be
/// set by an admin or a billing webhook, so users can't grant themselves a paid plan for free.
/// </summary>
public class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Null = allow self-service only in non-Production. Set true/false to force it.</summary>
    public bool? AllowSelfServiceTier { get; set; }

    /// <summary>Shared secret for the billing webhook (e.g. a Stripe webhook signing secret).</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Whether users may change their own tier (default: only outside Production).</summary>
    public bool SelfServiceAllowed(bool isProduction) => AllowSelfServiceTier ?? !isProduction;
}
