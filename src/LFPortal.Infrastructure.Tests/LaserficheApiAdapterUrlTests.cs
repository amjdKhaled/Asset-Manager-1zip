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
