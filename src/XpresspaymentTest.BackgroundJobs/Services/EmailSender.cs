using Microsoft.Extensions.Options;

namespace XpresspaymentTest.BackgroundJobs.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, string? attachmentPath = null, CancellationToken ct = default);
}

public class EmailSettings
{
    public string From { get; set; } = "noreply@xpresstest.local";
    public string OpsEmail { get; set; } = "ops@xpresstest.local";

    // 0.0 - 1.0. Lets us watch Hangfire retry without pulling the network cable out
    public double SimulatedFailureRate { get; set; }
}

/// <summary>
/// Stand-in for an SMTP / SendGrid client. Logs instead of sending, and fails some of
/// the time on purpose so the retry behaviour is visible in the dashboard.
/// </summary>
public class FakeEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;
    private readonly ILogger<FakeEmailSender> _logger;

    public FakeEmailSender(IOptions<EmailSettings> settings, ILogger<FakeEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body, string? attachmentPath = null, CancellationToken ct = default)
    {
        // pretend to talk to the mail server
        await Task.Delay(300, ct);

        if (Random.Shared.NextDouble() < _settings.SimulatedFailureRate)
            throw new EmailDeliveryException($"Mail server timed out while sending to {to}");

        _logger.LogInformation("Email sent from {From} to {To}: '{Subject}' (attachment: {Attachment})",
            _settings.From, to, subject, attachmentPath ?? "none");
    }
}

public class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string message) : base(message) { }
}
