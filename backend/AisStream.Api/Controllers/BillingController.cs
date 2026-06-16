using AisStream.Api.Auth;
using AisStream.Api.Data;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Controllers;

/// <summary>
/// Billing webhook endpoint. In production a billing provider (e.g. Stripe) calls this to set
/// a user's tier after a successful payment. This scaffold authenticates with a shared secret
/// and sets the tier by email; swap the body handling for real Stripe event + signature
/// verification when wiring a provider. Inert until a webhook secret is configured.
/// </summary>
[ApiController]
[Route("api/billing")]
[AllowAnonymous]
public class BillingController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BillingOptions _options;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        UserManager<ApplicationUser> userManager,
        IOptions<BillingOptions> options,
        ILogger<BillingController> logger)
    {
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
    }

    public record WebhookEvent(string Email, SubscriptionTier Tier);

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] WebhookEvent body)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Billing webhook is not configured." });
        }

        var provided = Request.Headers["X-Webhook-Secret"].ToString();
        if (!CryptographicEquals(provided, _options.WebhookSecret))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByEmailAsync(body.Email);
        if (user is null)
        {
            return NotFound(new { error = "Unknown user." });
        }

        user.Tier = body.Tier;
        await _userManager.UpdateAsync(user);
        _logger.LogInformation("Billing set {Email} to {Tier}", body.Email, body.Tier);
        return NoContent();
    }

    private static bool CryptographicEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}
