namespace Ten21.Domain.Entities;

/// <summary>
/// A rotating 7-day refresh token, delivered to the client as an HTTP-only cookie (never
/// in a JSON body -- see SECURITY.docx §2).
///
/// Deliberately NOT ITenantScopedEntity, even though it carries a TenantId. The refresh
/// endpoint is called precisely when no valid access token exists yet, which means
/// TenantMiddleware has nothing to extract tenant_id from and ITenantContext is
/// unresolved for that request. If this implemented ITenantScopedEntity, the fail-closed
/// global query filter (US-01) would return zero rows for every single refresh attempt --
/// the exact bootstrap problem TenantMembership also has during login, but with no
/// IgnoreQueryFilters() escape hatch available here because refresh tokens are looked up
/// by opaque hash, not by any tenant-scoped listing operation that filter is meant to
/// protect. TenantId is stored as a plain column instead, purely to know which tenant
/// context to re-mint the next access token for.
///
/// Refreshing (POST /api/auth/refresh-token) always mints a new token for the SAME tenant
/// as the one being rotated out -- refresh itself never changes tenant context. The one
/// exception is POST /api/organization/switch-context (US-04), which requires an
/// already-valid access token and explicitly reissues the refresh token scoped to the new
/// tenant (RefreshTokenService.RevokeAndReissueForTenantAsync) precisely so a SUBSEQUENT
/// refresh keeps the caller on the tenant they switched to, rather than reverting to
/// whichever tenant the original login happened to issue this chain under.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw token value. The raw value is returned to the client exactly
    /// once (in the cookie) and is never persisted -- if the database were ever exposed,
    /// stolen hashes are useless for authentication, same principle as password hashing.
    /// </summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }

    /// <summary>
    /// Set when this token is rotated out in favor of a new one, forming an auditable
    /// chain. If a revoked token is ever presented again (token reuse -- a strong signal of
    /// theft), the whole chain should be revoked, not just the one token.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
