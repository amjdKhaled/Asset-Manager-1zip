using LFPortal.Domain.Version;
using LFPortal.Web.Authentication;
using LFPortal.Infrastructure.Configuration;
using LFPortal.Infrastructure.Extensions;
using LFPortal.Infrastructure.Options;
using LFPortal.Web.Middleware;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Serilog;

// ── Bootstrap logger — captures startup errors before full logging is configured ──
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting {Display}", LFPortalVersion.Display);

    var builder = WebApplication.CreateBuilder(args);

    // ── Configuration layering (last-wins) ────────────────────────────────────
    //  1. appsettings.json                                  structural defaults (already loaded)
    //  2. <ContentRoot>\config\laserfiche.json              LEGACY writable file (pre-Phase-1
    //                                                       installs and non-Windows dev fallback)
    //  3. %ProgramData%\Dashboard\laserfiche.config.json    installer wizard values
    //  4. %ProgramData%\Dashboard\laserfiche.runtime.json   Settings-page overrides
    // All are optional with reloadOnChange so Settings-page saves apply without restart.
    builder.Configuration.AddJsonFile(
        DashboardConfigPaths.GetLegacyRuntimeConfigPath(builder.Environment.ContentRootPath),
        optional: true,
        reloadOnChange: true);

    builder.Configuration.AddJsonFile(
        DashboardConfigPaths.InstallerConfigPath,
        optional: true,
        reloadOnChange: true);

    builder.Configuration.AddJsonFile(
        DashboardConfigPaths.RuntimeConfigPath,
        optional: true,
        reloadOnChange: true);

    // ── Serilog — replace the default ASP.NET Core logging pipeline ──────────
    builder.Host.UseSerilog((context, services, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", LFPortalVersion.Display);

        // ── Machine-wide diagnostics log — %ProgramData%\Dashboard\Logs ──────
        // The site-relative logs/ folder may not be writable (or easy to find)
        // under IIS; this second sink gives administrators a stable location
        // (C:\ProgramData\Dashboard\Logs on Windows) for [LF AUTH] diagnostics.
        try
        {
            var programDataLogs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Dashboard", "Logs");
            Directory.CreateDirectory(programDataLogs);
            loggerConfig.WriteTo.File(
                Path.Combine(programDataLogs, "dashboard-.log"),
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        }
        catch (Exception ex)
        {
            // Never let a log-directory problem prevent startup.
            Log.Warning(ex, "Could not initialise the ProgramData diagnostics log directory.");
        }
    });

    // ── IIS integration ───────────────────────────────────────────────────────
    builder.Services.Configure<IISServerOptions>(opts =>
    {
        opts.AutomaticAuthentication = false;
    });

    // ── ASP.NET Core Data Protection (cross-platform credential encryption) ───
    builder.Services.AddDataProtection();

    // ── Localization — Arabic (ar) + English (en, default) ────────────────────
    // No ResourcesPath: the RESX in Resources/SharedResource.resx has a C# codebehind
    // (SharedResource.cs) in namespace LFPortal.Web, so MSBuild embeds the binary resource
    // as "LFPortal.Web.SharedResource".  ResourcesPath="" keeps the lookup consistent.
    builder.Services.AddLocalization();

    // ── MVC ───────────────────────────────────────────────────────────────────
    builder.Services.AddControllersWithViews()
                    .AddViewLocalization();

    // ── Dashboard browser authentication ─────────────────────────────────────
    // LFDS authenticates the user and issues the Repository API token; this
    // cookie persists the resulting Dashboard identity across the callback
    // redirect and subsequent page refreshes.
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = DashboardAuthenticationDefaults.Scheme;
            options.DefaultChallengeScheme    = DashboardAuthenticationDefaults.Scheme;
            options.DefaultSignInScheme       = DashboardAuthenticationDefaults.Scheme;
        })
        .AddCookie(DashboardAuthenticationDefaults.Scheme, options =>
        {
            options.Cookie.Name       = ".Dashboard.Authentication";
            options.Cookie.HttpOnly   = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite   = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.LoginPath         = "/Login";
            options.ExpireTimeSpan    = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });
    builder.Services.AddAuthorization();

    // ── Laserfiche Infrastructure layer ───────────────────────────────────────
    builder.Services.AddLaserficheInfrastructure(builder.Configuration);

    // ── HttpContext accessor (used by SessionAwareRepositoryContext) ──────────
    builder.Services.AddHttpContextAccessor();

    // ── Session — stores the active repository when opened from the Desktop Client ──
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSession(opts =>
    {
        opts.Cookie.HttpOnly  = true;
        opts.Cookie.IsEssential = true;
        opts.IdleTimeout      = TimeSpan.FromHours(8);
        opts.Cookie.Name      = ".Dashboard.Session";
        opts.Cookie.SameSite  = SameSiteMode.Lax;
        opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

    // ── Build ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── Startup diagnostics — log non-secret Laserfiche configuration ────────
    // Lets administrators immediately verify the API URL, version, and timeout
    // without reading config files.  Credentials are never logged.
    {
        var opts = app.Services.GetRequiredService<IOptions<LaserficheOptions>>().Value;
        Log.Information(
            "Laserfiche config: ServerUrl={ServerUrl} ApiBasePath={ApiBasePath} " +
            "ApiVersion={ApiVersion} (effective: {EffectiveApiVersion}) Timeout={Timeout}s CredentialProvider={Provider} " +
            "FallbackRepository={Repo}",
            opts.ServerUrl,
            opts.ApiBasePath,
            opts.ApiVersion,
            opts.EffectiveApiVersion,
            opts.TimeoutSeconds,
            opts.CredentialProvider,
            string.IsNullOrEmpty(opts.RepositoryId)
                ? "(none — login page will prompt)"
                : opts.RepositoryId);

        if (opts.Sso.IsConfigured)
        {
            Log.Information(
                "SSO config: LfdsBaseUrl={LfdsBaseUrl} ClientId={ClientId} " +
                "AuthEndpoint={AuthEndpoint} RedirectUri={RedirectUri}",
                opts.Sso.LfdsBaseUrl,
                opts.Sso.ClientId,
                opts.SsoAuthorizationEndpoint,
                string.IsNullOrEmpty(opts.Sso.RedirectUri)
                    ? "(computed from request at runtime)"
                    : opts.Sso.RedirectUri);
        }
        else
        {
            Log.Information("SSO config: LFDS SSO is not configured (Laserfiche:Sso:LfdsBaseUrl is empty).");
        }
    }

    // ── Error handling ────────────────────────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    // ── Serilog HTTP request logging ──────────────────────────────────────────
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    });

    // ── Static files ──────────────────────────────────────────────────────────
    app.UseStaticFiles();

    // ── Request localization — culture cookie sets en / ar ────────────────────
    {
        var supported = new[] { "en", "ar" };
        app.UseRequestLocalization(new RequestLocalizationOptions()
            .SetDefaultCulture("en")
            .AddSupportedCultures(supported)
            .AddSupportedUICultures(supported));
    }

    // ── Routing ───────────────────────────────────────────────────────────────
    app.UseRouting();

    // ── Session — must be after UseRouting, before controllers ───────────────
    app.UseSession();

    // Authentication must run after routing/session and before the custom guard
    // and MVC endpoints so HttpContext.User is restored on every request.
    app.UseAuthentication();
    app.UseAuthorization();

    // ── Repository session middleware — captures ?repository= from Desktop Client ──
    app.UseMiddleware<RepositorySessionMiddleware>();

    // ── Session auth guard — redirects unauthenticated Desktop Client sessions to /Login ──
    app.UseMiddleware<SessionAuthGuardMiddleware>();

    // ── Health check endpoint ─────────────────────────────────────────────────
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = WriteHealthResponseAsync
    });

    // ── MVC routes — default lands on the Dashboard ──────────────────────────
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Dashboard}/{action=Index}/{id?}");

    Log.Information("{Display} started successfully.", LFPortalVersion.Display);
    await app.RunAsync();
}
catch (Exception ex) when (ex is not OperationCanceledException && ex.GetType().Name != "HostAbortedException")
{
    Log.Fatal(ex, "{Display} terminated unexpectedly.", LFPortalVersion.Display);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;

// ── Health check JSON writer ──────────────────────────────────────────────────

static async Task WriteHealthResponseAsync(
    HttpContext ctx,
    Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    ctx.Response.ContentType = "application/json; charset=utf-8";

    var entries = report.Entries.Select(e => new
    {
        name        = e.Key,
        status      = e.Value.Status.ToString(),
        description = e.Value.Description,
        duration    = e.Value.Duration.TotalMilliseconds,
        data        = e.Value.Data
    });

    var payload = new
    {
        version       = LFPortalVersion.Full,
        status        = report.Status.ToString(),
        totalDuration = report.TotalDuration.TotalMilliseconds,
        entries
    };

    var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy         = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented                = true,
        DefaultIgnoreCondition       = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    });

    ctx.Response.StatusCode = report.Status ==
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy ? 200 : 503;

    await ctx.Response.WriteAsync(json);
}
