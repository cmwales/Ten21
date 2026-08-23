using Microsoft.Extensions.DependencyInjection;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Security;

public static class BotDefenseServiceCollectionExtensions
{
    public static IServiceCollection AddBotDefense(this IServiceCollection services)
    {
        services.AddHttpClient<ITurnstileVerificationService, TurnstileVerificationService>();
        return services;
    }
}
