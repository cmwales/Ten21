using System.Security.Claims;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Persistence;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>Audit Refinement Sprint: the one-line call site controllers use in place of
/// `?? throw new NotFoundException(...)`.</summary>
public class ResourceAuthorizationExtensionsTests
{
    private sealed class FakeTenantScopedEntity : ITenantScopedEntity
    {
        public Guid TenantId { get; set; }
    }

    private static readonly ClaimsPrincipal AnyPrincipal = new(new ClaimsIdentity(authenticationType: "TestAuth"));

    [Fact]
    public async Task ThrowsNotFoundException_WhenResourceIsNull()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.NewGuid());
        var authorizationService = TestAuthorizationService.Create(tenantContext);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            authorizationService.EnsureSameTenantAsync<FakeTenantScopedEntity>(AnyPrincipal, null, "not found"));
    }

    [Fact]
    public async Task ThrowsNotFoundException_WhenResourceBelongsToADifferentTenant()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.NewGuid());
        var authorizationService = TestAuthorizationService.Create(tenantContext);
        var resource = new FakeTenantScopedEntity { TenantId = Guid.NewGuid() };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            authorizationService.EnsureSameTenantAsync(AnyPrincipal, resource, "not found"));
    }

    [Fact]
    public async Task ReturnsTheResource_WhenItBelongsToTheActiveTenant()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);
        var authorizationService = TestAuthorizationService.Create(tenantContext);
        var resource = new FakeTenantScopedEntity { TenantId = tenantId };

        var result = await authorizationService.EnsureSameTenantAsync(AnyPrincipal, resource, "not found");

        Assert.Same(resource, result);
    }
}
