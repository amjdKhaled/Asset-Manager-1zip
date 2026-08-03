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
        services.AddSingleton<ILaserficheApiAdapter, LaserficheApiAdapter>();

        // ── Repository context — singleton; reads session-scoped override first,
        //    then falls back to live options (supports Desktop Client repo param) ─
        services.AddSingleton<IRepositoryContext, SessionAwareRepositoryContext>();

        // ── Credential provider — singleton; chain(primary, env-var fallback) ─
        RegisterCredentialProvider(services);

        // ── Portal configuration service — singleton ───────────────────────────
        services.AddSingleton<IPortalConfigurationService, PortalConfigurationService>();

        // ── Auth service — singleton; safe because IMemoryCache & IHttpClientFactory
        //    are both singleton-safe ─────────────────────────────────────────────
        services.AddSingleton<ILaserficheAuthService, LaserficheAuthService>();

        // ── HTTP clients ───────────────────────────────────────────────────────
        RegisterHttpClients(services);

        // ── Search audit log — singleton; accumulates across the process lifetime ─
        services.AddSingleton<ISearchAuditLog, InMemorySearchAuditLog>();

        // ── API version auto-detection — probes v2 → v1 when ApiVersion = Auto and
        //    persists the result to the runtime settings file ────────────────────
        services.AddHostedService<ApiVersionDetectionService>();

        // ── Domain services — scoped (HttpClient usage is per-request) ─────────
        services.AddScoped<ILaserficheRepositoryService,       LaserficheRepositoryService>();
        services.AddScoped<ILaserficheEntryService,            LaserficheEntryService>();
        services.AddScoped<ILaserficheFieldDefinitionService,  LaserficheFieldDefinitionService>();
        services.AddScoped<ILaserficheSearchService,           LaserficheSearchService>();
        services.AddScoped<ILaserficheDocumentService,         LaserficheDocumentService>();
        services.AddScoped<ILaserficheTemplateService,         LaserficheTemplateService>();
        services.AddScoped<ILaserficheDashboardService,        LaserficheDashboardService>();

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
    /// Registers a three-layer credential stack:
    /// <list type="number">
    ///   <item><b>Session credentials</b> — established by the Desktop Client Login page
    ///     (<see cref="SessionCredentialStore"/>).</item>
    ///   <item><b>Disk store</b> — DPAPI (Windows) or Data Protection (non-Windows).</item>
    ///   <item><b>Environment variables</b> — read-only fallback for dev machines.</item>
    /// </list>
    /// The outer <see cref="SessionAwareCredentialProvider"/> is registered as
    /// <see cref="ICredentialProvider"/> so the token service always checks the session first.
    /// Writes (Settings page) always go to the disk store and are not session-scoped.
    /// </summary>
    private static void RegisterCredentialProvider(IServiceCollection services)
    {
        // ── Disk chain: writable secure store + read-only env-var fallback ────
        services.AddSingleton<CredentialChainProvider>(sp =>
        {
            ICredentialProvider primary = OperatingSystem.IsWindows()
                ? ActivatorUtilities.CreateInstance<DpapiCredentialProvider>(sp)
                : ActivatorUtilities.CreateInstance<DataProtectionCredentialProvider>(sp);

            var fallback = ActivatorUtilities.CreateInstance<EnvironmentVariableCredentialProvider>(sp);
            var logger   = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CredentialChainProvider>>();

            return new CredentialChainProvider(primary, fallback, logger);
        });

        // ── Session credential store — singleton; safe because it uses IHttpContextAccessor ──
        services.AddSingleton<ISessionCredentialStore, SessionCredentialStore>();

        // ── Composite provider: session-first, disk-chain fallback ────────────
        services.AddSingleton<ICredentialProvider>(sp => new SessionAwareCredentialProvider(
            sp.GetRequiredService<ISessionCredentialStore>(),
            sp.GetRequiredService<CredentialChainProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SessionAwareCredentialProvider>>()));
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
        services.AddTransient<LaserficheRequestLoggingHandler>();

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
        .AddHttpMessageHandler<LaserficheRequestLoggingHandler>()
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
        .AddHttpMessageHandler<LaserficheRequestLoggingHandler>()
        .AddStandardResilienceHandler();

        // Unauthenticated, short-timeout client used ONLY by API-version
        // auto-detection probes. No resilience pipeline: a failed probe should
        // fail fast (the next candidate version is tried immediately).
        services.AddHttpClient("LaserficheProbe", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
    }
}
