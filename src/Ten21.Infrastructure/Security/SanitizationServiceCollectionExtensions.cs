using Microsoft.Extensions.DependencyInjection;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Security;

public static class SanitizationServiceCollectionExtensions
{
    public static IServiceCollection AddInputSanitization(this IServiceCollection services)
    {
        services.AddSingleton<IInputSanitizer, HtmlInputSanitizer>();
        return services;
    }
}
