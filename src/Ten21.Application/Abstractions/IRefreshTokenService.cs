namespace Ten21.Application.Abstractions;

/// <summary>
/// Issues, rotates, and revokes refresh tokens. Interfaced so AuthController can be tested
/// against a fake without a real database, and because the storage mechanism (currently
/// EF Core/Postgres) is a legitimate candidate to swap for something like Redis later if
/// refresh-token volume ever makes that worthwhile.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Issues a brand new refresh token for the given user/tenant context. Returns the raw
    /// token value (to go in the HTTP-only cookie) -- this is the only time the raw value
    /// ever exists outside the client; only its hash is persisted.
    /// </summary>
    Task<string> IssueAsync(Guid userId, Guid tenantId, string? createdByIp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a presented raw refresh token and, if valid, rotates it: revokes the old
    /// one and issues a new one in the same call, returning both the new raw token and the
    /// (userId, tenantId) context to re-mint an access token for.
    /// Throws RefreshTokenException if the token is missing, expired, or already revoked.
    /// </summary>
    Task<RefreshTokenRotationResult> ValidateAndRotateAsync(string rawToken, string? ip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a token without issuing a replacement -- used for logout.
    /// A no-op (not an error) if the token doesn't exist or is already revoked, since the
    /// end state the caller wants ("this token no longer works") is already true.
    /// </summary>
    Task RevokeAsync(string rawToken, string? ip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Used by context-switching (US-04): revokes whatever refresh token the caller
    /// currently holds (chain-tracked via ReplacedByTokenId, same as ValidateAndRotateAsync)
    /// and issues a brand new one scoped to newTenantId instead of the old token's tenant.
    /// Without this, a switched-context session reverts to the caller's original tenant the
    /// moment its access token expires and the frontend silently refreshes.
    /// oldRawToken missing/unrecognized/already-revoked is a no-op for the revoke half (same
    /// reasoning as RevokeAsync) -- a new token is issued regardless.
    /// Returns the new raw token value, to go in the refresh cookie.
    /// </summary>
    Task<string> RevokeAndReissueForTenantAsync(
        Guid userId, Guid newTenantId, string? oldRawToken, string? ip, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-16: revokes every currently-active refresh token for a user, across every tenant
    /// they hold a session in -- called after a successful password reset, on the same
    /// reasoning as forcing a re-login after any other credential change: a token issued
    /// under the old password shouldn't silently keep working after it's been reset (e.g.
    /// because the account was compromised and the reset IS the recovery action).
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, string? ip, CancellationToken cancellationToken = default);
}

public record RefreshTokenRotationResult(string NewRawToken, Guid UserId, Guid TenantId);

public class RefreshTokenException(string message) : Exception(message);
