using LFPortal.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LFPortal.Web.Tests;

/// <summary>
/// Unit tests for <see cref="RepositorySessionMiddleware"/>.
///
/// Test 1 from the regression suite: When the Laserfiche Web Client (or Desktop Client)
/// opens the portal with <c>?repository=NewEmployeeTest&amp;source=webclient</c>, the
/// middleware must write <c>"NewEmployeeTest"</c> to the session under
/// <c>"ActiveRepositoryId"</c> — this value then flows through to the login page and
/// every subsequent request in the same session.
///
/// Also tests guard conditions: no query param → session unchanged, invalid repo IDs
/// are rejected, Desktop Client (no source param) is labelled correctly.
/// </summary>
public sealed class RepositorySessionMiddlewareTests
{
    // ── Test 1: Web Client launch stores repository in session ─────────────────

    [Fact]
    public async Task Invoke_WebClientLaunchUrl_StoresRepositoryIdInSession()
    {
        // Arrange: request with ?repository=NewEmployeeTest&source=webclient
        var (ctx, session) = MakeContext("/", "repository=NewEmployeeTest&source=webclient");
        var middleware = MakeMiddleware();

        // Act
        await middleware.InvokeAsync(ctx);

        // Assert: session has the correct repository ID
        // "ActiveRepositoryId" is the session key written by RepositorySessionMiddleware.
        Assert.Equal("NewEmployeeTest", session.GetString("ActiveRepositoryId"));
    }

    [Fact]
    public async Task Invoke_WebClientLaunchUrl_StoresWebClientSourceLabel()
    {
        var (ctx, session) = MakeContext("/", "repository=NewEmployeeTest&source=webclient");
        await MakeMiddleware().InvokeAsync(ctx);

        Assert.Equal("Laserfiche Web Client",
            session.GetString("ActiveRepositorySource"));
    }

    [Fact]
    public async Task Invoke_WebClientLaunchUrl_OverridesPreviouslyStoredRepository()
    {
        // The user previously had "LFNewRepoWF" in their session (from a prior login).
        // Opening with a new ?repository= must override it immediately.
        var (ctx, session) = MakeContext("/", "repository=NewEmployeeTest&source=webclient");
        session.SetString("ActiveRepositoryId", "LFNewRepoWF");

        await MakeMiddleware().InvokeAsync(ctx);

        // ?repository=NewEmployeeTest must override the previously stored LFNewRepoWF.
        Assert.Equal("NewEmployeeTest", session.GetString("ActiveRepositoryId"));
    }

    [Fact]
    public async Task Invoke_DesktopClientLaunchUrl_StoresRepositoryAndDesktopSourceLabel()
    {
        // Desktop Client does NOT send source=webclient.
        var (ctx, session) = MakeContext("/", "repository=LFNewRepoWF");
        await MakeMiddleware().InvokeAsync(ctx);

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

        await MakeMiddleware().InvokeAsync(ctx);

        // Without ?repository= the session must not be modified.
        Assert.Equal("ExistingRepo", session.GetString("ActiveRepositoryId"));
    }

    [Fact]
    public async Task Invoke_EmptyRepositoryParam_DoesNotModifySession()
    {
        var (ctx, session) = MakeContext("/", "repository=");
        session.SetString("ActiveRepositoryId", "ExistingRepo");

        await MakeMiddleware().InvokeAsync(ctx);

        Assert.Equal("ExistingRepo",
            session.GetString("ActiveRepositoryId"));
    }

    // ── Guard: control characters and excessive length ─────────────────────────

    [Fact]
    public async Task Invoke_RepositoryParamWithControlChars_IsRejected()
    {
        var (ctx, session) = MakeContext("/", "repository=Repo\tId");  // tab character

        await MakeMiddleware().InvokeAsync(ctx);

        // Session must not be set when the value contains control characters.
        Assert.Null(session.GetString("ActiveRepositoryId"));
    }

    [Fact]
    public async Task Invoke_RepositoryParamTooLong_IsRejected()
    {
        var longId = new string('A', 201);
        var (ctx, session) = MakeContext("/", $"repository={longId}");

        await MakeMiddleware().InvokeAsync(ctx);

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

        await middleware.InvokeAsync(ctx);

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
}
