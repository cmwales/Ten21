namespace Ten21.Api.Contracts.Auth;

public record LoginRequest(string Email, string Password);

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
