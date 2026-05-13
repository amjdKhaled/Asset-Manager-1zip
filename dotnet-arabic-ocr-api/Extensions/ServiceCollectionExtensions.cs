using ArabicPdfExtraction.Api.Contracts;
using ArabicPdfExtraction.Api.Services;

namespace ArabicPdfExtraction.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPdfExtractionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ITempFileStore, TempFileStore>();
        services.AddScoped<IPdfTextExtractorService, PdfTextExtractorService>();
        services.AddScoped<IPdfImageRendererService, PdfImageRendererService>();
        services.AddScoped<IArabicOcrService, ArabicOcrService>();
        services.AddScoped<ITextCleanupService, TextCleanupService>();
        services.AddScoped<PdfExtractionOrchestrator>();
        return services;
    }
}
