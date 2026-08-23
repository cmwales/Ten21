using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Email;

/// <summary>
/// Real SMTP delivery via System.Net.Mail.SmtpClient -- no extra NuGet dependency for what
/// the story calls a "temporary SMTP gateway" (cmwales@gmail.com today). Uses STARTTLS on
/// port 587 by default, which is what a Gmail App Password-authenticated connection needs
/// (Smtp:Password must be an App Password, not the account's real password -- Gmail
/// rejects plain-password SMTP auth outright; see README for how to generate one).
/// Registered only when Smtp:Username/Smtp:Password are both configured
/// (EmailServiceCollectionExtensions) -- ConsoleEmailSender is the fallback otherwise.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly string _fromAddress;
    private readonly string _fromName;

    public SmtpEmailSender(IConfiguration configuration)
    {
        _host = configuration["Smtp:Host"] ?? "smtp.gmail.com";
        _port = int.TryParse(configuration["Smtp:Port"], out var port) ? port : 587;
        _username = configuration["Smtp:Username"]
            ?? throw new InvalidOperationException("Smtp:Username is not configured.");
        _password = configuration["Smtp:Password"]
            ?? throw new InvalidOperationException("Smtp:Password is not configured.");
        _fromAddress = configuration["Smtp:FromAddress"] ?? _username;
        _fromName = configuration["Smtp:FromName"] ?? "Ten21";
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_host, _port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_username, _password),
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_fromAddress, _fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(toEmail);

        // SmtpClient's own SendMailAsync predates CancellationToken support; honor
        // cancellation at least before the network call starts.
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
    }
}
