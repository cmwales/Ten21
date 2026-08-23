namespace Ten21.Application.Abstractions;

/// <summary>
/// Verifies a Cloudflare Turnstile response token server-side (US-18). Interfaced for the
/// same reason as IS3StorageService: a real HTTP call to a third party is not something
/// tests should make, and the concrete verification mechanism is a legitimate candidate to
/// change later (Cloudflare Turnstile today, something else if that ever changes).
/// </summary>
public interface ITurnstileVerificationService
{
    Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken = default);
}
