using Microsoft.AspNetCore.Authorization;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;

namespace Ten21.Infrastructure.Authorization;

/// <summary>
/// Audit Refinement Sprint: a genuine resource-based handler -- takes the loaded entity
/// itself, not just a route parameter, and succeeds only if the resource's own TenantId
/// matches the caller's active tenant. This is a redundant backstop, not the primary defense:
/// the EF Core global query filter already scopes every load to the active tenant, so under
/// normal operation this always succeeds trivially. It earns its keep the same way Postgres
/// RLS does -- catching the case where a query filter gets bypassed (a stray
/// IgnoreQueryFilters(), a raw-SQL escape hatch, a future EF release-note regression) before
/// that bypassed data reaches the response, applied via ResourceAuthorizationExtensions at
/// each nested-resource controller's "find by id" call site.
/// </summary>
public class SameTenantResourceAuthorizationHandler
    : AuthorizationHandler<SameTenantRequirement, ITenantScopedEntity>
{
    private readonly ITenantContext _tenantContext;

    public SameTenantResourceAuthorizationHandler(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SameTenantRequirement requirement, ITenantScopedEntity resource)
    {
        if (_tenantContext.TenantId is { } tenantId && resource.TenantId == tenantId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
