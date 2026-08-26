using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Pdf;

public static class PdfServiceCollectionExtensions
{
    /// <summary>US-40: registers QuestPdfService and sets QuestPDF's global license mode to
    /// Community (free for Ten21 at its current revenue stage -- see this method's own call
    /// site comment in Program.cs for the standing note to revisit that threshold as the
    /// company grows). Setting Settings.License here, at startup, once, rather than per
    /// document generation.</summary>
    public static IServiceCollection AddPdfGeneration(this IServiceCollection services)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        services.AddSingleton<IPdfService, QuestPdfService>();
        return services;
    }
}
