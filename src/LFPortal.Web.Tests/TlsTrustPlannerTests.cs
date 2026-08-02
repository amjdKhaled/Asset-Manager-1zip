using Dashboard.SetupHelper;
using Xunit;

namespace LFPortal.Web.Tests;

/// <summary>
/// Regression tests for the installer's certificate trust decision logic.
/// TlsTrustPlanner.cs is compile-linked from installer/Dashboard.SetupHelper/
/// so these tests run against the REAL production code, not a copy.
/// Pure decision logic — no certificate store, no network.
/// </summary>
public sealed class TlsTrustPlannerTests
{
    private static readonly DateTime Now = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    private static TrustPlanInput Base() => new()
    {
        IsSelfSigned           = true,
        ChainTrusted           = false,
        ChainUntrustedRootOnly = true,
        ChainStatusSummary     = "UntrustedRoot",
        NotBeforeUtc           = Now.AddYears(-1),
        NotAfterUtc            = Now.AddYears(1),
        NowUtc                 = Now,
        RequestedHost          = "localhost",
        DnsSans                = new List<string> { "localhost" },
        IpSans                 = new List<string>(),
        AlreadyInRootStore     = false,
        IsElevated             = true,
        OperatorConsented      = true,
        Issuer                 = "CN=localhost"
    };

    // TEST 1: Trusted CA certificate -> no trust-store modification.
    [Fact]
    public void Trusted_certificate_results_in_no_store_change()
    {
        var i = Base();
        i.IsSelfSigned = false;
        i.ChainTrusted = true;
        i.ChainUntrustedRootOnly = false;
        i.ChainStatusSummary = "Valid";

        var plan = TlsTrustPlanner.Decide(i);

        Assert.Equal(TrustPlanAction.None, plan.Action);
        Assert.Contains("already trusted", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // TEST 2: Valid self-signed + exact SAN match + UntrustedRoot -> allowed.
    [Fact]
    public void Valid_selfsigned_matching_host_untrusted_root_is_allowed()
    {
        var plan = TlsTrustPlanner.Decide(Base());
        Assert.Equal(TrustPlanAction.InstallPublicCertificateToRoot, plan.Action);
    }

    // TEST 3: Self-signed + hostname mismatch -> DENIED.
    [Fact]
    public void Selfsigned_hostname_mismatch_is_denied()
    {
        var i = Base();
        i.RequestedHost = "other-server";

        var plan = TlsTrustPlanner.Decide(i);

        Assert.Equal(TrustPlanAction.Deny, plan.Action);
        Assert.Contains("identity", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // TEST 4: Expired self-signed certificate -> DENIED.
    [Fact]
    public void Expired_selfsigned_is_denied()
    {
        var i = Base();
        i.NotAfterUtc = Now.AddDays(-1);

        var plan = TlsTrustPlanner.Decide(i);

        Assert.Equal(TrustPlanAction.Deny, plan.Action);
        Assert.Contains("expired", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Not_yet_valid_selfsigned_is_denied()
    {
        var i = Base();
        i.NotBeforeUtc = Now.AddDays(1);

        Assert.Equal(TrustPlanAction.Deny, TlsTrustPlanner.Decide(i).Action);
    }

    // TEST 5: CA-issued leaf + UntrustedRoot -> never added to Root.
    [Fact]
    public void Ca_issued_leaf_with_untrusted_chain_is_reported_not_trusted()
    {
        var i = Base();
        i.IsSelfSigned = false;
        i.Issuer = "CN=Contoso Enterprise CA";
        i.ChainStatusSummary = "PartialChain";
        i.ChainUntrustedRootOnly = false;

        var plan = TlsTrustPlanner.Decide(i);

        Assert.Equal(TrustPlanAction.ReportCaChainProblem, plan.Action);
        Assert.Contains("Contoso Enterprise CA", plan.Reason);
        Assert.Contains("Local Computer certificate store", plan.Reason);
    }

    // TEST 6: Certificate already present in LocalMachine\Root -> no-op.
    [Fact]
    public void Already_in_root_store_is_a_noop()
    {
        var i = Base();
        i.AlreadyInRootStore = true;

        var plan = TlsTrustPlanner.Decide(i);

        Assert.Equal(TrustPlanAction.None, plan.Action);
        Assert.Contains("already present", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // TEST 7: IP URL but only DNS SAN exists -> identity mismatch.
    [Fact]
    public void Ip_host_with_only_dns_san_is_identity_mismatch()
    {
        Assert.False(TlsTrustPlanner.IdentityMatches(
            "192.168.102.111", new List<string> { "localhost" }, new List<string>()));

        var i = Base();
        i.RequestedHost = "192.168.102.111";
        Assert.Equal(TrustPlanAction.Deny, TlsTrustPlanner.Decide(i).Action);
    }

    [Fact]
    public void Ip_host_with_matching_ip_san_is_identity_match()
    {
        Assert.True(TlsTrustPlanner.IdentityMatches(
            "192.168.102.111", new List<string>(), new List<string> { "192.168.102.111" }));
    }

    // TEST 8: localhost URL + SAN localhost -> identity match.
    [Fact]
    public void Localhost_with_localhost_san_matches()
    {
        Assert.True(TlsTrustPlanner.IdentityMatches(
            "localhost", new List<string> { "localhost" }, new List<string>()));
    }

    // TEST 9: Wildcard DNS SAN -> RFC-style behavior.
    [Fact]
    public void Wildcard_san_matches_single_label_only()
    {
        var sans = new List<string> { "*.domain.local" };
        Assert.True(TlsTrustPlanner.IdentityMatches("host.domain.local", sans, new List<string>()));
        Assert.False(TlsTrustPlanner.IdentityMatches("a.b.domain.local", sans, new List<string>()));
        Assert.False(TlsTrustPlanner.IdentityMatches("domain.local", sans, new List<string>()));
        // A bare "*" SAN never matches.
        Assert.False(TlsTrustPlanner.IdentityMatches("host", new List<string> { "*" }, new List<string>()));
    }

    // TEST 10: After trust the chain rebuilds valid -> planner reports no
    // further action (final verification PASS path).
    [Fact]
    public void After_trust_established_chain_valid_means_no_further_action()
    {
        var i = Base();
        i.ChainTrusted = true;          // chain rebuilt after Root install
        i.AlreadyInRootStore = true;
        i.ChainStatusSummary = "Valid";

        var plan = TlsTrustPlanner.Decide(i);

        Assert.Equal(TrustPlanAction.None, plan.Action);
    }

    // Guard rails: no elevation / no consent.
    [Fact]
    public void Not_elevated_is_denied()
    {
        var i = Base();
        i.IsElevated = false;
        Assert.Equal(TrustPlanAction.Deny, TlsTrustPlanner.Decide(i).Action);
    }

    [Fact]
    public void Operator_decline_results_in_no_store_change_with_warning()
    {
        var i = Base();
        i.OperatorConsented = false;

        var plan = TlsTrustPlanner.Decide(i);

        Assert.Equal(TrustPlanAction.None, plan.Action);
        Assert.Contains("declined", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
