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

    /// <summary>
    /// US-15/US-17: mints a short-lived, deliberately tenant-less and role-less token for a
    /// caller who is genuinely authenticated but not yet fully provisioned (a first-time
    /// Google signup with no workspace yet, or a password check that passed but is still
    /// waiting on a 2FA code). Carries only `user_id` and a `purpose` claim -- no
    /// `tenant_id`, so every existing tenant-scoped endpoint already fail-closes against it
    /// for free (Ten21DbContext's fail-closed query filter, US-01), and no role claim, so
    /// PermissionClaimsTransformation expands it to zero permissions. The one endpoint that
    /// should accept it (e.g. POST /api/auth/complete-profile) checks the `purpose` claim
    /// explicitly rather than relying on structural inertness alone.
    /// </summary>
    AccessToken GenerateInterimAccessToken(Guid userId, string purpose);

    /// <summary>
    /// US-17: same interim-token shape as GenerateInterimAccessToken (tenant-less,
    /// role-less, purpose = TokenPurposes.TwoFactorPending), plus one extra claim
    /// (TokenPurposes.TwoFactorProviderClaimType) recording WHICH Identity token provider
    /// ("Email" or "Authenticator") the challenge was issued against -- verify-2fa checks
    /// the submitted code against that specific provider, not either one, so a code
    /// generated for one channel can't be replayed against the other.
    /// </summary>
    AccessToken GenerateTwoFactorChallengeToken(Guid userId, string twoFactorProvider);
}

public record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

/// <summary>Values for the `purpose` claim an interim token (GenerateInterimAccessToken)
/// carries -- what the token is narrowly scoped to be used for next.</summary>
public static class TokenPurposes
{
    public const string ClaimType = "purpose";

    /// <summary>US-15: a Google signup with no Tenant/TenantMembership yet. Only valid
    /// against POST /api/auth/complete-profile.</summary>
    public const string ProfileIncomplete = "profile_incomplete";

    /// <summary>US-17: password verified, awaiting a 2FA code. Only valid against
    /// POST /api/auth/login/verify-2fa.</summary>
    public const string TwoFactorPending = "2fa_pending";

    /// <summary>US-17: the claim type recording which Identity token provider
    /// (TokenOptions.DefaultEmailProvider/DefaultAuthenticatorProvider) a TwoFactorPending
    /// token's challenge was issued against.</summary>
    public const string TwoFactorProviderClaimType = "tfa_provider";
}
