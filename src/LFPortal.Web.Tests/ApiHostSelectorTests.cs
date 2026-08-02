using Dashboard.BA;
using Xunit;

namespace LFPortal.Web.Tests;

/// <summary>
/// Regression tests for the installer's certificate-driven API host selection.
/// The selector source file is compile-linked from installer/Dashboard.BA/ so
/// these tests run against the REAL production code, not a copy.
///
/// Covers the required TLS guarantees:
///  - localhost is rejected as an automatic choice unless the certificate covers it.
///  - the machine host name / FQDN is selected when the certificate matches.
///  - an expired certificate is rejected.
///  - an untrusted chain produces a warning and NEVER a certificate bypass —
///    the selector has no mechanism to weaken validation, only to warn.
/// </summary>
public sealed class ApiHostSelectorTests
{
    private static readonly DateTime Now = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    private static ApiCertificateInfo Cert(params string[] dnsNames) => new()
    {
        DnsNames     = dnsNames.ToList(),
        NotBeforeUtc = Now.AddYears(-1),
        NotAfterUtc  = Now.AddYears(1),
        ChainTrusted = true
    };

    // --- localhost must never be an automatic default -----------------------

    [Fact]
    public void Localhost_is_rejected_when_certificate_does_not_cover_it()
    {
        var sel = ApiHostSelector.SelectHost(
            bindingHost: "", machineFqdn: "", machineName: "OTHER-MACHINE",
            cert: Cert("DESKTOP-K1SVI53"), nowUtc: Now);

        Assert.Null(sel.Host);
        Assert.Contains("match", sel.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Localhost_is_selected_only_when_certificate_explicitly_covers_it()
    {
        var sel = ApiHostSelector.SelectHost(
            "", "", "NO-MATCH-NAME", Cert("localhost"), Now);

        Assert.Equal("localhost", sel.Host);
    }

    // --- machine name / FQDN selection --------------------------------------

    [Fact]
    public void Machine_name_is_selected_when_certificate_matches()
    {
        var sel = ApiHostSelector.SelectHost(
            "", "", "DESKTOP-K1SVI53", Cert("DESKTOP-K1SVI53"), Now);

        Assert.Equal("DESKTOP-K1SVI53", sel.Host);
        Assert.Equal("", sel.Warning);
    }

    [Fact]
    public void Fqdn_is_preferred_over_machine_name_when_both_match()
    {
        var sel = ApiHostSelector.SelectHost(
            "", "server.domain.local", "SERVER",
            Cert("server.domain.local", "SERVER"), Now);

        Assert.Equal("server.domain.local", sel.Host);
    }

    [Fact]
    public void Binding_host_is_preferred_when_certificate_valid()
    {
        var sel = ApiHostSelector.SelectHost(
            "lf.example.com", "server.domain.local", "SERVER",
            Cert("lf.example.com", "server.domain.local"), Now);

        Assert.Equal("lf.example.com", sel.Host);
    }

    [Fact]
    public void Wildcard_certificate_matches_single_label_host()
    {
        var sel = ApiHostSelector.SelectHost(
            "", "server.domain.local", "SERVER", Cert("*.domain.local"), Now);

        Assert.Equal("server.domain.local", sel.Host);
        // Wildcards must not span labels.
        Assert.False(ApiHostSelector.Matches("a.b.domain.local", new[] { "*.domain.local" }));
    }

    // --- expiry and trust ----------------------------------------------------

    [Fact]
    public void Expired_certificate_is_rejected()
    {
        var cert = Cert("DESKTOP-K1SVI53");
        cert.NotAfterUtc = Now.AddDays(-1);

        var sel = ApiHostSelector.SelectHost("", "", "DESKTOP-K1SVI53", cert, Now);

        Assert.Null(sel.Host);
        Assert.Contains("expired", sel.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Untrusted_chain_returns_host_with_LocalMachine_trust_warning_not_bypass()
    {
        var cert = Cert("DESKTOP-K1SVI53");
        cert.ChainTrusted = false;

        var sel = ApiHostSelector.SelectHost("", "", "DESKTOP-K1SVI53", cert, Now);

        Assert.Equal("DESKTOP-K1SVI53", sel.Host);
        Assert.Contains("LocalMachine", sel.Warning);
        // The selection result carries no flag that could weaken validation.
        Assert.DoesNotContain("bypass", sel.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_certificate_selects_nothing()
    {
        var sel = ApiHostSelector.SelectHost("", "", "SERVER", null, Now);
        Assert.Null(sel.Host);
        Assert.NotEqual("", sel.Warning);
    }

    // --- host matching -------------------------------------------------------

    [Fact]
    public void Match_is_case_insensitive()
    {
        Assert.True(ApiHostSelector.Matches("desktop-k1svi53", new[] { "DESKTOP-K1SVI53" }));
    }
}
