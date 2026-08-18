using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Common;
using LFPortal.Web.Authentication;
using LFPortal.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LFPortal.Web.Tests;

public sealed class LaunchControllerTests
{
    [Fact]
    public async Task Launch_WebClient_ClearsDashboardStateAndRedirectsToPasswordGateway()
    {
        var auth = new SpyAuthService();
        var credentials = new SpyCredentialStore();
        var correlation = new SpyOAuthCookie();
        var (controller, session, context) = MakeController(auth, credentials, correlation);
        session.SetString("ActiveRepositoryId", "OldRepo");
        session.SetString("ActiveRepositorySource", "Laserfiche Web Client");
        session.SetString("AuthenticatedRepositoryId", "OldRepo");
        session.SetString("AuthenticatedLaserficheUser", "amjd");
        session.SetString("OAuth_PendingState", "old-state");
        session.SetString("AuthenticationScopeMethod", "LFDS");
        session.SetString("AuthenticationScopeSubject", "amjd");

        var result = await controller.Index("TestEmployee", "webclient");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Login", redirect.ControllerName);
        Assert.Equal("TestEmployee", redirect.RouteValues?["repository"]);
        Assert.Equal("/Dashboard", redirect.RouteValues?["returnUrl"]);
        Assert.DoesNotContain("forceLogin", redirect.RouteValues?.Keys ?? []);

        Assert.True(auth.Invalidated);
        Assert.True(credentials.Cleared);
        Assert.True(correlation.Deleted);
        Assert.Contains("Dashboard.Cookie", context.Response.Headers.SetCookie.ToString());
        Assert.All(new[]
        {
            "ActiveRepositoryId", "ActiveRepositorySource", "AuthenticatedRepositoryId",
            "AuthenticatedLaserficheUser", "OAuth_PendingState", "AuthenticationScopeMethod",
            "AuthenticationScopeSubject",
        }, key => Assert.Null(session.GetString(key)));
    }

    [Fact]
    public async Task Launch_PreservesSafeLocalReturnUrl()
    {
        var (controller, _, _) = MakeController(
            new SpyAuthService(), new SpyCredentialStore(), new SpyOAuthCookie());

        var result = await controller.Index(
            "NewLfRepo", "webclient", "/Dashboard?repository=NewLfRepo");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("NewLfRepo", redirect.RouteValues?["repository"]);
        Assert.Equal("/Dashboard?repository=NewLfRepo", redirect.RouteValues?["returnUrl"]);
    }

    [Fact]
    public async Task Launch_RejectsNonWebClientSource()
    {
        var (controller, _, _) = MakeController(
            new SpyAuthService(), new SpyCredentialStore(), new SpyOAuthCookie());

        var result = await controller.Index("TestEmployee", "unknown");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static (LaunchController Controller, TestSession Session, HttpContext Context)
        MakeController(
            SpyAuthService auth,
            SpyCredentialStore credentials,
            SpyOAuthCookie correlation)
    {
        var context = new DefaultHttpContext();
        var session = new TestSession();
        context.Session = session;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddControllersWithViews();
        services.AddAuthentication(DashboardAuthenticationDefaults.Scheme)
            .AddCookie(DashboardAuthenticationDefaults.Scheme);
        context.RequestServices = services.BuildServiceProvider();

        var controller = new LaunchController(
            auth,
            credentials,
            correlation,
            NullLogger<LaunchController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        controller.Url = new StubUrlHelper(context);
        return (controller, session, context);
    }

    private sealed class StubUrlHelper(HttpContext context) : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new(
            context,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        public string? Action(UrlActionContext actionContext)
            => $"/{actionContext.Controller}/{actionContext.Action}";

        public bool IsLocalUrl(string? url) =>
            !string.IsNullOrEmpty(url) && url[0] == '/' &&
            (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
        public string? Content(string? contentPath) => contentPath;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = [];
        public bool IsAvailable => true;
        public string Id => "launch-test-session";
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SpyAuthService : ILaserficheAuthService
    {
        public bool Invalidated { get; private set; }
        public Task InvalidateCurrentSessionTokensAsync()
        {
            Invalidated = true;
            return Task.CompletedTask;
        }
        public Task<string> GetTokenAsync(RepositoryDescriptor repository, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InvalidateTokenAsync(RepositoryDescriptor repository) => throw new NotSupportedException();
        public Task<bool> TryAuthenticateAsync(RepositoryDescriptor repository, string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExchangeAuthorizationCodeAsync(RepositoryDescriptor repository, string code, string codeVerifier, string redirectUri, string clientId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SpyCredentialStore : ISessionCredentialStore
    {
        public bool Cleared { get; private set; }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Cleared = true;
            return Task.CompletedTask;
        }
        public Task StoreAsync(string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LaserficheCredential?> TryGetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SpyOAuthCookie : IOAuthTransactionCookie
    {
        public bool Deleted { get; private set; }
        public void Delete(HttpContext context)
        {
            Deleted = true;
            context.Response.Cookies.Delete(".Dashboard.OAuth.Correlation");
        }
        public OAuthTransactionCookieWriteResult Write(HttpContext context, OAuthTransaction transaction) => throw new NotSupportedException();
        public OAuthTransactionCookieResult Read(HttpContext context) => throw new NotSupportedException();
    }
}
