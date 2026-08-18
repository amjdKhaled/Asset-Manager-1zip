using LFPortal.Web.Middleware;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Web.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LFPortal.Web.Tests;

/// <summary>
/// Unit tests for <see cref="RepositorySessionMiddleware"/>.
///
/// A legacy Web Client link is routed through <c>/Launch</c>; that endpoint owns the
/// Dashboard-state cleanup and loading view. Desktop links continue to populate the
/// repository session directly.
///
/// Also tests guard conditions: no query param → session unchanged, invalid repo IDs
/// are rejected, Desktop Client (no source param) is labelled correctly.
/// </summary>
public sealed class RepositorySessionMiddlewareTests
{
    // ── Web Client launch routes through the loading boundary ──────────────────

    [Fact]
    public async Task Invoke_LegacyWebClientLaunch_RedirectsToLoadingPageWithoutChangingState()
    {
        // Arrange: request with ?repository=NewEmployeeTest&source=webclient
        var (ctx, session) = MakeContext("/", "repository=NewEmployeeTest&source=webclient");
        session.SetString("AuthenticatedLaserficheUser", "amjd");
        session.SetString("AuthenticatedRepositoryId", "OldRepo");
        ctx.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "amjd")],
                DashboardAuthenticationDefaults.Scheme));
        var middleware = MakeMiddleware();

        // Act
        var auth = new SpyAuthService();
        await middleware.InvokeAsync(ctx, auth, new SpyOAuthCookie());

        // The /Launch endpoint owns cleanup so the loading page can be rendered first.
        Assert.Null(session.GetString("ActiveRepositoryId"));
        Assert.Equal("amjd", session.GetString("AuthenticatedLaserficheUser"));
        Assert.Empty(ctx.Response.Headers.SetCookie.ToString());
        Assert.StartsWith("/Launch?", ctx.Response.Headers.Location.ToString());
        Assert.Contains("repository=NewEmployeeTest", ctx.Response.Headers.Location.ToString());
        Assert.Contains("source=webclient", ctx.Response.Headers.Location.ToString());
        Assert.DoesNotContain("forceLogin", ctx.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Invoke_LegacyWebClientLaunch_DoesNotWriteSessionBeforeLaunchPage()
    {
        var (ctx, session) = MakeContext("/", "repository=NewEmployeeTest&source=webclient");
        await MakeMiddleware().InvokeAsync(ctx, new SpyAuthService(), new SpyOAuthCookie());

        Assert.Null(session.GetString("ActiveRepositorySource"));
    }

    [Fact]
    public async Task Invoke_LoadingPage_IsNotIntercepted()
    {
        var (ctx, _) = MakeContext(
            "/Launch", "repository=NewEmployeeTest&source=webclient");
        var nextCalled = false;
        var middleware = new RepositorySessionMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<RepositorySessionMiddleware>.Instance);

        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.False(ctx.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Invoke_LegacyWebClientLaunch_LeavesCleanupToLaunchEndpoint()
    {
        // The user previously had "LFNewRepoWF" in their session (from a prior login).
        // Opening with a new ?repository= must override it immediately.
        var (ctx, session) = MakeContext("/", "repository=NewEmployeeTest&source=webclient");
        session.SetString("ActiveRepositoryId", "LFNewRepoWF");

        await MakeMiddleware().InvokeAsync(ctx, new SpyAuthService(), new SpyOAuthCookie());

        // Middleware redirects only; /Launch invalidates this old Dashboard state.
        Assert.Equal("LFNewRepoWF", session.GetString("ActiveRepositoryId"));
        Assert.Contains("repository=NewEmployeeTest", ctx.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Invoke_DesktopClientLaunchUrl_StoresRepositoryAndDesktopSourceLabel()
    {
        // Desktop Client does NOT send source=webclient.
        var (ctx, session) = MakeContext("/", "repository=LFNewRepoWF");
        await MakeMiddleware().InvokeAsync(ctx, new SpyAuthService(), new SpyOAuthCookie());

        Assert.Equal("LFNewRepoWF",
            session.GetString("ActiveRepositoryId"));
        Assert.Equal("Laserfiche Desktop Client",
            session.GetString("ActiveRepositorySource"));
    }

    // ── No query param → session unchanged ────────────────────────────────────

    [Fact]
    public async Task Invoke_NoRepositoryParam_DoesNotModifySession()
    {
        var (ctx, session) = MakeContext("/Login");
        // Pre-existing session value (from a previous request).
        session.SetString("ActiveRepositoryId", "ExistingRepo");

        await MakeMiddleware().InvokeAsync(ctx, new SpyAuthService(), new SpyOAuthCookie());

        // Without ?repository= the session must not be modified.
        Assert.Equal("ExistingRepo", session.GetString("ActiveRepositoryId"));
    }

    [Fact]
    public async Task Invoke_EmptyRepositoryParam_DoesNotModifySession()
    {
        var (ctx, session) = MakeContext("/", "repository=");
        session.SetString("ActiveRepositoryId", "ExistingRepo");

        await MakeMiddleware().InvokeAsync(ctx, new SpyAuthService(), new SpyOAuthCookie());

        Assert.Equal("ExistingRepo",
            session.GetString("ActiveRepositoryId"));
    }

    // ── Guard: control characters and excessive length ─────────────────────────

    [Fact]
    public async Task Invoke_RepositoryParamWithControlChars_IsRejected()
    {
        var (ctx, session) = MakeContext("/", "repository=Repo\tId");  // tab character

        await MakeMiddleware().InvokeAsync(ctx, new SpyAuthService(), new SpyOAuthCookie());

        // Session must not be set when the value contains control characters.
        Assert.Null(session.GetString("ActiveRepositoryId"));
    }

    [Fact]
    public async Task Invoke_RepositoryParamTooLong_IsRejected()
    {
        var longId = new string('A', 201);
        var (ctx, session) = MakeContext("/", $"repository={longId}");

        await MakeMiddleware().InvokeAsync(ctx, new SpyAuthService(), new SpyOAuthCookie());

        Assert.Null(session.GetString("ActiveRepositoryId"));
    }

    // ── Next delegate is always called ────────────────────────────────────────

    [Fact]
    public async Task Invoke_AlwaysCallsNextDelegate()
    {
        var (ctx, _) = MakeContext("/", "repository=TestRepo");
        var nextCalled = false;

        var middleware = new RepositorySessionMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<RepositorySessionMiddleware>.Instance);

        await middleware.InvokeAsync(ctx, new SpyAuthService(), new SpyOAuthCookie());

        Assert.True(nextCalled, "The next request delegate must always be called.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RepositorySessionMiddleware MakeMiddleware() =>
        new(_ => Task.CompletedTask, NullLogger<RepositorySessionMiddleware>.Instance);

    private static (HttpContext ctx, TestSession session) MakeContext(
        string path, string? queryString = null)
    {
        var ctx     = new DefaultHttpContext();
        var session = new TestSession();
        ctx.Session = session;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddOptions();
        services.AddAuthentication(DashboardAuthenticationDefaults.Scheme)
            .AddCookie(DashboardAuthenticationDefaults.Scheme);
        ctx.RequestServices = services.BuildServiceProvider();
        ctx.Request.Path = path;
        if (queryString is not null)
            ctx.Request.QueryString = new QueryString("?" + queryString);
        return (ctx, session);
    }

    // ── Test infrastructure ───────────────────────────────────────────────────

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _data = [];

        public bool   IsAvailable  => true;
        public string Id           => "test-session-id";
        public IEnumerable<string> Keys => _data.Keys;

        public void   Clear()                               => _data.Clear();
        public void   Remove(string key)                   => _data.Remove(key);
        public void   Set(string key, byte[] value)        => _data[key] = value;
        public bool   TryGetValue(string key, out byte[] value) =>
            _data.TryGetValue(key, out value!);

        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadAsync  (CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SpyAuthService : ILaserficheAuthService
    {
        public bool Invalidated { get; private set; }
        public Task InvalidateCurrentSessionTokensAsync() { Invalidated = true; return Task.CompletedTask; }
        public Task<string> GetTokenAsync(RepositoryDescriptor repository, CancellationToken cancellationToken = default) => Task.FromResult("unused");
        public Task InvalidateTokenAsync(RepositoryDescriptor repository) => Task.CompletedTask;
        public Task<bool> TryAuthenticateAsync(RepositoryDescriptor repository, string username, string password, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExchangeAuthorizationCodeAsync(RepositoryDescriptor repository, string code, string codeVerifier, string redirectUri, string clientId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class SpyOAuthCookie : IOAuthTransactionCookie
    {
        public OAuthTransactionCookieWriteResult Write(HttpContext context, OAuthTransaction transaction) => throw new NotSupportedException();
        public OAuthTransactionCookieResult Read(HttpContext context) => new(null, false, false);
        public void Delete(HttpContext context) => context.Response.Cookies.Delete(".Dashboard.OAuth.Correlation");
    }
}
