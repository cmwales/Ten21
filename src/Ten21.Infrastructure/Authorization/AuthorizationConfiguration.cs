using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Domain.Common;

namespace Ten21.Infrastructure.Authorization;

/// <summary>
/// Registers one authorization policy per permission constant (Permissions.All), discovered
/// via reflection rather than hand-maintained -- a new permission added in Domain
/// automatically gets a usable [Authorize(Policy = ...)] policy with zero extra wiring
/// here, the same reflection-over-registration pattern as Ten21DbContext's tenant query
/// filters in US-01.
///
/// The secure-by-default fallback policy (US-01/US-02) lives here too, not in Program.cs --
/// policy SHAPE (what policies exist, what each requires) is a security-model concern that
/// belongs with the rest of the claims engine, not host-specific pipeline wiring. JWT
/// Bearer SCHEME configuration (issuer/audience/signing key) stays in Program.cs, since
/// that genuinely is specific to how this particular host validates incoming tokens.
/// </summary>
public static class AuthorizationConfiguration
{
    public static IServiceCollection AddTen21Authorization(this IServiceCollection services)
    {
        services.AddScoped<IClaimsTransformation, PermissionClaimsTransformation>();
        services.AddSingleton<IAuthorizationHandler, PermissionClaimAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, TenantHardBlockAuthorizationHandler>();
        // Scoped, not Singleton -- depends on ITenantContext, which is itself Scoped
        // (resolved per-request from the JWT/TenantMiddleware).
        services.AddScoped<IAuthorizationHandler, SameTenantResourceAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy =>
                    policy.Requirements.Add(new PermissionRequirement(permission)));
            }

            // Resource-based BOLA/IDOR backstop (Audit Refinement Sprint) -- see
            // ResourceAuthorizationExtensions.EnsureSameTenantAsync for the call-site shape.
            options.AddPolicy(ResourceAuthorizationPolicies.SameTenant, policy =>
                policy.Requirements.Add(new SameTenantRequirement()));
        });

        return services;
    }
}
