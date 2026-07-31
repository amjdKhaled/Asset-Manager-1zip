using System.Net;
using System.Net.Http.Headers;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Adapters;
using LFPortal.Infrastructure.Credentials;
using LFPortal.Infrastructure.HealthChecks;
using LFPortal.Infrastructure.Http;
using LFPortal.Infrastructure.Options;
using LFPortal.Infrastructure.Repository;
using LFPortal.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LFPortal.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering the complete Laserfiche Infrastructure layer
/// into the ASP.NET Core dependency injection container.
/// </summary>
/// <remarks>
/// Call <see cref="AddLaserficheInfrastructure"/> from <c>Program.cs</c> as the single
/// entry point for all Infrastructure registrations. No Infrastructure types are
/// referenced directly in <c>Program.cs</c> beyond this call.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Laserfiche Infrastructure services, HTTP clients, health checks,
    /// credential providers, and repository context into <paramref name="services"/>.
    /// </summary>
    public static IServiceCollection AddLaserficheInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Options ───────────────────────────────────────────────────────────
        services.AddOptions<LaserficheOptions>()
            .Bind(configuration.GetSection(LaserficheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Memory cache (token cache) ────────────────────────────────────────
        services.AddMemoryCache();

        // ── API adapter — singleton; reads live options via IOptionsMonitor ───
        services.AddSingleton<ILaserficheApiAdapter, LaserficheV2ApiAdapter>();

        // ── Repository context — singleton; reads live options per call ────────
        services.AddSingleton<IRepositoryContext, ConfigurationRepositoryContext>();

        // ── Credential provider — singleton; chain(primary, env-var fallback) ─
        RegisterCredentialProvider(services);

        // ── Portal configuration service — singleton ───────────────────────────
        services.AddSingleton<IPortalConfigurationService, PortalConfigurationService>();

        // ── Auth service — singleton; safe because IMemoryCache & IHttpClientFactory
        //    are both singleton-safe ─────────────────────────────────────────────
        services.AddSingleton<ILaserficheAuthService, LaserficheAuthService>();

        // ── HTTP clients ───────────────────────────────────────────────────────
        RegisterHttpClients(services);

        // ── Domain services — scoped (HttpClient usage is per-request) ─────────
        services.AddScoped<ILaserficheRepositoryService, LaserficheRepositoryService>();
        services.AddScoped<ILaserficheEntryService,      LaserficheEntryService>();
        services.AddScoped<ILaserficheSearchService,     LaserficheSearchService>();
        services.AddScoped<ILaserficheDocumentService,   LaserficheDocumentService>();
        services.AddScoped<ILaserficheDashboardService,  LaserficheDashboardService>();

        // ── Health checks ──────────────────────────────────────────────────────
        services.AddHealthChecks()
            .AddCheck<LaserficheHealthCheck>(
                name: "laserfiche",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: ["laserfiche", "api"]);

        return services;
    }

    // ──────────────────────────── Private helpers ─────────────────────────────

    /// <summary>
    /// Registers a <see cref="CredentialChainProvider"/> that always tries the
    /// secure writable store first (DPAPI on Windows, ASP.NET Core Data Protection
    /// on non-Windows), then falls back to environment variables for reads.
    /// Environment variables are never used as the write target — they are always
    /// a read-only fallback so that dev machines can still override without a UI.
    /// The <c>CredentialProvider</c> option has no effect on this behaviour; it is
    /// retained for future extension (e.g. Azure Key Vault).
    /// </summary>
    private static void RegisterCredentialProvider(IServiceCollection services)
    {
        services.AddSingleton<ICredentialProvider>(sp =>
        {
            // Always build the chain: writable secure store + read-only env-var fallback.
            ICredentialProvider primary = OperatingSystem.IsWindows()
                ? ActivatorUtilities.CreateInstance<DpapiCredentialProvider>(sp)
                : ActivatorUtilities.CreateInstance<DataProtectionCredentialProvider>(sp);

            var fallback = ActivatorUtilities.CreateInstance<EnvironmentVariableCredentialProvider>(sp);
            var logger   = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CredentialChainProvider>>();

            return new CredentialChainProvider(primary, fallback, logger);
        });
    }

    /// <summary>
    /// Registers two named <see cref="System.Net.Http.HttpClient"/> instances.
    ///
    /// <para><b>LaserficheRaw</b> — no authentication. Used for token requests.</para>
    /// <para><b>LaserficheAuthenticated</b> — Bearer token attached automatically via
    /// <see cref="BearerTokenHandler"/>. Used by all repository service calls.</para>
    /// </summary>
    private static void RegisterHttpClients(IServiceCollection services)
    {
        services.AddTransient<BearerTokenHandler>();

        services.AddHttpClient("LaserficheRaw", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<LaserficheOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        .AddStandardResilienceHandler();

        services.AddHttpClient("LaserficheAuthenticated", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<LaserficheOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        .AddHttpMessageHandler<BearerTokenHandler>()
        .AddStandardResilienceHandler();
    }
}
