namespace Ten21.Application.Abstractions;

/// <summary>
/// Mints short-lived (15-minute, per SECURITY.docx) JWT access tokens.
///
/// Interfaced deliberately: the signing scheme (HMAC today, possibly RSA/asymmetric later
/// for multi-service verification) and the token library itself are real candidates for
/// change, and AuthController needs a mockable seam for testing without a real signing key.
///
/// Deliberately takes a single role name string, not a full claims/permission bundle.
/// US-03 (Additive Claims Authorization Engine) deliberately does NOT widen this signature
/// to accept a permission bundle -- role-to-permission expansion happens server-side, per
/// request, via PermissionClaimsTransformation (Infrastructure), not at token-issuance
/// time. This keeps tokens small regardless of how large the permission catalog grows, and
/// means a role's permissions can change without needing every already-issued token to expire
/// first. See PermissionClaimsTransformation's class comment for the full reasoning.
/// </summary>
public interface IJwtTokenService
{
    AccessToken GenerateAccessToken(Guid userId, Guid tenantId, Guid? organizationId, string roleName);
}

public record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
