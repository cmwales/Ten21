using System.Security.Claims;
using Ten21.Domain.Common;
using Ten21.Infrastructure.Authorization;
using Xunit;

namespace Ten21.UnitTests;

public class PermissionClaimsTransformationTests
{
    [Fact]
    public async Task ExpandsRoleClaim_IntoFullPermissionBundle()
    {
        var transformation = new PermissionClaimsTransformation();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, RoleNames.Accountant)],
            authenticationType: "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var result = await transformation.TransformAsync(principal);

        foreach (var permission in RolePermissions.Bundles[RoleNames.Accountant])
        {
            Assert.Contains(result.Claims, c =>
                c.Type == PermissionClaimAuthorizationHandler.PermissionClaimType && c.Value == permission);
        }
    }

    [Fact]
    public async Task DoesNothing_ForUnauthenticatedPrincipal()
    {
        var transformation = new PermissionClaimsTransformation();
        var anonymousIdentity = new ClaimsIdentity(); // no authenticationType => IsAuthenticated == false
        var principal = new ClaimsPrincipal(anonymousIdentity);

        var result = await transformation.TransformAsync(principal);

        Assert.DoesNotContain(
            result.Claims, c => c.Type == PermissionClaimAuthorizationHandler.PermissionClaimType);
    }

    [Fact]
    public async Task DoesNotDuplicateClaims_OnSecondInvocation()
    {
        var transformation = new PermissionClaimsTransformation();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, RoleNames.Vendor)],
            authenticationType: "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var first = await transformation.TransformAsync(principal);
        var second = await transformation.TransformAsync(first);

        var permissionClaimCount = second.Claims.Count(
            c => c.Type == PermissionClaimAuthorizationHandler.PermissionClaimType);
        Assert.Equal(RolePermissions.Bundles[RoleNames.Vendor].Count, permissionClaimCount);
    }

    [Fact]
    public async Task GrantsNoPermissions_ForUnrecognizedRole()
    {
        var transformation = new PermissionClaimsTransformation();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "SomeRoleThatDoesNotExist")],
            authenticationType: "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var result = await transformation.TransformAsync(principal);

        Assert.DoesNotContain(
            result.Claims, c => c.Type == PermissionClaimAuthorizationHandler.PermissionClaimType);
    }
}
