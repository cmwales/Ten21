using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Ten21.Domain.Common;

namespace Ten21.Infrastructure.Authorization;

/// <summary>
/// SECURITY.docx §4.2's "Owner vs. Tenant Isolation Principle" enforced as a defense-in-depth
/// invariant, independent of RolePermissions.Bundles. Even if a future change accidentally
/// grants a Tenant one of the TenantRestrictedPermissionPrefixes permissions, this handler
/// still calls Fail() for it.
///
/// context.Fail() wins over any other handler's Succeed() for the same requirement --
/// ASP.NET Core's default authorization evaluation treats one Fail() as an overall policy
/// failure regardless of what else succeeded. That's the entire point of this handler
/// existing separately from PermissionClaimAuthorizationHandler rather than folding this
/// check into it: a hard, structural override that doesn't depend on the claims bundle
/// being correct, the same belt-and-suspenders principle as EF Core filter + Postgres RLS.
/// </summary>
public class TenantHardBlockAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var isTenant = context.User.HasClaim(ClaimTypes.Role, RoleNames.Tenant);
        var isRestricted = TenantRestrictedPermissionPrefixes.Values
            .Any(prefix => requirement.Permission.StartsWith(prefix, StringComparison.Ordinal));

        if (isTenant && isRestricted)
        {
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
