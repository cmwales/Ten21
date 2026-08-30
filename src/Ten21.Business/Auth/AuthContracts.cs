namespace Ten21.Business.Auth;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Auth.</summary>
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
/// Google signup with no workspace yet.</summary>
public record CompleteProfileRequest(
    string? PhoneNumber, string? Address, string WorkspaceName, int PortfolioSize);

/// <summary>US-15: returned instead of AuthResponse when a Google signup has no workspace
/// yet.</summary>
public record ProfileCompletionRequiredResponse(
    bool RequiresProfileCompletion, string InterimToken, DateTimeOffset ExpiresAtUtc);

public record ResendActivationRequest(string Email);

public record ActivateAccountRequest(Guid UserId, string Token);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

public record GenericAcknowledgementResponse(string Message);

/// <summary>US-17: returned instead of AuthResponse when Login's password check succeeds
/// but a 6-digit emailed code is still required.</summary>
public record TwoFactorRequiredResponse(bool RequiresTwoFactor, string ChallengeToken, DateTimeOffset ExpiresAtUtc);

public record VerifyTwoFactorRequest(string Code);

/// <summary>US-24: returned instead of AuthResponse (or a 2FA challenge) when Login's
/// password check succeeds but ApplicationUser.MustChangePassword is true.</summary>
public record PasswordChangeRequiredResponse(bool RequiresPasswordChange, string ChallengeToken, DateTimeOffset ExpiresAtUtc);

public record ChangeTempPasswordRequest(string NewPassword);

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

/// <summary>
/// Business-layer refactor: pairs a real AuthResponse with the raw refresh token value the
/// controller needs to set the HTTP-only cookie -- the cookie itself (Response-specific)
/// can't be set from inside the service.
/// </summary>
public record AuthTokenResult(AuthResponse Response, string RawRefreshToken);

/// <summary>
/// Business-layer refactor: the three possible outcomes of a Login/ChangeTempPassword call.
/// Exactly one of the three is populated -- the controller checks them in order and maps
/// each to its own response shape.
/// </summary>
public record LoginResult(
    PasswordChangeRequiredResponse? PasswordChangeRequired,
    TwoFactorRequiredResponse? TwoFactorRequired,
    AuthTokenResult? Tokens)
{
    public static LoginResult ForPasswordChange(PasswordChangeRequiredResponse response) => new(response, null, null);
    public static LoginResult ForTwoFactor(TwoFactorRequiredResponse response) => new(null, response, null);
    public static LoginResult ForTokens(AuthTokenResult tokens) => new(null, null, tokens);
}

/// <summary>Business-layer refactor: the two possible outcomes of a Google login call.</summary>
public record GoogleLoginResult(
    ProfileCompletionRequiredResponse? ProfileCompletionRequired,
    AuthTokenResult? Tokens)
{
    public static GoogleLoginResult ForProfileCompletion(ProfileCompletionRequiredResponse response) => new(response, null);
    public static GoogleLoginResult ForTokens(AuthTokenResult tokens) => new(null, tokens);
}

/// <summary>
/// Business-layer refactor: RefreshToken's outcome. On failure, the controller always
/// deletes the refresh cookie regardless of which specific reason failed (an invalid/expired
/// raw token, or a since-revoked tenant membership) -- both branches did that identically in
/// the pre-refactor controller, so a single Success flag is enough to drive that from here.
/// </summary>
public record RefreshTokenOutcome(bool Success, string? FailureMessage, AuthTokenResult? Tokens)
{
    public static RefreshTokenOutcome Failed(string message) => new(false, message, null);
    public static RefreshTokenOutcome Succeeded(AuthTokenResult tokens) => new(true, null, tokens);
}
