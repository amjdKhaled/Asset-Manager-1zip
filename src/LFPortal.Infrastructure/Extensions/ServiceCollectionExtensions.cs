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
    /// <param name="services">The DI service collection.</param>
    /// <param name="configuration">
    /// Application configuration used to bind <see cref="LaserficheOptions"/>
    /// from the <c>Laserfiche</c> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for method chaining.</returns>
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

        // ── API adapter — singleton; URL patterns don't change at runtime ─────
        services.AddSingleton<ILaserficheApiAdapter, LaserficheV2ApiAdapter>();

        // ── Repository context — singleton; driven by immutable config ─────────
        services.AddSingleton<IRepositoryContext, ConfigurationRepositoryContext>();

        // ── Credential provider — singleton; stateless reads from OS/env ───────
        RegisterCredentialProvider(services);

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
    /// Registers the credential provider appropriate for the current platform and
    /// configured provider type. Both provider implementations are stateless,
    /// making singleton lifetime safe.
    /// </summary>
    private static void RegisterCredentialProvider(IServiceCollection services)
    {
        services.AddSingleton<ICredentialProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LaserficheOptions>>().Value;

            return options.CredentialProvider switch
            {
                CredentialProviderType.DPAPI when OperatingSystem.IsWindows() =>
                    ActivatorUtilities.CreateInstance<DpapiCredentialProvider>(sp),

                _ =>
                    ActivatorUtilities.CreateInstance<EnvironmentVariableCredentialProvider>(sp)
            };
        });
    }

    /// <summary>
    /// Registers two named <see cref="System.Net.Http.HttpClient"/> instances.
    ///
    /// <para><b>LaserficheRaw</b> — no authentication. Used for token requests and explicit
    /// credential verification on the Settings page.</para>
    ///
    /// <para><b>LaserficheAuthenticated</b> — Bearer token attached automatically via
    /// <see cref="BearerTokenHandler"/>, which also handles transparent token refresh on
    /// HTTP 401 responses. Used by all other service calls.</para>
    ///
    /// Both clients apply gzip decompression and a configurable request timeout from
    /// <see cref="LaserficheOptions.TimeoutSeconds"/>. Standard resilience policies
    /// (retry with exponential back-off, circuit breaker) are applied via
    /// <c>AddStandardResilienceHandler()</c>.
    /// </summary>
    private static void RegisterHttpClients(IServiceCollection services)
    {
        // BearerTokenHandler — transient so HttpClientFactory can manage its lifetime
        services.AddTransient<BearerTokenHandler>();

        // Raw client — no token handler
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

        // Authenticated client — BearerTokenHandler provides transparent auth
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
