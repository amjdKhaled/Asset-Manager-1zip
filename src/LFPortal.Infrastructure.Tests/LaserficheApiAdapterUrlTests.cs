using LFPortal.Infrastructure.Adapters;
using LFPortal.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace LFPortal.Infrastructure.Tests;

/// <summary>
/// URL-builder tests for <see cref="LaserficheApiAdapter"/>. These guard the
/// exact concern from the login-failure investigation: the configured
/// <c>ApiBasePath</c> (<c>/LFRepositoryAPI</c>) must appear exactly once in
/// every built URL, regardless of how the ServerUrl was entered.
/// </summary>
public sealed class LaserficheApiAdapterUrlTests
{
    private static LaserficheApiAdapter CreateAdapter(
        string serverUrl,
        string apiBasePath = "/LFRepositoryAPI",
        string apiVersion  = "v1")
    {
        var options = new LaserficheOptions
        {
            ServerUrl    = serverUrl,
            ApiBasePath  = apiBasePath,
            ApiVersion   = apiVersion,
            RepositoryId = "Documents"
        };
        return new LaserficheApiAdapter(new StaticOptionsMonitor(options));
    }

    // ── Token URL (the login endpoint) ───────────────────────────────────────

    [Fact]
    public void TokenUrl_PlainServerUrl_ContainsBasePathOnce()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local");
        var url = adapter.BuildTokenUrl("Documents");

