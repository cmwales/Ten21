using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Application.Abstractions;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Identity.Services;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;

namespace Ten21.Infrastructure;

/// <summary>
/// Single composition point for everything this layer needs registered. Api.Program.cs
/// calls AddInfrastructure(configuration) once instead of knowing about EF Core, Npgsql,
/// Identity store, or interceptor wiring directly -- that wiring detail belongs to this
/// layer, not Api. JWT Bearer authentication SCHEME configuration (as opposed to token
/// issuance/persistence, which is here) stays in Api.Program.cs -- see the comment there --
/// since that's a specific-host request-pipeline concern.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IHardDeleteOverride, HardDeleteOverride>();
        services.AddScoped<TenantSessionInterceptor>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<Ten21DbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("Ten21Database"));
            options.AddInterceptors(
                serviceProvider.GetRequiredService<TenantSessionInterceptor>(),
                serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Brute-force lockout per SECURITY.docx §5 (5 failed attempts = 15-minute
                // lock). Configured here since it's inherent to Identity setup -- the
                // sliding-window IP rate-limiting middleware alongside it is still US-05.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
                // Password policy left at ASP.NET Core Identity's defaults deliberately --
                // no reason to weaken or bikeshed them without a specific requirement.
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<Ten21DbContext>()
            .AddDefaultTokenProviders();

        // US-16: 24-hour expiry for email confirmation and password reset tokens, both of
        // which use the "Default" DataProtectorTokenProvider unless a story-specific
        // provider is registered for them. Explicit, not relied on as an undocumented
        // library default -- same reasoning as every other security-relevant constant in
        // this codebase (AccessTokenLifetime, RefreshTokenLifetime, ...).
        services.Configure<DataProtectionTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(24);
        });

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IGoogleIdTokenVerifier, GoogleIdTokenVerifier>(); // US-15

        return services;
    }
}
