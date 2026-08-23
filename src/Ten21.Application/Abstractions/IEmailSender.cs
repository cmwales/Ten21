namespace Ten21.Application.Abstractions;

/// <summary>
/// Sends transactional email (US-16: activation links, password resets; US-17: OTP codes).
/// Interfaced so AuthController never depends on SMTP directly and tests never need a real
/// mailbox -- same reasoning as every other external-service seam in this codebase
/// (ITurnstileVerificationService, IGoogleIdTokenVerifier).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
