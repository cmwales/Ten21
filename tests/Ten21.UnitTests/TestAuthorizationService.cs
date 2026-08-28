using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Application.Abstractions;
using Ten21.Infrastructure.Authorization;

namespace Ten21.UnitTests;

/// <summary>
/// Audit Refinement Sprint: builds a real (not mocked) IAuthorizationService wired with the
/// actual SameTenantResourceAuthorizationHandler and policy -- the same way
/// AuthorizationConfiguration.AddTen21Authorization wires it in the real app, just via
/// AddAuthorizationCore (no MVC/HTTP pipeline needed) so controller unit tests can exercise
/// ResourceAuthorizationExtensions.EnsureSameTenantAsync exactly as production code does,
/// matching this test project's existing convention of real concrete implementations over
/// mocking frameworks.
/// </summary>
internal static class TestAuthorizationService
{
    public static IAuthorizationService Create(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tenantContext);
        services.AddScoped<IAuthorizationHandler, SameTenantResourceAuthorizationHandler>();
        services.AddAuthorizationCore(options =>
            options.AddPolicy(ResourceAuthorizationPolicies.SameTenant, policy =>
                policy.Requirements.Add(new SameTenantRequirement())));

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }
}
