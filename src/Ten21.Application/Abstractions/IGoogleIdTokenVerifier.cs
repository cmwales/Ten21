namespace Ten21.Application.Abstractions;

/// <summary>
/// Verifies a Google-issued ID token server-side (US-15). Interfaced for the same reason
/// as ITurnstileVerificationService: the real implementation calls out to Google's own
/// signature-verification library, which tests should never depend on, and a fake
/// Google-signed JWT can't be fabricated in a test anyway.
/// </summary>
public interface IGoogleIdTokenVerifier
{
    /// <summary>Returns null if the token is invalid/expired/wrong-audience, or if
    /// Google:ClientId isn't configured in this environment at all -- never throws for a
    /// merely-bad-or-unconfigured token, only for genuine infrastructure failures.</summary>
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken = default);
}

public record GoogleIdentity(
    string Subject, string Email, bool EmailVerified, string? GivenName, string? FamilyName);
