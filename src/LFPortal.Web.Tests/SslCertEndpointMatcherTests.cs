// SslCertEndpointMatcherTests.cs
// Unit tests for the pure http.sys sslcert endpoint matching logic used by
// the installer wizard to VALIDATE (never create) HTTPS bindings.
// The source file is compile-linked from installer/Dashboard.BA/ like
// ApiHostSelector.cs.

using System.Collections.Generic;
using Dashboard.BA;
using Xunit;

namespace LFPortal.Web.Tests
{
    public class SslCertEndpointMatcherTests
    {
        private const string SampleNetshOutput = @"
SSL Certificate bindings:
-------------------------

    IP:port                      : 0.0.0.0:443
    Certificate Hash             : abc123
    Application ID               : {00000000-0000-0000-0000-000000000000}

    Hostname:port                : dash.example.com:5001
    Certificate Hash             : def456
    Application ID               : {00000000-0000-0000-0000-000000000000}

    IP:port                      : 10.0.0.25:6001
    Certificate Hash             : 789abc
    Application ID               : {00000000-0000-0000-0000-000000000000}
";

        [Fact]
        public void ParseEndpoints_ExtractsAllEndpointKinds()
        {
            var eps = SslCertEndpointMatcher.ParseEndpoints(SampleNetshOutput);
            Assert.Contains("0.0.0.0:443", eps);
            Assert.Contains("dash.example.com:5001", eps);
            Assert.Contains("10.0.0.25:6001", eps);
            Assert.Equal(3, eps.Count);
        }

        [Fact]
        public void Matches_ExactSniHostname()
        {
            var eps = SslCertEndpointMatcher.ParseEndpoints(SampleNetshOutput);
            Assert.True(SslCertEndpointMatcher.Matches(
                eps, "DASH.example.COM", 5001, null, out var ep));
            Assert.Equal("dash.example.com:5001", ep);
        }

        [Fact]
        public void Matches_WildcardIpServesAnyHost()
        {
            var eps = SslCertEndpointMatcher.ParseEndpoints(SampleNetshOutput);
            Assert.True(SslCertEndpointMatcher.Matches(
                eps, "anything.example.com", 443, null, out var ep));
            Assert.Equal("0.0.0.0:443", ep);
        }

        [Fact]
        public void Matches_ExplicitIp_OnlyWhenHostResolvesToIt()
        {
            var eps = SslCertEndpointMatcher.ParseEndpoints(SampleNetshOutput);

            // Host resolves to the bound IP -> match.
            Assert.True(SslCertEndpointMatcher.Matches(
                eps, "server-b", 6001, new List<string> { "10.0.0.25" }, out var ep));
            Assert.Equal("10.0.0.25:6001", ep);

            // Host resolves elsewhere -> NO match.
            Assert.False(SslCertEndpointMatcher.Matches(
                eps, "server-b", 6001, new List<string> { "10.0.0.99" }, out _));

            // Resolution failed -> explicit-IP endpoints never match.
            Assert.False(SslCertEndpointMatcher.Matches(
                eps, "server-b", 6001, null, out _));
        }

        [Fact]
        public void Matches_DifferentHostSamePort_Fails()
        {
            // SNI binding for dash.example.com must NOT validate another host
            // on the same port (the code-review scenario).
            var eps = SslCertEndpointMatcher.ParseEndpoints(SampleNetshOutput);
            Assert.False(SslCertEndpointMatcher.Matches(
                eps, "other.example.com", 5001, null, out _));
        }

        [Fact]
        public void Matches_DifferentPort_Fails()
        {
            var eps = SslCertEndpointMatcher.ParseEndpoints(SampleNetshOutput);
            Assert.False(SslCertEndpointMatcher.Matches(
                eps, "dash.example.com", 5002, null, out _));
        }

        [Fact]
        public void Matches_Ipv6Wildcard_And_BracketedResolution()
        {
            var eps = new List<string> { "[::]:8443", "[fe80::1]:9443" };
            Assert.True(SslCertEndpointMatcher.Matches(
                eps, "host", 8443, null, out var ep));
            Assert.Equal("[::]:8443", ep);

            Assert.True(SslCertEndpointMatcher.Matches(
                eps, "host", 9443, new List<string> { "fe80::1" }, out var ep6));
            Assert.Equal("[fe80::1]:9443", ep6);
        }

        [Fact]
        public void ParseEndpoints_EmptyOrNullOutput_ReturnsEmpty()
        {
            Assert.Empty(SslCertEndpointMatcher.ParseEndpoints(""));
            Assert.Empty(SslCertEndpointMatcher.ParseEndpoints(null!));
        }
    }
}
