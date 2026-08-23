using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Ten21.Domain.Common;
using Ten21.Infrastructure.Authorization;
using Xunit;

namespace Ten21.UnitTests;

public class PermissionClaimAuthorizationHandlerTests
{
    private static ClaimsPrincipal PrincipalWithPermissions(params string[] permissions)
    {
        var claims = permissions.Select(p => new Claim(PermissionClaimAuthorizationHandler.PermissionClaimType, p));
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task Succeeds_WhenPrincipalHasMatchingPermissionClaim()
    {
        var handler = new PermissionClaimAuthorizationHandler();
        var requirement = new PermissionRequirement(Permissions.Ledger.Read);
        var principal = PrincipalWithPermissions(Permissions.Ledger.Read);
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task DoesNotSucceed_WhenPermissionClaimIsMissing()
    {
        var handler = new PermissionClaimAuthorizationHandler();
        var requirement = new PermissionRequirement(Permissions.Ledger.Write);
        var principal = PrincipalWithPermissions(Permissions.Ledger.Read); // different permission
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task DoesNotSucceed_ForAnonymousPrincipal()
    {
        var handler = new PermissionClaimAuthorizationHandler();
        var requirement = new PermissionRequirement(Permissions.Ledger.Read);
        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // no claims at all
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
