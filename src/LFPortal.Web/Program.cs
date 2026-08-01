using LFPortal.Domain.Version;
using LFPortal.Infrastructure.Extensions;
using LFPortal.Web.Middleware;
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

    // ── Writable settings override (Settings page writes here; reloads without restart) ──
    var writableConfigPath = Path.Combine(
        builder.Environment.ContentRootPath, "config", "laserfiche.json");

    builder.Configuration.AddJsonFile(
        writableConfigPath,
        optional: true,
        reloadOnChange: true);

    // ── Serilog — replace the default ASP.NET Core logging pipeline ──────────
    builder.Host.UseSerilog((context, services, loggerConfig) =>
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", LFPortalVersion.Display));

    // ── IIS integration ───────────────────────────────────────────────────────
    builder.Services.Configure<IISServerOptions>(opts =>
    {
        opts.AutomaticAuthentication = false;
    });

    // ── ASP.NET Core Data Protection (cross-platform credential encryption) ───
    builder.Services.AddDataProtection();

    // ── MVC ───────────────────────────────────────────────────────────────────
    builder.Services.AddControllersWithViews();

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
    });

    // ── Build ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

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

    // ── Routing ───────────────────────────────────────────────────────────────
    app.UseRouting();

    // ── Session — must be after UseRouting, before controllers ───────────────
    app.UseSession();

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
