using LFPortal.Application.DTOs;
using System.Security.Claims;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Common;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.OAuth;
using LFPortal.Infrastructure.Options;
using LFPortal.Web.Controllers;
using LFPortal.Web.Authentication;
using LFPortal.Web.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LFPortal.Web.Tests;

/// <summary>
/// Regression suite verifying that SSO remains dormant and the existing
/// username/password login flow is completely unchanged when
/// <c>Laserfiche:Sso:LfdsBaseUrl</c> is empty (the default).
///
/// Covers requirement 9 items:
///  - default configuration → SSO disabled
///  - GET /Login renders the normal login form, never redirects to LFDS
///  - no V2 SSO token endpoint is called during the password-grant flow
///  - existing V1 username/password login still works
///  - repository selection still works
///  - sign-out still works
///  - StartSso falls back to Login (not LFDS) when SSO is not configured
///  - session/repository isolation is preserved (no cross-session leakage)
/// </summary>
public sealed class LoginControllerSsoDormantTests
{
    // ── Default (dormant-SSO) options ─────────────────────────────────────────

    /// <summary>
    /// The out-of-the-box configuration — LfdsBaseUrl is empty → SSO is disabled.
    /// </summary>
    private static LaserficheOptions DefaultOptions() => new()
    {
        ServerUrl   = "http://lf-server.test",
        ApiBasePath = "/LFRepositoryAPI",
        ApiVersion  = "v1",
        // Sso section deliberately left at default: LfdsBaseUrl = ""
    };

    private static LaserficheOptions SsoOptions() => new()
    {
        ServerUrl   = "http://lf-server.test",
        DashboardPublicBaseUrl = "https://dashboard.test",
        ApiBasePath = "/LFRepositoryAPI",
        ApiVersion  = "v1",
        Sso         = new LaserficheOAuthOptions { LfdsBaseUrl = "https://lfds.example.com/LFDS" }
    };

    private static RepositoryDescriptor TestRepo() =>
        new("TestRepo", "http://lf-server.test", "TestRepo", "TestRepo");

    // ── Factory ───────────────────────────────────────────────────────────────

    private static (LoginController ctrl, SpyAuthService authSpy, SpyOAuthStateStore storeSpy)
        Build(LaserficheOptions? options = null, bool directBrowser = true)
    {
        var opts      = options ?? DefaultOptions();
        var authSpy   = new SpyAuthService();
        var repoCtx   = new StubRepositoryContext(TestRepo());
        var credStore = new StubSessionCredentialStore();
        var storeSpy  = new SpyOAuthStateStore();
        var monitor   = new StaticOptionsMonitor<LaserficheOptions>(opts);

        var httpCtx  = new DefaultHttpContext();
        var session  = new TestSession();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddControllersWithViews();
        services.AddAuthentication(DashboardAuthenticationDefaults.Scheme)
            .AddCookie(DashboardAuthenticationDefaults.Scheme, options =>
            {
                options.Cookie.Name = ".Dashboard.Authentication";
            });
        var serviceProvider = services.BuildServiceProvider();
        httpCtx.RequestServices = serviceProvider;
        httpCtx.Request.Scheme = "https";
        httpCtx.Request.Host = new HostString("dashboard.test");

        // Simulate a direct-browser session by leaving the source key absent.
        // For Desktop/Web Client sessions set source to the value the middleware writes.
        // "ActiveRepositorySource" is the internal session key in RepositorySessionMiddleware.
        if (!directBrowser)
            session.SetString("ActiveRepositorySource", "Laserfiche Web Client");

        httpCtx.Session = session;

        var ctrl = new LoginController(
            authSpy,
            repoCtx,
            credStore,
            storeSpy,
            new OAuthTransactionCookie(serviceProvider.GetRequiredService<IDataProtectionProvider>()),
            monitor,
            NullLogger<LoginController>.Instance);

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = httpCtx,
        };

        // Provide a minimal IUrlHelper so Url.Action / Url.IsLocalUrl work.
        ctrl.Url = new StubUrlHelper(httpCtx);

        return (ctrl, authSpy, storeSpy);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Default config → IsConfigured = false
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultOptions_SsoIsNotConfigured()
    {
        var opts = DefaultOptions();
        Assert.False(opts.Sso.IsConfigured);
    }

