using System.Net.Sockets;
using System.Security.Authentication;
using LFPortal.Web.Diagnostics;
using Xunit;

namespace LFPortal.Web.Tests;

/// <summary>
/// Tests for the shared transport-error classifier used by both the login flow
/// and the /api/diagnostics/laserfiche probe. Focuses on the ordering fix:
/// an <see cref="HttpRequestException"/> with a precise
/// <see cref="HttpRequestError"/> must win over its generic inner
/// <see cref="SocketException"/>.
/// </summary>
public sealed class LaserficheErrorClassifierTests
{
    // ── DNS ───────────────────────────────────────────────────────────────────

    [Fact]
    public void DnsFailure_WithInnerSocketException_IsClassifiedAsDnsError()
    {
        // Real .NET 8 shape: HttpRequestException(NameResolutionError)
        // wrapping SocketException(HostNotFound).
        var ex = new HttpRequestException(
            HttpRequestError.NameResolutionError,
            "No such host is known.",
            new SocketException((int)SocketError.HostNotFound));

        var result = LaserficheErrorClassifier.Classify(ex);

        Assert.Equal("dns-error", result.Code);
        Assert.Contains("DNS", result.Detail);
    }

    [Fact]
    public void BareSocketHostNotFound_IsClassifiedAsDnsError()
    {
        var ex = new SocketException((int)SocketError.HostNotFound);
        Assert.Equal("dns-error", LaserficheErrorClassifier.Classify(ex).Code);
    }

    // ── Connection refused ────────────────────────────────────────────────────

    [Fact]
    public void ConnectionRefused_WithInnerSocketException_IsClassifiedAsRefused()
    {
        var ex = new HttpRequestException(
            HttpRequestError.ConnectionError,
            "Connection refused",
            new SocketException((int)SocketError.ConnectionRefused));

        var result = LaserficheErrorClassifier.Classify(ex);

        Assert.Equal("connection-refused", result.Code);
        Assert.Contains("refused", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectionError_WithoutInnerSocketException_IsClassifiedAsRefused()
    {
        var ex = new HttpRequestException(HttpRequestError.ConnectionError, "Connection error");
        Assert.Equal("connection-refused", LaserficheErrorClassifier.Classify(ex).Code);
    }

    // ── TLS ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SecureConnectionError_IsClassifiedAsTlsError()
    {
        var ex = new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established",
            new AuthenticationException("The remote certificate is invalid."));

        Assert.Equal("tls-error", LaserficheErrorClassifier.Classify(ex).Code);
    }

    [Fact]
    public void WrappedAuthenticationException_IsClassifiedAsTlsError()
    {
        // Some handlers wrap without setting HttpRequestError — the inner
        // AuthenticationException must still be found.
        var ex = new InvalidOperationException(
            "wrapper", new AuthenticationException("certificate invalid"));

        Assert.Equal("tls-error", LaserficheErrorClassifier.Classify(ex).Code);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public void TaskCanceled_IsClassifiedAsTimeout()
    {
        Assert.Equal("timeout",
            LaserficheErrorClassifier.Classify(new TaskCanceledException()).Code);
    }

    [Fact]
    public void SocketTimedOut_IsClassifiedAsTimeout()
    {
        var ex = new SocketException((int)SocketError.TimedOut);
        Assert.Equal("timeout", LaserficheErrorClassifier.Classify(ex).Code);
    }

    // ── Fallbacks ─────────────────────────────────────────────────────────────

    [Fact]
    public void GenericHttpRequestException_IsClassifiedAsConnectionFailed()
    {
        var ex = new HttpRequestException("something went wrong");
        Assert.Equal("connection-failed", LaserficheErrorClassifier.Classify(ex).Code);
    }

    [Fact]
    public void OtherSocketError_IsClassifiedAsNetworkErrorWithCode()
    {
        var ex = new SocketException((int)SocketError.NetworkUnreachable);
        var result = LaserficheErrorClassifier.Classify(ex);

        Assert.Equal("network-error", result.Code);
        Assert.Contains("NetworkUnreachable", result.Detail);
    }

    [Fact]
    public void UnknownException_IsClassifiedAsUnknownError()
    {
        Assert.Equal("unknown-error",
            LaserficheErrorClassifier.Classify(new InvalidOperationException("boom")).Code);
    }
}
