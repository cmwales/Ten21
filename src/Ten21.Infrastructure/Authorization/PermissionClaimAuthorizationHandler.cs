using Microsoft.AspNetCore.Authorization;

namespace Ten21.Infrastructure.Authorization;

/// <summary>
/// Grants a PermissionRequirement when the ClaimsPrincipal carries a matching "permission"
/// claim. Those claims are never issued directly in the JWT -- they're expanded at request
/// time from the token's single role claim by PermissionClaimsTransformation (see that
/// class for why: centralizes role->permission changes server-side and keeps tokens small
/// regardless of how large the permission catalog grows).
/// </summary>
public class PermissionClaimAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    public const string PermissionClaimType = "permission";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(PermissionClaimType, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
