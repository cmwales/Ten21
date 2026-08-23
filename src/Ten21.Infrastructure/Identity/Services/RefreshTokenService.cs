using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Infrastructure.Identity.Services;

/// <summary>
/// Implements the 7-day HTTP-only refresh token lifecycle (SECURITY.docx §2). Token lookups
/// here deliberately query RefreshTokens directly by TokenHash -- RefreshToken is not
/// ITenantScopedEntity (see its class-level comment), so these calls work correctly even
/// when ITenantContext is unresolved, which it always is during login/refresh.
/// </summary>
public class RefreshTokenService : IRefreshTokenService
{
    // 7 days per SECURITY.docx §2. A constant, not config -- same reasoning as the access
    // token lifetime in JwtTokenService: a security policy decision, not an environment knob.
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly Ten21DbContext _dbContext;

    public RefreshTokenService(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> IssueAsync(
        Guid userId, Guid tenantId, string? createdByIp, CancellationToken cancellationToken = default)
    {
        var rawToken = RefreshTokenHasher.GenerateRawToken();

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            TokenHash = RefreshTokenHasher.Hash(rawToken),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByIp = createdByIp,
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime),
        });

        // RefreshToken doesn't implement ITenantScopedEntity, so Ten21DbContext's
        // ApplyTenantStamping loop skips it entirely -- this save succeeds regardless of
        // whether ITenantContext is resolved, which during login it never is yet.
        await _dbContext.SaveChangesAsync(cancellationToken);

        return rawToken;
    }

    public async Task<RefreshTokenRotationResult> ValidateAndRotateAsync(
        string rawToken, string? ip, CancellationToken cancellationToken = default)
    {
        var hash = RefreshTokenHasher.Hash(rawToken);
        var existing = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

        if (existing is null)
        {
            throw new RefreshTokenException("Refresh token not recognized.");
        }

        if (existing.RevokedAt is not null)
        {
            // Token reuse is a strong signal of theft (a legitimate client never presents
            // an already-rotated-out token). Revoke the entire chain, not just this one --
            // if an attacker has this token, they may also have ones issued after it.
            await RevokeChainAsync(existing, ip, cancellationToken);
            throw new RefreshTokenException(
                "Refresh token has already been used. All tokens in this chain have been revoked.");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new RefreshTokenException("Refresh token has expired.");
        }

        var newRawToken = RefreshTokenHasher.GenerateRawToken();
        var replacement = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existing.UserId,
            TenantId = existing.TenantId,
            TokenHash = RefreshTokenHasher.Hash(newRawToken),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByIp = ip,
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime),
        };

        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.RevokedByIp = ip;
        existing.ReplacedByTokenId = replacement.Id;

        _dbContext.RefreshTokens.Add(replacement);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RefreshTokenRotationResult(newRawToken, existing.UserId, existing.TenantId);
    }

    public async Task RevokeAsync(string rawToken, string? ip, CancellationToken cancellationToken = default)
    {
        var hash = RefreshTokenHasher.Hash(rawToken);
        var existing = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

        // Missing or already-revoked is a no-op, not an error: the caller's desired end
        // state ("this token no longer works") is already true either way.
        if (existing is null || existing.RevokedAt is not null)
        {
            return;
        }

        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.RevokedByIp = ip;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> RevokeAndReissueForTenantAsync(
        Guid userId, Guid newTenantId, string? oldRawToken, string? ip, CancellationToken cancellationToken = default)
    {
        RefreshToken? existing = null;
        if (!string.IsNullOrEmpty(oldRawToken))
        {
            var hash = RefreshTokenHasher.Hash(oldRawToken);
            existing = await _dbContext.RefreshTokens
                .SingleOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);
        }

        var newRawToken = RefreshTokenHasher.GenerateRawToken();
        var replacement = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = newTenantId,
            TokenHash = RefreshTokenHasher.Hash(newRawToken),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByIp = ip,
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime),
        };

        // Missing/already-revoked is a no-op here, same reasoning as RevokeAsync -- the
        // caller's desired end state (old token dead, new one scoped to the target tenant)
        // holds either way. When it IS active, chain-track it via ReplacedByTokenId so token
        // reuse detection (see ValidateAndRotateAsync) still sees one continuous chain across
        // the context switch, not an orphaned dead end.
        if (existing is not null && existing.RevokedAt is null)
        {
            existing.RevokedAt = DateTimeOffset.UtcNow;
            existing.RevokedByIp = ip;
            existing.ReplacedByTokenId = replacement.Id;
        }

        _dbContext.RefreshTokens.Add(replacement);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return newRawToken;
    }

    private async Task RevokeChainAsync(RefreshToken token, string? ip, CancellationToken cancellationToken)
    {
        var current = token;
        while (true)
        {
            if (current.RevokedAt is null)
            {
                current.RevokedAt = DateTimeOffset.UtcNow;
                current.RevokedByIp = ip;
            }

            if (current.ReplacedByTokenId is null)
                break;

            var next = await _dbContext.RefreshTokens
                .SingleOrDefaultAsync(rt => rt.Id == current.ReplacedByTokenId, cancellationToken);
            if (next is null)
                break;

            current = next;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
