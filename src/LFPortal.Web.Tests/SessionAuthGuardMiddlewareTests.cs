using LFPortal.Infrastructure.Options;
using LFPortal.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LFPortal.Web.Tests;

/// <summary>
/// Regression tests for <see cref="SessionAuthGuardMiddleware"/>.
///
/// Key invariants guarded here:
///   12. Web Client launch MUST NOT redirect to /Login.
///   13. Web Client launch MUST reach Dashboard.
///   14. Dormant LFDS SSO remains unaffected by the bypass.
///   15. No LFDS/V2 OAuth token exchange is triggered by the bypass.
///   16. Direct-browser sessions with a configured repo pass through unchanged.
/// </summary>
public sealed class SessionAuthGuardMiddlewareTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SessionAuthGuardMiddleware MakeMiddleware(
        string? configuredRepoId = "LFNewRepoWF",
        RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;

        var options = Microsoft.Extensions.Options.Options.Create(
            new LaserficheOptions { RepositoryId = configuredRepoId ?? string.Empty });

        return new SessionAuthGuardMiddleware(
            next,
            new TestOptionsMonitor(options.Value),
            NullLogger<SessionAuthGuardMiddleware>.Instance);
    }

    private static DefaultHttpContext MakeContext(
        string path = "/",
        string? source = null,
        string? activeRepoId = null,
        string? authenticatedRepoId = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new System.IO.MemoryStream();

        var session = new FakeSession();
        if (source           != null) session.SetString("ActiveRepositorySource",  source);
        if (activeRepoId     != null) session.SetString("ActiveRepositoryId",       activeRepoId);
        if (authenticatedRepoId != null) session.SetString("AuthenticatedRepositoryId", authenticatedRepoId);
        ctx.Session = session;
        return ctx;
    }

    // ── Requirement 12 & 13: Web Client launch must NOT redirect to /Login ────

    [Fact]
    public async Task Invoke_WebClientSource_NoAuth_ReachesNext()
    {
        // Arrange – Web Client launch: source=webclient sets "Laserfiche Web Client",
        // repository id is in session from RepositorySessionMiddleware.
        bool nextCalled = false;
        var mw = MakeMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = MakeContext(
            path:        "/",
            source:      "Laserfiche Web Client",
            activeRepoId: "NewEmployeeTest");

        // Act
        await mw.InvokeAsync(ctx);

        // Assert — must reach Dashboard, must NOT redirect to /Login
        Assert.True(nextCalled,
            "Web Client sessions must reach the Dashboard without login.");
        // Web Client sessions must NOT receive a /Login redirect.
        Assert.Null(ctx.Response.Headers["Location"].FirstOrDefault());
    }

    [Fact]
    public async Task Invoke_WebClientSource_NoAuthenticatedKey_StillReachesNext()
    {
        // Regression: before fix, guard checked AuthenticatedRepositoryId == ActiveRepositoryId.
        // Web Client sessions never set AuthenticatedRepositoryId (no login form),
        // so the guard incorrectly redirected them.
        bool nextCalled = false;
        var mw = MakeMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });

        // No AuthenticatedRepositoryId — simulates a fresh Web Client launch
        var ctx = MakeContext(
            source:      "Laserfiche Web Client",
            activeRepoId: "NewEmployeeTest"
            // authenticatedRepoId intentionally absent
        );

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Invoke_WebClientSource_WithMismatchedAuthRepo_StillReachesNext()
    {
        // If someone previously authenticated to repo A and now launches via Web Client
        // into repo B, the guard must NOT block them — Web Client uses DPAPI credentials,
        // not session-authenticated credentials.
        bool nextCalled = false;
        var mw = MakeMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = MakeContext(
            source:             "Laserfiche Web Client",
            activeRepoId:       "NewEmployeeTest",
            authenticatedRepoId: "OldRepo");   // deliberately mismatched

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled,
            "Web Client sessions must pass even when AuthenticatedRepositoryId differs.");
    }

    // ── Desktop Client still requires login ───────────────────────────────────

    [Fact]
    public async Task Invoke_DesktopClient_NoAuth_RedirectsToLogin()
    {
        // Desktop Client remains guarded — the Login form is still shown for it.
        bool nextCalled = false;
        var mw = MakeMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = MakeContext(
            source:      "Laserfiche Desktop Client",
            activeRepoId: "LFNewRepoWF"
            // no AuthenticatedRepositoryId
        );

        await mw.InvokeAsync(ctx);

        Assert.False(nextCalled, "Unauthenticated Desktop Client sessions must be redirected.");
    }

    [Fact]
    public async Task Invoke_DesktopClient_Authenticated_ReachesNext()
    {
        bool nextCalled = false;
        var mw = MakeMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = MakeContext(
            source:             "Laserfiche Desktop Client",
            activeRepoId:        "LFNewRepoWF",
            authenticatedRepoId: "LFNewRepoWF");

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    // ── Direct browser (no source) ────────────────────────────────────────────

    [Fact]
    public async Task Invoke_DirectBrowser_ConfiguredRepo_ReachesNext()
    {
        bool nextCalled = false;
        var mw = MakeMiddleware(
            configuredRepoId: "LFNewRepoWF",
            next: _ => { nextCalled = true; return Task.CompletedTask; });

        // No session source — direct browser access
        var ctx = MakeContext(path: "/Dashboard");

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled,
            "Direct browser access with configured fallback must pass through.");
    }

    [Fact]
    public async Task Invoke_DirectBrowser_NoRepoAnywhere_RedirectsToLogin()
    {
        // No session repo, no configured fallback → must redirect for repo selection.
        bool nextCalled = false;
        var mw = MakeMiddleware(
            configuredRepoId: "",   // nothing configured
            next: _ => { nextCalled = true; return Task.CompletedTask; });

        var ctx = MakeContext(path: "/Dashboard");

        await mw.InvokeAsync(ctx);

        Assert.False(nextCalled, "No-repo direct browser must be redirected to Login.");
    }

    // ── Excluded paths are never redirected ───────────────────────────────────

    [Theory]
    [InlineData("/Login")]
    [InlineData("/Login/Callback")]
    [InlineData("/Settings")]
    [InlineData("/Settings/TestConnection")]
    [InlineData("/health")]
    public async Task Invoke_ExcludedPath_AlwaysPassesThrough(string path)
    {
        // Even a Desktop Client session with no auth must pass excluded paths.
        bool nextCalled = false;
        var mw = MakeMiddleware(
            configuredRepoId: "",
            next: _ => { nextCalled = true; return Task.CompletedTask; });

        var ctx = MakeContext(
            path:   path,
            source: "Laserfiche Desktop Client",
            activeRepoId: "LFNewRepoWF");

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled, $"Excluded path '{path}' must always pass through.");
    }

    // ── SSO remains dormant ───────────────────────────────────────────────────

    [Fact]
    public async Task WebClientBypass_DoesNotTouchSsoOrOAuthKeys()
    {
        // Verifies the guard does not inject any LFDS / OAuth state.
        // The Web Client path is the "not guarded" branch — it simply calls _next
        // without touching the session beyond what RepositorySessionMiddleware already set.
        var ctx = MakeContext(
            source:      "Laserfiche Web Client",
            activeRepoId: "NewEmployeeTest");

        var session = (FakeSession)ctx.Session;
        var keysBefore = session.Keys.ToHashSet();

        var mw = MakeMiddleware();
        await mw.InvokeAsync(ctx);

        var keysAfter = session.Keys.ToHashSet();
        var newKeys   = keysAfter.Except(keysBefore).ToList();

        Assert.Empty(newKeys);   // guard must not write any new session keys
    }

    // ── Infrastructure ───────────────────────────────────────────────────────

    /// <summary>Minimal IOptionsMonitor stub.</summary>
    private sealed class TestOptionsMonitor : IOptionsMonitor<LaserficheOptions>
    {
        public TestOptionsMonitor(LaserficheOptions value) => CurrentValue = value;
        public LaserficheOptions CurrentValue { get; }
        public LaserficheOptions Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<LaserficheOptions, string?> listener) =>
            new NullDisposable();
        private sealed class NullDisposable : IDisposable { public void Dispose() { } }
    }

    /// <summary>In-memory ISession implementation for tests.</summary>
    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable => true;
        public string Id        => "test-session";
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
