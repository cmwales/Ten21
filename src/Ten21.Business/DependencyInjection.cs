using Microsoft.Extensions.DependencyInjection;
using Ten21.Business.Charges;

namespace Ten21.Business;

/// <summary>
/// Business-layer refactor: registers this project's concrete service/repository classes.
/// Both are registered Scoped, matching Ten21DbContext's own lifetime -- one instance per
/// HTTP request, same as every other Scoped dependency in this app.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddBusiness(this IServiceCollection services)
    {
        services.AddScoped<ChargeRepository>();
        services.AddScoped<ChargeService>();
        return services;
    }
}
