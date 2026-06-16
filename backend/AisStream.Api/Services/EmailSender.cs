using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Services;

/// <summary>SMTP settings. If Host is empty, emails are logged instead of sent.</summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "no-reply@aisstream.local";
    public bool UseSsl { get; set; } = true;

    public bool Configured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>Sends transactional emails. Swap the implementation for a real SMTP/API provider.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}

/// <summary>Delivers email over SMTP. Selected when Email:Host is configured.</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.UseSsl };
            if (!string.IsNullOrEmpty(_options.User))
            {
                client.Credentials = new NetworkCredential(_options.User, _options.Password);
            }

            using var message = new MailMessage(_options.From, to, subject, body);
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
        }
    }
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