    [Fact]
    public void EmptyLfdsBaseUrl_SsoIsNotConfigured()
    {
        var opts = new LaserficheOAuthOptions { LfdsBaseUrl = "" };
        Assert.False(opts.IsConfigured);
    }

    [Fact]
    public void WhitespaceLfdsBaseUrl_SsoIsNotConfigured()
    {
        var opts = new LaserficheOAuthOptions { LfdsBaseUrl = "   " };
        Assert.False(opts.IsConfigured);
    }

    [Fact]
    public void NonEmptyLfdsBaseUrl_SsoIsConfigured()
    {
        var opts = new LaserficheOAuthOptions { LfdsBaseUrl = "https://lfds.example.com/LFDS" };
        Assert.True(opts.IsConfigured);
    }

    [Fact]
    public void SsoAuthorizationEndpoint_IsAlwaysRepositoryApiV2Authorize()
    {
        var opts = SsoOptions();
        opts.ServerUrl = "https://localhost/";
        opts.ApiBasePath = "LFRepositoryAPI/";

        Assert.Equal(
            "https://localhost/LFRepositoryAPI/v2/Authorize",
            opts.SsoAuthorizationEndpoint);
    }

    [Fact]
    public void SsoAuthorizationEndpoint_DoesNotDuplicateApiBasePathAlreadyInServerUrl()
    {
        var opts = SsoOptions();
        opts.ServerUrl = "https://localhost/LFRepositoryAPI/";
        opts.ApiBasePath = "/LFRepositoryAPI";

        Assert.Equal(
            "https://localhost/LFRepositoryAPI/v2/Authorize",
            opts.SsoAuthorizationEndpoint);
        Assert.DoesNotContain(
            "/LFRepositoryAPI/LFRepositoryAPI",
            opts.SsoAuthorizationEndpoint,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://localhost:5000", "http://localhost:5000/login/Callback")]
    [InlineData("http://desktop-k1svi53:5000", "http://desktop-k1svi53:5000/login/Callback")]
    public void DashboardPublicBaseUrl_ProducesDeterministicCallback(string baseUrl, string expected)
    {
        var opts = SsoOptions();
        opts.DashboardPublicBaseUrl = baseUrl;
        Assert.Equal(expected, opts.SsoCallbackUrl);
    }

    [Fact]
    public void MarkdownUrlConfiguration_IsRejected()
    {
        var opts = SsoOptions();
        opts.ServerUrl = "[https://localhost](https://localhost)";
        Assert.Contains("Laserfiche:ServerUrl", opts.MarkdownConfigurationKeys());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. GET /Login renders the password form — no LFDS redirect
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Get_DefaultConfig_ReturnsView()
    {
        var (ctrl, _, _) = Build();
        var result = await ctrl.Index(cancellationToken: default);
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Login_Get_DefaultConfig_IsNotRedirect()
    {
        var (ctrl, _, _) = Build();
        var result = await ctrl.Index(cancellationToken: default);
        Assert.IsNotType<RedirectResult>(result);
        Assert.IsNotType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Login_Get_DefaultConfig_DoesNotRedirectToStartSso()
    {
        var (ctrl, _, _) = Build();
        var result = await ctrl.Index(cancellationToken: default);

        // Must not be a redirect to StartSso.
        if (result is RedirectToActionResult rta)
            Assert.NotEqual("StartSso", rta.ActionName, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Get_WithSsoConfigured_NoSsoFailed_RedirectsToStartSso()
    {
        // Positive control: with SSO configured, the redirect DOES happen.
        var (ctrl, _, _) = Build(SsoOptions());
        var result = await ctrl.Index(cancellationToken: default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("StartSso", redirect.ActionName, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Get_WithSsoConfigured_SsoFailed_RendersForm()
    {
        // When ssoFailed=true, the password form is rendered even if SSO is configured.
        var (ctrl, _, _) = Build(SsoOptions());
        var result = await ctrl.Index(ssoFailed: true, cancellationToken: default);
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Login_Get_DefaultConfig_SsoFailedBanner_NotVisible()
    {
        // SsoFailed is only meaningful when SSO is configured.
        // With default config the banner flag must be false.
        var (ctrl, _, _) = Build();
        var result = await ctrl.Index(ssoFailed: true, cancellationToken: default);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<LoginViewModel>(view.Model);
        Assert.False(vm.SsoFailed); // SsoFailed = ssoFailed && opts.Sso.IsConfigured
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. StartSso when SSO not configured — must fall back to Login, not LFDS
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartSso_DefaultConfig_RedirectsToLoginNotLfds()
    {
        var (ctrl, _, _) = Build();
        var result = await ctrl.StartSso(cancellationToken: default);

        // Must redirect back to the Login action, not to any external LFDS URL.
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Login",  redirect.ControllerName, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartSso_DefaultConfig_DoesNotStoreOAuthState()
    {
        var (ctrl, _, storeSpy) = Build();
        await ctrl.StartSso(cancellationToken: default);

        // OAuthStateStore.Store must never be called when SSO is not configured.
        Assert.Equal(0, storeSpy.StoreCallCount);
    }

    [Fact]
    public async Task StartSso_DefaultConfig_DoesNotRedirectToExternalUrl()
    {
        var (ctrl, _, _) = Build();
        var result = await ctrl.StartSso(cancellationToken: default);

        // Must never be an external Redirect (which would hit LFDS).
        Assert.IsNotType<RedirectResult>(result);
    }

    [Fact]
    public async Task StartSso_WebClient_UsesRepositoryApiAuthorizeAndPreservesReturnUrl()
    {
        var options = SsoOptions();
        options.Sso.RedirectUri = "https://dashboard.test/Login/Callback";
        var (ctrl, _, store) = Build(options, directBrowser: false);

        var result = await ctrl.StartSso(
            returnUrl: "/Dashboard?repository=TestRepo&source=webclient",
            cancellationToken: default);

        var redirect = Assert.IsType<RedirectResult>(result);
        var uri = new Uri(redirect.Url!);
        Assert.Equal("http://lf-server.test/LFRepositoryAPI/v2/Authorize", uri.GetLeftPart(UriPartial.Path));
        Assert.DoesNotContain(
            "/LFRepositoryAPI/LFRepositoryAPI",
            redirect.Url,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LFDS", redirect.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("response_type=code", uri.Query);
        Assert.Contains("redirect_uri=https%3A%2F%2Fdashboard.test%2Flogin%2FCallback", uri.Query);
        Assert.Contains("state=", uri.Query);
        Assert.Contains("code_challenge=", uri.Query);
        Assert.Contains("code_challenge_method=S256", uri.Query);
        Assert.Equal("/Dashboard?repository=TestRepo&source=webclient", store.LastStoredEntry?.ReturnUrl);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. No V2 SSO token request during the password-grant flow
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Post_DefaultConfig_NeverCallsExchangeAuthorizationCode()
    {
        var (ctrl, authSpy, _) = Build();
        var input = new LoginInputModel { Username = "alice", Password = "secret" };

        await ctrl.Index(input, default);

        Assert.Equal(0, authSpy.ExchangeAuthCodeCallCount);
    }

    [Fact]
    public async Task Login_Post_DefaultConfig_CallsTryAuthenticate_NotSsoExchange()
    {
        var (ctrl, authSpy, _) = Build();
        var input = new LoginInputModel { Username = "alice", Password = "pass" };

        await ctrl.Index(input, default);

        Assert.True(authSpy.TryAuthenticateCallCount > 0, "TryAuthenticateAsync should have been called.");
        Assert.Equal(0, authSpy.ExchangeAuthCodeCallCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Existing V1 username/password login still works
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Post_ValidCredentials_RedirectsToDashboard()
    {
        var (ctrl, authSpy, _) = Build();
        authSpy.TryAuthenticateResult = true;

        var result = await ctrl.Index(
            new LoginInputModel { Username = "alice", Password = "secret" }, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index",     redirect.ActionName,    StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Dashboard", redirect.ControllerName, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Post_InvalidCredentials_ReturnsViewWithError()
    {
        var (ctrl, authSpy, _) = Build();
        authSpy.TryAuthenticateResult = false;

        var result = await ctrl.Index(
            new LoginInputModel { Username = "alice", Password = "wrong" }, default);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<LoginViewModel>(view.Model);
        Assert.NotNull(vm.ErrorMessage);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Login_Post_ValidCredentials_SetsAuthenticatedSessionKey()
    {
        var (ctrl, authSpy, _) = Build();
        authSpy.TryAuthenticateResult = true;
        var session = (TestSession)ctrl.HttpContext.Session;

        await ctrl.Index(
            new LoginInputModel { Username = "alice", Password = "secret" }, default);

        // "AuthenticatedRepositoryId" is the internal key in SessionAuthGuardMiddleware.
        var authRepo = session.GetString("AuthenticatedRepositoryId");
        Assert.Equal("TestRepo", authRepo);
    }

    [Fact]
    public async Task Login_Post_ValidCredentials_WritesAuthenticationCookie()
    {
        var (ctrl, authSpy, _) = Build();
        authSpy.TryAuthenticateResult = true;

        await ctrl.Index(
            new LoginInputModel { Username = "alice", Password = "secret" }, default);

        Assert.Contains(
            ".Dashboard.Authentication=",
            ctrl.HttpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Sso_Callback_Success_WritesAuthenticationCookieAndSessionMarker()
    {
        var (ctrl, _, store) = Build(SsoOptions(), directBrowser: false);

        await ctrl.StartSso(returnUrl: "/Dashboard", cancellationToken: default);
        var state = ctrl.HttpContext.Session.GetString("OAuth_PendingState");
        Assert.False(string.IsNullOrWhiteSpace(state));

        var result = await ctrl.Callback(code: "valid-code", state: state, cancellationToken: default);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Dashboard", redirect.Url);
        Assert.Equal("TestRepo", ctrl.HttpContext.Session.GetString("ActiveRepositoryId"));
        Assert.Equal("TestRepo", ctrl.HttpContext.Session.GetString("AuthenticatedRepositoryId"));
        Assert.Contains(
            ".Dashboard.Authentication=",
            ctrl.HttpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Sso_Callback_CookieAuthenticatesPrincipalOnNextRequest()
    {
        var (ctrl, _, _) = Build(SsoOptions(), directBrowser: false);
        await ctrl.StartSso(returnUrl: "/Dashboard", cancellationToken: default);
        var state = ctrl.HttpContext.Session.GetString("OAuth_PendingState");

        await ctrl.Callback(code: "valid-code", state: state, cancellationToken: default);

        var setCookie = ctrl.HttpContext.Response.Headers.SetCookie.ToString();
        var cookiePair = setCookie.Split(';', 2)[0];
        using var nextScope = ctrl.HttpContext.RequestServices.CreateScope();
        var nextRequest = new DefaultHttpContext
        {
            RequestServices = nextScope.ServiceProvider,
        };
        nextRequest.Request.Scheme = "https";
        nextRequest.Request.Host = new HostString("dashboard.test");
        nextRequest.Request.Headers.Cookie = cookiePair;

        var authentication = await nextRequest.AuthenticateAsync(
            DashboardAuthenticationDefaults.Scheme);

        Assert.True(authentication.Succeeded,
            authentication.Failure?.ToString() ?? "No authentication failure was reported.");
        Assert.True(authentication.Principal?.Identity?.IsAuthenticated);
        Assert.Equal(
            "TestRepo",
            authentication.Principal?.FindFirst(
                DashboardAuthenticationDefaults.RepositoryClaimType)?.Value);
        Assert.Equal(
            DashboardAuthenticationDefaults.LfdsAuthenticationMethod,
            authentication.Principal?.FindFirst(ClaimTypes.AuthenticationMethod)?.Value);
    }

    [Fact]
    public async Task Sso_Callback_WithoutSessionOrCookie_ReturnsCorrelationCookieMissing()
    {
        var (ctrl, auth, _) = Build(SsoOptions(), directBrowser: false);

        var result = await ctrl.Callback(
            code: "valid-code",
            state: "state-from-another-session",
            cancellationToken: default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Diagnostic", redirect.ActionName);
        Assert.Equal("oauth_correlation_cookie_missing", redirect.RouteValues?["reason"]);
        Assert.Equal(0, auth.ExchangeAuthCodeCallCount);
    }

    [Fact]
    public async Task StartSso_WritesProtectedHttpOnlyCorrelationCookie()
    {
        var (ctrl, _, _) = Build(SsoOptions(), directBrowser: false);

        await ctrl.StartSso("/Dashboard", default);

        var cookie = ctrl.Response.Headers.SetCookie.ToString();
        Assert.Contains(".Dashboard.OAuth.Correlation=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code_verifier", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sso_Callback_ValidCookieSucceedsWhenAspNetSessionIsLost_AndDeletesCookie()
    {
        var (ctrl, auth, _) = Build(SsoOptions(), directBrowser: false);
        await ctrl.StartSso("/Dashboard", default);
        var state = ctrl.HttpContext.Session.GetString("OAuth_PendingState");
        var correlation = ctrl.Response.Headers.SetCookie.ToString().Split(';', 2)[0];
        ctrl.HttpContext.Request.Headers.Cookie = correlation;
        ctrl.HttpContext.Session = new TestSession();

        var result = await ctrl.Callback("valid-code", state, cancellationToken: default);

        Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal(1, auth.ExchangeAuthCodeCallCount);
        Assert.Equal("https://dashboard.test/login/Callback", auth.LastExchangeRedirectUri);
        Assert.Equal("TestRepo", ctrl.HttpContext.Session.GetString("ActiveRepositoryId"));
        Assert.Equal("TestRepo", ctrl.HttpContext.Session.GetString("AuthenticatedRepositoryId"));
        Assert.Contains(
            ".Dashboard.OAuth.Correlation=; expires=",
            ctrl.Response.Headers.SetCookie.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sso_Callback_CookieStateMismatch_ReturnsSpecificReason()
    {
        var (ctrl, auth, _) = Build(SsoOptions(), directBrowser: false);
        await ctrl.StartSso("/Dashboard", default);
        var correlation = ctrl.Response.Headers.SetCookie.ToString().Split(';', 2)[0];
        ctrl.HttpContext.Request.Headers.Cookie = correlation;
        ctrl.HttpContext.Session = new TestSession();

        var result = await ctrl.Callback("valid-code", "different-state", cancellationToken: default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Diagnostic", redirect.ActionName);
        Assert.Equal("oauth_state_mismatch", redirect.RouteValues?["reason"]);
        Assert.Equal(0, auth.ExchangeAuthCodeCallCount);
    }

    [Fact]
    public async Task Sso_Callback_TokenExchangeFailure_ReturnsDiagnosticNotPasswordLogin()
    {
        var (ctrl, auth, _) = Build(SsoOptions(), directBrowser: false);
        auth.ExchangeException = new LaserficheException("Rejected", 401);
        await ctrl.StartSso("/Dashboard", default);
        var state = ctrl.HttpContext.Session.GetString("OAuth_PendingState");
        ctrl.HttpContext.Request.Headers.Cookie =
            ctrl.Response.Headers.SetCookie.ToString().Split(';', 2)[0];

        var result = await ctrl.Callback("code", state, cancellationToken: default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Diagnostic", redirect.ActionName);
        Assert.Equal("token_exchange_failed", redirect.RouteValues?["reason"]);
        Assert.NotEqual("Index", redirect.ActionName);
    }

    [Fact]
    public async Task Login_Get_SsoFailure_DisplaysSanitizedSpecificReason()
    {
        var (ctrl, _, _) = Build(SsoOptions(), directBrowser: false);

        var result = await ctrl.Index(
            ssoFailed: true,
            ssoFailure: "token_exchange_failed",
            cancellationToken: default);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LoginViewModel>(view.Model);
        Assert.True(model.SsoFailed);
        Assert.Equal(
            "Repository API rejected the authorization-code exchange.",
            model.SsoFailureReason);
    }

    [Fact]
    public async Task Sso_Callback_UntrustedSaml_ReturnsDiagnosticPageWithoutPasswordFallback()
    {
        var (ctrl, auth, _) = Build(SsoOptions(), directBrowser: false);
        auth.ExchangeException = new LaserficheException(
            "Rejected", 403, "9530", "Received an invalid or untrusted SAML token. [9530]");
        await ctrl.StartSso(returnUrl: "/Dashboard", cancellationToken: default);
        var state = ctrl.HttpContext.Session.GetString("OAuth_PendingState");

        var result = await ctrl.Callback("code", state, cancellationToken: default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Diagnostic", redirect.ActionName);
        Assert.Equal("saml_token_untrusted", redirect.RouteValues?["reason"]);
        Assert.NotEqual("Index", redirect.ActionName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Repository selection works (direct browser vs. client launch)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Get_DirectBrowser_AllowsRepositoryInput()
    {
        var (ctrl, _, _) = Build(directBrowser: true);
        var result = await ctrl.Index(cancellationToken: default);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<LoginViewModel>(view.Model);
        Assert.True(vm.AllowRepositoryInput);
    }

    [Fact]
    public async Task Login_Get_WebClientLaunch_DisallowsRepositoryInput()
    {
        var (ctrl, _, _) = Build(directBrowser: false);
        var result = await ctrl.Index(cancellationToken: default);

        var view = Assert.IsType<ViewResult>(result);
        var vm   = Assert.IsType<LoginViewModel>(view.Model);
        Assert.False(vm.AllowRepositoryInput);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. Sign-out still works
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SignOut_RedirectsToLoginIndex()
    {
        var (ctrl, _, _) = Build();
        var result = await ctrl.SignOut(default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName,    StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Login", redirect.ControllerName, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignOut_CallsInvalidateCurrentSessionTokens()
    {
        var (ctrl, authSpy, _) = Build();
        await ctrl.SignOut(default);

        Assert.True(authSpy.InvalidateCurrentSessionCalled,
            "InvalidateCurrentSessionTokensAsync should have been called on sign-out.");
    }

    [Fact]
    public async Task SignOut_RemovesAuthenticatedRepoIdFromSession()
    {
        var (ctrl, _, _) = Build();
        var session = (TestSession)ctrl.HttpContext.Session;

        // Simulate an authenticated session.
        session.SetString("AuthenticatedRepositoryId", "TestRepo");

        await ctrl.SignOut(default);

        var authRepo = session.GetString("AuthenticatedRepositoryId");
        Assert.Null(authRepo);
    }

    [Fact]
    public async Task SignOut_RemovesPendingOAuthStateFromSession()
    {
        var (ctrl, _, _) = Build();
        var session = (TestSession)ctrl.HttpContext.Session;

        // "OAuth_PendingState" is the internal session key in LoginController.
        session.SetString("OAuth_PendingState", "stale-state");

        await ctrl.SignOut(default);

        Assert.Null(session.GetString("OAuth_PendingState"));
    }

    [Fact]
    public async Task SignOut_ExpiresAuthenticationCookie()
    {
        var (ctrl, _, _) = Build();

        await ctrl.SignOut(default);

        var setCookie = ctrl.HttpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains(".Dashboard.Authentication=", setCookie);
        Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Session / repository isolation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Post_Success_SetsRepositoryIdInSession()
    {
        var (ctrl, authSpy, _) = Build();
        authSpy.TryAuthenticateResult = true;
        var session = (TestSession)ctrl.HttpContext.Session;

        await ctrl.Index(new LoginInputModel { Username = "bob", Password = "pw" }, default);

        // Both session keys must be consistent.
        // "ActiveRepositoryId" and "AuthenticatedRepositoryId" are the internal keys in
        // RepositorySessionMiddleware and SessionAuthGuardMiddleware respectively.
        var sessionRepo = session.GetString("ActiveRepositoryId");
        var authRepo    = session.GetString("AuthenticatedRepositoryId");

        Assert.Equal("TestRepo", sessionRepo);
        Assert.Equal("TestRepo", authRepo);
    }

    [Fact]
    public async Task TwoIndependentControllers_HaveIsolatedSessions()
    {
        // Two independent controller instances must have independent sessions —
        // a successful login in one must not affect the other.
        var (ctrl1, authSpy1, _) = Build();
        var (ctrl2, _, _)        = Build();

        authSpy1.TryAuthenticateResult = true;
        await ctrl1.Index(new LoginInputModel { Username = "u", Password = "p" }, default);

        var session2 = (TestSession)ctrl2.HttpContext.Session;
        Assert.Null(session2.GetString("AuthenticatedRepositoryId"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Stubs and spies
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class SpyAuthService : ILaserficheAuthService
    {
        public bool TryAuthenticateResult       { get; set; } = true;
        public int  TryAuthenticateCallCount    { get; private set; }
        public int  ExchangeAuthCodeCallCount   { get; private set; }
        public bool InvalidateCurrentSessionCalled { get; private set; }
        public Exception? ExchangeException { get; set; }
        public string? LastExchangeRedirectUri { get; private set; }

        public Task<bool> TryAuthenticateAsync(
            RepositoryDescriptor r, string u, string p, CancellationToken ct = default)
        {
            TryAuthenticateCallCount++;
            return Task.FromResult(TryAuthenticateResult);
        }

        public Task<bool> ExchangeAuthorizationCodeAsync(
            RepositoryDescriptor r, string code, string verifier,
            string redirectUri, string clientId, CancellationToken ct = default)
        {
            ExchangeAuthCodeCallCount++;
            LastExchangeRedirectUri = redirectUri;
            if (ExchangeException is not null)
                return Task.FromException<bool>(ExchangeException);
            return Task.FromResult(true);
        }

        public Task<string>  GetTokenAsync(RepositoryDescriptor r, CancellationToken ct = default) =>
            Task.FromResult("stub-token");

        public Task InvalidateTokenAsync(RepositoryDescriptor r) => Task.CompletedTask;

        public Task InvalidateCurrentSessionTokensAsync()
        {
            InvalidateCurrentSessionCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class SpyOAuthStateStore : IOAuthStateStore
    {
        private readonly Dictionary<string, OAuthStateEntry> _entries = new();
        public int StoreCallCount { get; private set; }
        public OAuthStateEntry? LastStoredEntry { get; private set; }

        public void Store(string state, OAuthStateEntry entry)
        {
            StoreCallCount++;
            LastStoredEntry = entry;
            _entries[state] = entry;
        }

        public OAuthStateEntry? TryConsume(string state)
        {
            if (!_entries.Remove(state, out var entry))
                return null;
            return entry.ExpiresAt > DateTimeOffset.UtcNow ? entry : null;
        }
    }

    private sealed class StubRepositoryContext : IRepositoryContext
    {
        private readonly RepositoryDescriptor _repo;
        public StubRepositoryContext(RepositoryDescriptor repo) => _repo = repo;

        public Task<RepositoryDescriptor> GetActiveRepositoryAsync(CancellationToken ct = default) =>
            Task.FromResult(_repo);

        public Task<IReadOnlyList<RepositoryDescriptor>> GetAllRepositoriesAsync(
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RepositoryDescriptor>>(new[] { _repo });
    }

    private sealed class StubSessionCredentialStore : ISessionCredentialStore
    {
        public Task StoreAsync(string u, string p, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<LaserficheCredential?> TryGetAsync(CancellationToken ct = default) =>
            Task.FromResult<LaserficheCredential?>(null);

        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>Minimal IUrlHelper for unit tests.</summary>
    private sealed class StubUrlHelper : IUrlHelper
    {
        public StubUrlHelper(HttpContext ctx) => ActionContext =
            new ActionContext(ctx, new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        public ActionContext ActionContext { get; }

        // IsLocalUrl: replicate the ASP.NET Core logic for local-URL detection.
        public bool IsLocalUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return (url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')))
                || (url.Length > 1 && url[0] == '~' && url[1] == '/');
        }

        public string? Action(UrlActionContext ctx) =>
            $"/{ctx.Controller}/{ctx.Action}";

        public string? Content(string? contentPath) => contentPath;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext ctx) => null;
    }

    /// <summary>In-memory ISession for unit tests.</summary>
    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _data = new();

        public bool   IsAvailable  => true;
        public string Id           => "test-session-id";
        public IEnumerable<string> Keys => _data.Keys;

        public void   Clear()                          => _data.Clear();
        public void   Remove(string key)               => _data.Remove(key);
        public void   Set(string key, byte[] value)    => _data[key] = value;
        public bool   TryGetValue(string key, out byte[] value) =>
            _data.TryGetValue(key, out value!);

        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken   ct = default) => Task.CompletedTask;
    }
}
