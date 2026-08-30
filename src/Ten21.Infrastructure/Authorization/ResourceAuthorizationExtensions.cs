using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;

namespace Ten21.Infrastructure.Authorization;

public static class ResourceAuthorizationPolicies
{
    public const string SameTenant = "SameTenantResource";
}

/// <summary>
/// Audit Refinement Sprint: the call controllers make to add the
/// SameTenantResourceAuthorizationHandler backstop on top of their existing (already-correct)
/// PropertyId-scoped "find by id" queries. A guard clause, not a lookup -- callers resolve the
/// entity themselves first (typically via `?? throw new NotFoundException(...)` against a
/// Service's FindAsync), then pass the already-loaded, non-null entity here to independently
/// re-verify its TenantId before using it further. Throws NotFoundException either way --
/// deliberately not ForbiddenException, so a cross-tenant probe looks identical to a genuinely
/// missing resource from the outside (see NotFoundException's own doc comment).
/// </summary>
public static class ResourceAuthorizationExtensions
{
    public static async Task EnsureSameTenantAsync<TEntity>(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        TEntity resource,
        string notFoundMessage,
        CancellationToken cancellationToken = default)
        where TEntity : class, ITenantScopedEntity
    {
        var result = await authorizationService.AuthorizeAsync(user, resource, ResourceAuthorizationPolicies.SameTenant);
        if (!result.Succeeded)
        {
            throw new NotFoundException(notFoundMessage);
        }
    }
}
