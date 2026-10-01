using System.Reflection;
using System.Security.Claims;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Web.Authentication;
using LFPortal.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LFPortal.Web.Tests;

public sealed class RepositoryAuthenticationFilterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingToken_ClearsCookieAndReturnsToFullPage(bool ajax)
    {
        var (http, context) = Context(ajax ? "/Archive/Detail" : "/Archive");
        if (ajax) { http.Request.Headers["X-Requested-With"] = "XMLHttpRequest"; http.Request.Headers.Referer = "https://portal.test/Archive?entryId=17"; }
        var auth = Proxy<ILaserficheAuthService>(call => Task.FromException<string>(new UnauthorizedAccessException()));
        var filter = Filter(auth);
        var called = false;
        await filter.OnActionExecutionAsync(context, () => { called = true; return Task.FromResult(new ActionExecutedContext(context, [], new object())); });
        Assert.False(called);
        Assert.Null(http.Session.GetString("AuthenticatedRepositoryId"));
        Assert.False(http.User.Identity!.IsAuthenticated);
        if (ajax)
        {
            var result = Assert.IsType<JsonResult>(context.Result);
            Assert.Equal(401, result.StatusCode);
            var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
            Assert.Contains("entryId", json);
            Assert.DoesNotContain("%2FArchive%2FDetail", json);
        }
        else Assert.StartsWith("/Login?sessionExpired=true&returnUrl=", Assert.IsType<LocalRedirectResult>(context.Result).Url);
    }

    [Fact]
    public async Task TokenInvalidatedInsideController_ReplacesCaughtErrorViewBeforeRendering()
    {
        var (_, context) = Context("/Archive");
        var calls = 0;
        var auth = Proxy<ILaserficheAuthService>(_ => ++calls == 1 ? Task.FromResult("cached") : Task.FromException<string>(new UnauthorizedAccessException()));
        var executed = new ActionExecutedContext(context, [], new object()) { Result = new ViewResult() };
        await Filter(auth).OnActionExecutionAsync(context, () => Task.FromResult(executed));
        Assert.IsType<LocalRedirectResult>(executed.Result);
    }

    [Fact]
    public async Task OpeningWebClient_DoesNotAcquireOrRequireAnApiToken()
    {
        var (_, context) = Context("/Archive/OpenInLaserfiche");
        var auth = Proxy<ILaserficheAuthService>(_ => throw new Exception("Token must not be acquired"));
        var called = false;
        await Filter(auth).OnActionExecutionAsync(context, () => { called = true; return Task.FromResult(new ActionExecutedContext(context, [], new object())); });
        Assert.True(called);
    }

    [Theory]
    [InlineData("https://lf.test/LFRepositoryAPI", "http://lf.test/Laserfiche/index.aspx", "http://lf.test")]
    [InlineData("https://lf.test", "https://evil.test/Laserfiche", "https://lf.test")]
    public void EntryLink_SearchesExactIdAndPreservesOnlyTrustedWebClientOrigin(string api, string referer, string origin)
    {
        var url = LaserficheWebClientLinks.EntrySearch(api, "Arabic repository", 619, referer);
        Assert.StartsWith(origin + "/Laserfiche/Browse.aspx?db=Arabic%20repository#search=", url);
        Assert.EndsWith(";view=search", url);
        Assert.DoesNotContain("#?", url);
        Assert.EndsWith("{LF:ID=619};view=search", Uri.UnescapeDataString(url));
        Assert.DoesNotContain("index.aspx", url);
    }

    private static RepositoryAuthenticationFilter Filter(ILaserficheAuthService auth) => new(auth,
        Proxy<IRepositoryContext>(_ => Task.FromResult(new RepositoryDescriptor("test", "https://lf.test", "TestEmployee", "TestEmployee"))),
        NullLogger<RepositoryAuthenticationFilter>.Instance);

    private static (DefaultHttpContext, ActionExecutingContext) Context(string path)
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddAuthentication().AddCookie(DashboardAuthenticationDefaults.Scheme);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider(), Session = new Session() };
        http.Request.Scheme = "https"; http.Request.Host = new HostString("portal.test"); http.Request.Path = path;
        http.Session.SetString("AuthenticatedRepositoryId", "TestEmployee");
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.AuthenticationMethod, "RepositoryPassword")], "Dashboard.Cookie"));
        return (http, new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [], new Dictionary<string, object?>(), new object()));
    }

    private static T Proxy<T>(Func<MethodInfo, object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, Calls>(); ((Calls)(object)proxy).Call = call; return proxy;
    }
    public class Calls : DispatchProxy
    {
        public Func<MethodInfo, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!);
    }
    private sealed class Session : ISession
    {
        private readonly Dictionary<string, byte[]> values = [];
        public bool IsAvailable => true; public string Id => "test"; public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear(); public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => values.TryGetValue(key, out value!);
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
