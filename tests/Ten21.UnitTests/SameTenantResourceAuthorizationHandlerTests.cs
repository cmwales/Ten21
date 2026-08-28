using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Ten21.Domain.Common;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Persistence;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>Audit Refinement Sprint: the resource-based BOLA/IDOR backstop -- succeeds only
/// when the loaded resource's own TenantId matches the caller's active tenant.</summary>
public class SameTenantResourceAuthorizationHandlerTests
{
    private sealed class FakeTenantScopedEntity : ITenantScopedEntity
    {
        public Guid TenantId { get; set; }
    }

    private static readonly ClaimsPrincipal AnyPrincipal = new(new ClaimsIdentity(authenticationType: "TestAuth"));

    [Fact]
    public async Task Succeeds_WhenResourceTenantMatchesActiveTenant()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);
        var handler = new SameTenantResourceAuthorizationHandler(tenantContext);
        var resource = new FakeTenantScopedEntity { TenantId = tenantId };
        var context = new AuthorizationHandlerContext([new SameTenantRequirement()], AnyPrincipal, resource);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task DoesNotSucceed_WhenResourceBelongsToADifferentTenant()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.NewGuid());
        var handler = new SameTenantResourceAuthorizationHandler(tenantContext);
        var resource = new FakeTenantScopedEntity { TenantId = Guid.NewGuid() };
        var context = new AuthorizationHandlerContext([new SameTenantRequirement()], AnyPrincipal, resource);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task DoesNotSucceed_WhenNoActiveTenantIsResolved()
    {
        var tenantContext = new TenantContext();
        var handler = new SameTenantResourceAuthorizationHandler(tenantContext);
        var resource = new FakeTenantScopedEntity { TenantId = Guid.NewGuid() };
        var context = new AuthorizationHandlerContext([new SameTenantRequirement()], AnyPrincipal, resource);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
