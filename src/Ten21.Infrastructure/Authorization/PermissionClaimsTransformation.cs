using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Ten21.Domain.Common;

namespace Ten21.Infrastructure.Authorization;

/// <summary>
/// Expands the JWT's single role claim into its full additive permission-claim bundle
/// (RolePermissions.Bundles) at request-authorization time, rather than baking every
/// permission into the token at issuance.
///
/// Why not just widen JwtTokenService to issue permission claims directly instead? Two
/// reasons:
///   1. Role -> permission mapping stays entirely server-side and centrally controlled --
///      changing what a role can do takes effect on the next request, not after every
///      currently-issued 15-minute token happens to expire.
///   2. Tokens stay small regardless of how large the permission catalog grows across
///      Phase 2+ features -- a JWT travels on every single request; permission claims are
///      only ever needed during authorization, server-side.
/// </summary>
public class PermissionClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            // Anonymous request (login, refresh-token, revoke-token, /health) -- nothing
            // to expand, and no role claim to look up yet.
            return Task.FromResult(principal);
        }

        // ASP.NET Core may invoke a IClaimsTransformation more than once per request in some
        // pipelines -- guard against adding duplicate claims on a re-invocation.
        if (principal.HasClaim(c => c.Type == PermissionClaimAuthorizationHandler.PermissionClaimType))
        {
            return Task.FromResult(principal);
        }

        var roleName = principal.FindFirst(ClaimTypes.Role)?.Value;
        if (roleName is null || !RolePermissions.Bundles.TryGetValue(roleName, out var permissions))
        {
            // Authenticated but an unrecognized role (shouldn't happen in practice, since
            // JwtTokenService only ever issues roles from RoleNames.All) -- fail closed by
            // granting no permission claims at all, rather than guessing.
            return Task.FromResult(principal);
        }

        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(PermissionClaimAuthorizationHandler.PermissionClaimType, permission));
        }

        return Task.FromResult(principal);
    }
}
