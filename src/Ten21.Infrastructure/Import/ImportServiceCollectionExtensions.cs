using Microsoft.Extensions.DependencyInjection;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Import;

public static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddPropertyImport(this IServiceCollection services)
    {
        services.AddScoped<IPropertyImportFileParser, PropertyImportFileParser>();
        return services;
    }
}
