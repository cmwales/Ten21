namespace Ten21.Api.Contracts.Auth;

public record LoginRequest(string Email, string Password);

/// <summary>US-14: workspace registration. WorkspaceName/PortfolioSize describe the new
/// Tenant being provisioned, not the registrant themselves. TurnstileToken (US-18) is the
/// Cloudflare Turnstile response token from the frontend's "register" widget.</summary>
public record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? PhoneNumber,
    string? Address,
    string WorkspaceName,
    int PortfolioSize,
    bool AgreedToTerms,
    string TurnstileToken);

/// <summary>US-15: the Google-issued ID token from the frontend's Google Sign-In widget.
/// Verified server-side (IGoogleIdTokenVerifier) before anything else happens.</summary>
public record GoogleAuthRequest(string IdToken);

/// <summary>US-15: submitted against an interim (profile_incomplete) token by a first-time
/// Google signup with no workspace yet -- same shape as RegisterRequest minus the fields
/// Google already supplied (name, email, password isn't applicable at all).</summary>
public record CompleteProfileRequest(
    string? PhoneNumber, string? Address, string WorkspaceName, int PortfolioSize);

/// <summary>US-15: returned instead of AuthResponse when a Google signup has no workspace
/// yet -- the client must collect CompleteProfileRequest's fields and call
/// POST /api/auth/complete-profile with this InterimToken before a full AuthResponse
/// (carrying a real tenant_id/role) is ever issued.</summary>
public record ProfileCompletionRequiredResponse(
    bool RequiresProfileCompletion, string InterimToken, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// The refresh token deliberately never appears here -- it only ever travels as an
/// HTTP-only cookie (SECURITY.docx §2), never in a JSON body a script could read.
/// </summary>
public record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    Guid TenantId,
    Guid? OrganizationId,
    string Role);
