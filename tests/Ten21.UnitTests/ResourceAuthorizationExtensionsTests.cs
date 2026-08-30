using System.Security.Claims;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Persistence;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>Audit Refinement Sprint: the guard clause call sites use immediately after
/// resolving an entity via `?? throw new NotFoundException(...)`, to independently re-verify
/// tenant ownership before the entity is used further.</summary>
public class ResourceAuthorizationExtensionsTests
{
    private sealed class FakeTenantScopedEntity : ITenantScopedEntity
    {
        public Guid TenantId { get; set; }
    }

    private static readonly ClaimsPrincipal AnyPrincipal = new(new ClaimsIdentity(authenticationType: "TestAuth"));

    [Fact]
    public async Task ThrowsNotFoundException_WhenResourceBelongsToADifferentTenant()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.NewGuid());
        var authorizationService = TestAuthorizationService.Create(tenantContext);
        var resource = new FakeTenantScopedEntity { TenantId = Guid.NewGuid() };

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            authorizationService.EnsureSameTenantAsync(AnyPrincipal, resource, "not found"));
        Assert.Equal("not found", exception.Message);
    }

    [Fact]
    public async Task CompletesWithoutThrowing_WhenResourceBelongsToTheActiveTenant()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);
        var authorizationService = TestAuthorizationService.Create(tenantContext);
        var resource = new FakeTenantScopedEntity { TenantId = tenantId };

        await authorizationService.EnsureSameTenantAsync(AnyPrincipal, resource, "not found");
    }
}
