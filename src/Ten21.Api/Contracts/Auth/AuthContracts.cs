namespace Ten21.Api.Contracts.Auth;

public record LoginRequest(string Email, string Password);

/// <summary>US-14: workspace registration. WorkspaceName/PortfolioSize describe the new
/// Tenant being provisioned, not the registrant themselves.</summary>
public record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? PhoneNumber,
    string? Address,
    string WorkspaceName,
    int PortfolioSize,
    bool AgreedToTerms);

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
