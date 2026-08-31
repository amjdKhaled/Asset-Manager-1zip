using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Common;
using LFPortal.Infrastructure.Options;
using LFPortal.Web.Authentication;
using LFPortal.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LFPortal.Web.Tests;

public sealed class LaunchControllerTests
{
    [Fact]
    public async Task Launch_WebClient_ClearsDashboardStateAndReturnsLoadingView()
    {
        var auth = new SpyAuthService();
        var credentials = new SpyCredentialStore();
        var correlation = new SpyOAuthCookie();
        var (controller, session, context) = MakeController(
            auth, credentials, correlation, LaserficheAuthenticationMode.LfdsSso);
        session.SetString("ActiveRepositoryId", "OldRepo");
        session.SetString("ActiveRepositorySource", "Laserfiche Web Client");
        session.SetString("AuthenticatedRepositoryId", "OldRepo");
        session.SetString("AuthenticatedLaserficheUser", "amjd");
        session.SetString("OAuth_PendingState", "old-state");
        session.SetString("AuthenticationScopeMethod", "LFDS");
        session.SetString("AuthenticationScopeSubject", "amjd");

        var result = await controller.Index("TestEmployee", "webclient");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("LaunchLoading", view.ViewName);
        var model = Assert.IsType<LaunchLoadingViewModel>(view.Model);
        Assert.Equal("TestEmployee", model.RepositoryId);
        Assert.Contains("/Login/StartSso?", model.RedirectUrl);
        Assert.Contains("repository=TestEmployee", model.RedirectUrl);
        Assert.Contains("returnUrl=%2FDashboard", model.RedirectUrl);
        Assert.DoesNotContain("forceLogin", model.RedirectUrl);
        Assert.DoesNotContain("prompt=login", model.RedirectUrl);

        Assert.True(auth.Invalidated);
        Assert.True(credentials.Cleared);
        Assert.True(correlation.Deleted);
        Assert.Contains("Dashboard.Cookie", context.Response.Headers.SetCookie.ToString());

        // LFDS launch cleanup removes old authenticated-user state, then deliberately
        // restores only the validated repository/source routing markers for the
        // continuation through StartSso.
        Assert.Equal("TestEmployee", session.GetString("ActiveRepositoryId"));
        Assert.Equal("Laserfiche Web Client", session.GetString("ActiveRepositorySource"));
        Assert.All(new[]
        {
            "AuthenticatedRepositoryId", "AuthenticatedLaserficheUser", "OAuth_PendingState",
            "AuthenticationScopeMethod", "AuthenticationScopeSubject",
        }, key => Assert.Null(session.GetString(key)));
    }

    [Fact]
    public async Task Launch_WebClient_RepositoryPassword_PreservesAuthAndRedirectsToDashboard()
    {
        var auth = new SpyAuthService();
        var credentials = new SpyCredentialStore();
        var correlation = new SpyOAuthCookie();
        var (controller, session, context) = MakeController(
            auth, credentials, correlation, LaserficheAuthenticationMode.RepositoryPassword);

        session.SetString("AuthenticatedRepositoryId", "TestEmployee");
        session.SetString("AuthenticatedLaserficheUser", "admin");

        var result = await controller.Index("TestEmployee", "webclient");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Dashboard", redirect.Url);
        Assert.Equal("TestEmployee", session.GetString("ActiveRepositoryId"));
        Assert.Equal("Laserfiche Web Client", session.GetString("ActiveRepositorySource"));
        Assert.Equal("TestEmployee", session.GetString("AuthenticatedRepositoryId"));
        Assert.Equal("admin", session.GetString("AuthenticatedLaserficheUser"));
        Assert.False(auth.Invalidated);
        Assert.False(credentials.Cleared);
        Assert.False(correlation.Deleted);
        Assert.DoesNotContain("Dashboard.Cookie=;", context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Launch_PreservesSafeLocalReturnUrl()
    {
        var (controller, _, _) = MakeController(
            new SpyAuthService(), new SpyCredentialStore(), new SpyOAuthCookie(),
            LaserficheAuthenticationMode.LfdsSso);

        var result = await controller.Index(
            "NewLfRepo", "webclient", "/Dashboard?repository=NewLfRepo");

        var model = Assert.IsType<LaunchLoadingViewModel>(
            Assert.IsType<ViewResult>(result).Model);
        Assert.Contains("repository=NewLfRepo", model.RedirectUrl);
        Assert.Contains("returnUrl=%2FDashboard%3Frepository%3DNewLfRepo", model.RedirectUrl);
    }

    [Fact]
    public async Task Launch_RejectsNonWebClientSource()
    {
        var (controller, _, _) = MakeController(
            new SpyAuthService(), new SpyCredentialStore(), new SpyOAuthCookie(),
            LaserficheAuthenticationMode.RepositoryPassword);

        var result = await controller.Index("TestEmployee", "unknown");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void LaunchLoadingView_ContainsAutomaticStartSsoRedirect()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../LFPortal.Web/Views/Launch/LaunchLoading.cshtml"));
        var source = File.ReadAllText(path);

        Assert.Contains("Model.RedirectUrl", source);
        Assert.Contains("window.location.replace", source);
        Assert.Contains("300", source);
        Assert.Contains("Signing you in with your existing Laserfiche session.", source);
        Assert.DoesNotContain("prompt=login", source);
    }

    private static (LaunchController Controller, TestSession Session, HttpContext Context)
        MakeController(
            SpyAuthService auth,
            SpyCredentialStore credentials,
            SpyOAuthCookie correlation,
            LaserficheAuthenticationMode authenticationMode)
    {
        var context = new DefaultHttpContext();
        var session = new TestSession();
        context.Session = session;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddControllersWithViews();
        services.Configure<LaserficheOptions>(options =>
        {
            options.AuthenticationMode = authenticationMode;
        });
        services.AddAuthentication(DashboardAuthenticationDefaults.Scheme)
            .AddCookie(DashboardAuthenticationDefaults.Scheme);
        var provider = services.BuildServiceProvider();
        context.RequestServices = provider;

        var controller = new LaunchController(
            auth,
            credentials,
            correlation,
            provider.GetRequiredService<IOptionsMonitor<LaserficheOptions>>(),
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
        {
            var values = actionContext.Values!;
            var repository = values.GetType().GetProperty("repository")?.GetValue(values)?.ToString();
            var returnUrl = values.GetType().GetProperty("returnUrl")?.GetValue(values)?.ToString();
            return "/Login/StartSso?repository=" + Uri.EscapeDataString(repository!) +
                "&returnUrl=" + Uri.EscapeDataString(returnUrl!);
        }

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
