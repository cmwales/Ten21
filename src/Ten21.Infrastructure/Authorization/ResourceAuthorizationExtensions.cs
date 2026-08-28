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
/// Audit Refinement Sprint: the one-line call site controllers use to add the
/// SameTenantResourceAuthorizationHandler backstop on top of their existing (already-correct)
/// PropertyId-scoped "find by id" queries. Folds the null-check and the resource-based
/// authorization check into a single call so adopting it is a drop-in replacement for the
/// `?? throw new NotFoundException(...)` pattern every nested-resource controller already
/// uses. Throws NotFoundException either way -- deliberately not ForbiddenException, so a
/// cross-tenant probe looks identical to a genuinely missing resource from the outside (see
/// NotFoundException's own doc comment).
/// </summary>
public static class ResourceAuthorizationExtensions
{
    public static async Task<TEntity> EnsureSameTenantAsync<TEntity>(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        TEntity? resource,
        string notFoundMessage,
        CancellationToken cancellationToken = default)
        where TEntity : class, ITenantScopedEntity
    {
        if (resource is null)
        {
            throw new NotFoundException(notFoundMessage);
        }

        var result = await authorizationService.AuthorizeAsync(user, resource, ResourceAuthorizationPolicies.SameTenant);
        if (!result.Succeeded)
        {
            throw new NotFoundException(notFoundMessage);
        }

        return resource;
    }
}