        Assert.Equal(
            "https://lf-server.corp.local/LFRepositoryAPI/v1/Repositories/Documents/Token",
            url);
    }

    [Fact]
    public void TokenUrl_ServerUrlAlreadyEndsWithBasePath_DoesNotDuplicate()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local/LFRepositoryAPI");
        var url = adapter.BuildTokenUrl("Documents");

        Assert.Equal(
            "https://lf-server.corp.local/LFRepositoryAPI/v1/Repositories/Documents/Token",
            url);
        Assert.Equal(1, CountOccurrences(url, "/LFRepositoryAPI"));
    }

    [Fact]
    public void TokenUrl_ServerUrlWithTrailingSlashes_IsNormalised()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local/LFRepositoryAPI///");
        var url = adapter.BuildTokenUrl("Documents");

        Assert.Equal(
            "https://lf-server.corp.local/LFRepositoryAPI/v1/Repositories/Documents/Token",
            url);
    }

    [Fact]
    public void TokenUrl_BasePathCaseInsensitiveMatch_DoesNotDuplicate()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local/lfrepositoryapi");
        var url = adapter.BuildTokenUrl("Documents");

        // Only one base-path segment, whatever its casing was.
        Assert.Equal(1, CountOccurrencesIgnoreCase(url, "/LFRepositoryAPI"));
        Assert.EndsWith("/v1/Repositories/Documents/Token", url);
    }

    [Fact]
    public void TokenUrlFor_ExplicitServerUrl_ContainsBasePathOnce()
    {
        var adapter = CreateAdapter("https://ignored.example");
        var url = adapter.BuildTokenUrlFor(
            "https://other-server.corp.local/LFRepositoryAPI", "Archive");

        Assert.Equal(
            "https://other-server.corp.local/LFRepositoryAPI/v1/Repositories/Archive/Token",
            url);
    }

    // ── Repositories URL (the diagnostics probe endpoint) ────────────────────

    [Fact]
    public void RepositoriesUrl_PlainServerUrl_IsCorrect()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local");
        Assert.Equal(
            "https://lf-server.corp.local/LFRepositoryAPI/v1/Repositories",
            adapter.BuildRepositoriesUrl());
    }

    [Fact]
    public void RepositoriesUrl_ServerUrlWithBasePath_DoesNotDuplicate()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local/LFRepositoryAPI/");
        var url = adapter.BuildRepositoriesUrl();

        Assert.Equal(
            "https://lf-server.corp.local/LFRepositoryAPI/v1/Repositories", url);
        Assert.Equal(1, CountOccurrences(url, "/LFRepositoryAPI"));
    }

    // ── Non-default base path and port ────────────────────────────────────────

    [Fact]
    public void TokenUrl_CustomBasePathAndPort_AreRespected()
    {
        var adapter = CreateAdapter(
            "http://lf-server.corp.local:8080", apiBasePath: "CustomApi");
        var url = adapter.BuildTokenUrl("Documents");

        Assert.Equal(
            "http://lf-server.corp.local:8080/CustomApi/v1/Repositories/Documents/Token",
            url);
    }

    [Fact]
    public void TokenUrl_ApiVersionWithSlashes_IsTrimmed()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local", apiVersion: "/v1/");
        var url = adapter.BuildTokenUrl("Documents");

        Assert.Equal(
            "https://lf-server.corp.local/LFRepositoryAPI/v1/Repositories/Documents/Token",
            url);
    }

    // ── API version v2 ───────────────────────────────────────────────────────

    [Fact]
    public void TokenUrl_V2_ContainsV2Segment()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local", apiVersion: "v2");
        var url = adapter.BuildTokenUrl("Documents");

        Assert.Equal(
            "https://lf-server.corp.local/LFRepositoryAPI/v2/Repositories/Documents/Token",
            url);
    }

    [Fact]
    public void RepositoriesUrl_V2_ContainsV2Segment()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local", apiVersion: "v2");
        Assert.Equal(
            "https://lf-server.corp.local/LFRepositoryAPI/v2/Repositories",
            adapter.BuildRepositoriesUrl());
    }

    [Fact]
    public void AllUrlBuilders_V1_ContainV1NotV2()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local", apiVersion: "v1");

        var urls = new[]
        {
            adapter.BuildTokenUrl("R"),
            adapter.BuildRepositoriesUrl(),
        };

        foreach (var url in urls)
        {
            Assert.Contains("/v1/", url);
            Assert.DoesNotContain("/v2/", url);
        }
    }

    // ── Repository ID URL encoding ────────────────────────────────────────────

    [Fact]
    public void TokenUrl_RepositoryWithSpace_IsPercentEncoded()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local");
        var url = adapter.BuildTokenUrl("My Repository");

        Assert.Contains("/Repositories/My%20Repository/Token", url);
        Assert.DoesNotContain("/Repositories/My Repository/Token", url);
    }

    [Fact]
    public void TokenUrl_RepositoryWithAmpersand_IsPercentEncoded()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local");
        var url = adapter.BuildTokenUrl("A&B");

        Assert.Contains("/Repositories/A%26B/Token", url);
    }

    [Fact]
    public void TokenUrl_RepositoryWithPlusSign_IsPercentEncoded()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local");
        var url = adapter.BuildTokenUrl("Finance+HR");

        // + must be encoded as %2B (not left raw, which would be misread as a space).
        Assert.Contains("/Repositories/Finance%2BHR/Token", url);
    }

    [Fact]
    public void TokenUrl_StandardAlphanumericRepository_IsUnchanged()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local");
        var url = adapter.BuildTokenUrl("LFNewRepoWF");

        // Plain alphanumeric names must not be altered.
        Assert.Contains("/Repositories/LFNewRepoWF/Token", url);
    }

    // ── BuildTokenUrlV2 — always uses /v2/, regardless of configured ApiVersion ──
    //
    // Requirement 15: BuildTokenUrlV2 must use the hard-coded /v2/ segment and
    // must ONLY be invoked by the SSO OAuth2 authorization-code exchange flow.
    // All normal V1 resource operations (entry listing, search, etc.) must use
    // BuildTokenUrl (which honours the configured/detected ApiVersion), never V2.

    [Fact]
    public void BuildTokenUrlV2_AlwaysContainsV2Segment_RegardlessOfConfiguredVersion()
    {
        // Even when the adapter is configured for v1, V2 SSO token exchange must
        // use /v2/ because the LFDS token endpoint is always V2.
        var adapter = CreateAdapter("https://lf-server.corp.local", apiVersion: "v1");
        var url     = adapter.BuildTokenUrlV2("Documents");

        // BuildTokenUrlV2 must always use /v2/ — it is the SSO token endpoint.
        Assert.Contains("/v2/", url);
        Assert.DoesNotContain("/v1/", url);
    }

    [Fact]
    public void BuildTokenUrlV2_DiffersFromBuildTokenUrl_WhenConfiguredAsV1()
    {
        // This guards the invariant that V2 SSO token exchange and V1 resource
        // operations use different URL paths — they must never be swapped.
        var adapter = CreateAdapter("https://lf-server.corp.local", apiVersion: "v1");

        var v1Url = adapter.BuildTokenUrl("Documents");
        var v2Url = adapter.BuildTokenUrlV2("Documents");

        // V2 SSO token URL must differ from the V1 resource token URL.
        Assert.NotEqual(v1Url, v2Url);
        Assert.Contains("/v1/", v1Url);
        Assert.Contains("/v2/", v2Url);
    }

    [Fact]
    public void BuildTokenUrlV2_ContainsRepositoryIdAndTokenSegment()
    {
        var adapter = CreateAdapter("https://lf-server.corp.local");
        var url     = adapter.BuildTokenUrlV2("Documents");

        Assert.Contains("/Repositories/Documents/Token", url);
    }

    [Fact]
    public void BuildTokenUrl_V1_DoesNotContainV2Segment()
    {
        // Verify that the regular V1 token URL never contains /v2/ — this guards
        // against accidentally routing V1 resource operations through the V2 path.
        var adapter = CreateAdapter("https://lf-server.corp.local", apiVersion: "v1");
        var url     = adapter.BuildTokenUrl("Documents");

        // V1 resource token URL must not contain /v2/ — V2 path is reserved for SSO BuildTokenUrlV2.
        Assert.DoesNotContain("/v2/", url);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static int CountOccurrencesIgnoreCase(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    /// <summary>Minimal fixed-value <see cref="IOptionsMonitor{T}"/> for tests.</summary>
    private sealed class StaticOptionsMonitor : IOptionsMonitor<LaserficheOptions>
    {
        public StaticOptionsMonitor(LaserficheOptions value) => CurrentValue = value;
        public LaserficheOptions CurrentValue { get; }
        public LaserficheOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<LaserficheOptions, string?> listener) => null;
    }
}
