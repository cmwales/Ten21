using Microsoft.Extensions.Logging;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Email;

/// <summary>
/// Dev-mode IEmailSender: logs the email instead of sending it. Registered whenever
/// Smtp:Username/Smtp:Password aren't configured (EmailServiceCollectionExtensions) --
/// deliberately a config-presence check, not an environment check, so a real SMTP gateway
/// can still be tested locally in Development the moment real credentials are set, and a
/// misconfigured non-Development environment still degrades to logging rather than
/// throwing. Logged at Information so `dotnet run`'s console output IS the "inbox" for
/// local testing -- copy the activation/reset link straight out of the terminal.
/// </summary>
public class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "---- DEV EMAIL (Smtp:Username/Smtp:Password not configured) ----\n" +
            "To: {ToEmail}\nSubject: {Subject}\n{HtmlBody}\n" +
            "------------------------------------------------------------------",
            toEmail, subject, htmlBody);

        return Task.CompletedTask;
    }
}
