namespace Ten21.Application.Abstractions;

/// <summary>
/// Strips HTML/script content from free-text user input before it reaches the database
/// (US-19, US-21 acceptance criteria: "Server-side HTML/XSS sanitization is applied to all
/// string inputs prior to database persistence"). Interfaced for the same reason as every
/// other external-library seam in this codebase (IEmailSender, ITurnstileVerificationService)
/// -- controllers depend on the abstraction, not directly on whichever sanitizer library is
/// wired up in Infrastructure.
/// </summary>
public interface IInputSanitizer
{
    /// <summary>Returns <paramref name="value"/> with all HTML tags/attributes removed. A
    /// null input returns null; these are plain-text fields (property names, addresses, unit
    /// identifiers) that should never contain markup at all, so this strips rather than
    /// encodes.</summary>
    string? Sanitize(string? value);
}
