using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var hasSmtpCredentials =
            !string.IsNullOrWhiteSpace(configuration["Smtp:Username"]) &&
            !string.IsNullOrWhiteSpace(configuration["Smtp:Password"]);

        if (hasSmtpCredentials)
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, ConsoleEmailSender>();
        }

        return services;
    }
}
