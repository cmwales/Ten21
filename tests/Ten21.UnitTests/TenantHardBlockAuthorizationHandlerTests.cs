using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Ten21.Domain.Common;
using Ten21.Infrastructure.Authorization;
using Xunit;

namespace Ten21.UnitTests;

public class TenantHardBlockAuthorizationHandlerTests
{
    private static ClaimsPrincipal PrincipalWithRoleAndPermission(string role, string permission)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, role),
            new Claim(PermissionClaimAuthorizationHandler.PermissionClaimType, permission),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
    }

    [Fact]
    public async Task Fails_WhenTenantHasLedgerPermission_EvenThoughTheClaimIsPresent()
    {
        // Simulates the exact scenario this handler exists for: RolePermissions.Bundles
        // never grants Tenant this permission, but if a future bug DID add it (or the
        // permission claim were forged/mismapped somehow), this must still block it.
        var handler = new TenantHardBlockAuthorizationHandler();
        var requirement = new PermissionRequirement(Permissions.Ledger.Read);
        var principal = PrincipalWithRoleAndPermission(RoleNames.Tenant, Permissions.Ledger.Read);
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Fails_WhenTenantHasVotingPermission()
    {
        var handler = new TenantHardBlockAuthorizationHandler();
        var requirement = new PermissionRequirement(Permissions.Voting.Cast);
        var principal = PrincipalWithRoleAndPermission(RoleNames.Tenant, Permissions.Voting.Cast);
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task DoesNotFail_ForTenantWithNonRestrictedPermission()
    {
        var handler = new TenantHardBlockAuthorizationHandler();
        var requirement = new PermissionRequirement(Permissions.WorkOrders.Write);
        var principal = PrincipalWithRoleAndPermission(RoleNames.Tenant, Permissions.WorkOrders.Write);
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task DoesNotFail_ForNonTenantRoleWithRestrictedPermission()
    {
        var handler = new TenantHardBlockAuthorizationHandler();
        var requirement = new PermissionRequirement(Permissions.Ledger.Read);
        var principal = PrincipalWithRoleAndPermission(RoleNames.BoardMember, Permissions.Ledger.Read);
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasFailed);
    }
}
