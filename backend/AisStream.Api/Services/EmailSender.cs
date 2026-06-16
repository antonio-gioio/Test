namespace AisStream.Api.Services;

/// <summary>Sends transactional emails. Swap the implementation for a real SMTP/API provider.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default sender: logs the message instead of delivering it, so the app works without an email
/// provider configured (e.g. password-reset tokens appear in the logs in development). Replace
/// with an SMTP/SendGrid/SES implementation in production.
/// </summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[email] To: {To} | Subject: {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
