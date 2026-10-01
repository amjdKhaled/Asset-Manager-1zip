using System.Net;
using System.Reflection;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LFPortal.Infrastructure.Tests;

public sealed class BearerTokenRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedToken_ReleasesResponsesAndStopsAfterOneRetry(bool freshTokenAvailable)
    {
        var calls = 0;
        var invalidatedSession = false;
        var auth = Proxy<ILaserficheAuthService>(method =>
        {
            if (method.Name == "GetTokenAsync")
            {
                calls++;
                return calls == 1 || freshTokenAvailable ? Task.FromResult("token")
                    : Task.FromException<string>(new UnauthorizedAccessException());
            }
            if (method.Name == "InvalidateCurrentSessionTokensAsync") invalidatedSession = true;
            return Task.CompletedTask;
        });
        var contents = new[] { new Content(), new Content() };
        var upstream = new Rejected(contents);
        var handler = new BearerTokenHandler(auth,
            Proxy<IRepositoryContext>(_ => Task.FromResult(new RepositoryDescriptor("test", "https://lf.test", "Repo", "Repo"))),
            NullLogger<BearerTokenHandler>.Instance) { InnerHandler = upstream };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetAsync("https://lf.test/Entries"));
        Assert.True(contents[0].WasDisposed);
        Assert.Equal(freshTokenAvailable ? 2 : 1, upstream.Calls);
        Assert.Equal(freshTokenAvailable, invalidatedSession);
        if (freshTokenAvailable) Assert.True(contents[1].WasDisposed);
    }
    private sealed class Content() : StringContent("unauthorized")
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
    private sealed class Rejected(Content[] contents) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = contents[Calls++] });
    }
    private static T Proxy<T>(Func<MethodInfo, object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, Calls>(); ((Calls)(object)result).Call = call; return result;
    }
    public class Calls : DispatchProxy
    {
        public Func<MethodInfo, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!);
    }
}
