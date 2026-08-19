using System.Security.Claims;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Common;
using LFPortal.Infrastructure.Options;
using LFPortal.Web.Authentication;
using LFPortal.Web.Controllers;
using LFPortal.Web.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LFPortal.Web.Tests;

public sealed class ShareControllerTests
{
    [Fact]
    public void LoginGet_WhenEnabledAndKeyMatches_RendersShareLogin()
    {
        var fixture = Build();

        var result = fixture.Controller.Login("test-access-key");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ExternalShareLoginViewModel>(view.Model);
        Assert.Equal(["TestRepo"], model.Repositories);
        Assert.Equal("true", fixture.Session.GetString("ExternalShare.AccessGranted"));
    }

    [Fact]
    public void LoginGet_WhenEnabledButKeyIsWrong_ReturnsForbiddenNotNotFound()
    {
        var fixture = Build();

        var result = Assert.IsType<StatusCodeResult>(fixture.Controller.Login("wrong-key"));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task LoginPost_UsesRepositoryPasswordAndRedirectsOnlyToShareDashboard()
    {
        var fixture = Build();
        GrantAccess(fixture.Session);

        var result = await fixture.Controller.Login(new ExternalShareLoginInput
        {
            Repository = "TestRepo",
            Username = "share-user",
            Password = "not-persisted-in-cookie"
        }, default);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Share/Dashboard", redirect.Url);
        Assert.Equal(1, fixture.Auth.PasswordAuthenticationCalls);
        Assert.Equal("TestRepo", fixture.Auth.LastRepository?.RepositoryId);
        Assert.Equal("share-user", fixture.Auth.LastUsername);
        Assert.True(fixture.Authentication.SignInCalled);
        Assert.DoesNotContain(fixture.Session.Keys, key =>
            key.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fixture.Authentication.Principal!.Claims, claim =>
            claim.Value == "not-persisted-in-cookie");
        Assert.DoesNotContain("StartSso", redirect.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SsoDiagnostic", redirect.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dashboard_AfterShareLogin_ReturnsRealDashboardViewModel()
    {
        var fixture = Build();
        fixture.Session.SetString("ExternalShare.Authenticated", "true");
        fixture.Session.SetString("ActiveRepositoryId", "TestRepo");
        fixture.Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "share-user"),
            new Claim("external_share_repository", "TestRepo")
        ], "ExternalShare.Cookie"));

        var result = await fixture.Controller.Dashboard(default);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Dashboard/Index.cshtml", view.ViewName);
        var model = Assert.IsType<DashboardViewModel>(view.Model);
        Assert.Same(fixture.Dashboard.Stats, model.Stats);
        Assert.True(Assert.IsType<bool>(fixture.Controller.ViewData["ExternalShareReadOnly"]));
        Assert.Equal(1, fixture.Dashboard.Calls);
    }

    [Fact]
    public void LoginGet_WhenDisabled_DoesNotExposeRoute()
    {
        var fixture = Build(enabled: false);
        Assert.IsType<NotFoundResult>(fixture.Controller.Login("test-access-key"));
    }

    private static Fixture Build(bool enabled = true)
    {
        var auth = new SpyAuthService();
        var dashboard = new StubDashboardService();
        var credentials = new SpyCredentialStore();
        var authentication = new SpyAuthenticationService();
        var session = new TestSession();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddControllersWithViews();
        serviceCollection.AddSingleton<IAuthenticationService>(authentication);
        var services = serviceCollection.BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            Session = session,
            RequestServices = services
        };

        var controller = new ShareController(
            auth,
            dashboard,
            new StubRepositoryContext(),
            new StaticOptionsMonitor<ExternalShareOptions>(new ExternalShareOptions
            {
                Enabled = enabled,
                AccessKey = "test-access-key",
                ReadOnly = true,
                Repositories = ["TestRepo"]
            }),
            new StaticOptionsMonitor<LaserficheOptions>(new LaserficheOptions
            {
                ServerUrl = "https://repository.test",
                RepositoryId = "TestRepo"
            }),
            NullLogger<ShareController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        return new Fixture(controller, auth, dashboard, authentication, session);
    }

    private static void GrantAccess(ISession session)
    {
        session.SetString("ExternalShare.AccessGranted", "true");
        session.SetString(
            "ExternalShare.ExpiresUtc",
            DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString());
    }

    private sealed record Fixture(
        ShareController Controller,
        SpyAuthService Auth,
        StubDashboardService Dashboard,
        SpyAuthenticationService Authentication,
        TestSession Session);

    private sealed class SpyAuthService : ILaserficheAuthService
    {
        public int PasswordAuthenticationCalls { get; private set; }
        public RepositoryDescriptor? LastRepository { get; private set; }
        public string? LastUsername { get; private set; }
        public Task<string> GetTokenAsync(RepositoryDescriptor repository, CancellationToken cancellationToken = default) => Task.FromResult("token");
        public Task InvalidateTokenAsync(RepositoryDescriptor repository) => Task.CompletedTask;
        public Task InvalidateCurrentSessionTokensAsync() => Task.CompletedTask;
        public Task<bool> TryAuthenticateAsync(RepositoryDescriptor repository, string username, string password, CancellationToken cancellationToken = default)
        {
            PasswordAuthenticationCalls++;
            LastRepository = repository;
            LastUsername = username;
            return Task.FromResult(true);
        }
        public Task<bool> ExchangeAuthorizationCodeAsync(RepositoryDescriptor repository, string code, string codeVerifier, string redirectUri, string clientId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Share routes must never exchange an OAuth code.");
    }

    private sealed class StubDashboardService : ILaserficheDashboardService
    {
        public DashboardStatsDto Stats { get; } = new() { IsConnected = true, TotalDocuments = 4 };
        public int Calls { get; private set; }
        public Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Stats);
        }
    }

    private sealed class StubRepositoryContext : IRepositoryContext
    {
        public Task<RepositoryDescriptor> GetActiveRepositoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RepositoryDescriptor("TestRepo", "https://repository.test", "TestRepo", "TestRepo"));
        public Task<IReadOnlyList<RepositoryDescriptor>> GetAllRepositoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryDescriptor>>(
                [new RepositoryDescriptor("TestRepo", "https://repository.test", "TestRepo", "TestRepo")]);
    }

    private sealed class SpyAuthenticationService : IAuthenticationService
    {
        public bool SignInCalled { get; private set; }
        public ClaimsPrincipal? Principal { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignInCalled = true;
            Principal = principal;
            Assert.Equal("ExternalShare.Cookie", scheme);
            Assert.True(properties?.ExpiresUtc <= DateTimeOffset.UtcNow.AddHours(2).AddSeconds(2));
            return Task.CompletedTask;
        }
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }
}
