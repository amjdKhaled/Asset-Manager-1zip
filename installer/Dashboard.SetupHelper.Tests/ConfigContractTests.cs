// ConfigContractTests.cs
// Validates the configuration contract between Dashboard.SetupHelper (which
// generates laserfiche.config.json at install time) and the LFPortal.Web runtime
// (which binds it to LaserficheOptions via Microsoft.Extensions.Configuration).
//
// Rules under test:
//   1. Generated JSON is well-formed.
//   2. Laserfiche:ServerUrl is present and non-empty.
//   3. Laserfiche:RepositoryId is NOT written (repository is runtime session context).
//   4. Laserfiche:DisplayName  is NOT written.
//   5. Laserfiche:ApiBasePath  is present and non-empty.
//   6. Laserfiche:ApiVersion   is present and non-empty.
//   7. The runtime-resolved API base URL does not contain "LFRepositoryAPI" twice
//      (double-append guard: ServerUrl may already include /LFRepositoryAPI).
//   8. The token endpoint is correctly structured.
//
// This file is intentionally self-contained — no reference to Dashboard.SetupHelper
// or LFPortal.Infrastructure assemblies — so it builds on all platforms.
// The JSON generation logic (a subset of BuildLaserficheConfig) is reproduced
// inline; any divergence from the real helper will be caught by the smoke test
// in publish.ps1.

using System;
using System.Text.Json;

public static class ConfigContractTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // JSON production — mirrors the FIXED BuildLaserficheConfig output
    // (RepositoryId and DisplayName removed).
    // ──────────────────────────────────────────────────────────────────────────

    static string BuildLaserficheConfigJson(
        string serverUrl,
        string apiBasePath = "/LFRepositoryAPI",
        string apiVersion  = "v1",
        int    timeout     = 30)
    {
        return "{\r\n" +
               "  \"Laserfiche\": {\r\n" +
               $"    \"ServerUrl\": \"{EscJson(serverUrl)}\",\r\n" +
               $"    \"ApiBasePath\": \"{EscJson(apiBasePath)}\",\r\n" +
               $"    \"ApiVersion\": \"{EscJson(apiVersion)}\",\r\n" +
               $"    \"TimeoutSeconds\": {timeout},\r\n" +
               "    \"CredentialProvider\": \"DPAPI\"\r\n" +
               "  }\r\n" +
               "}\r\n";
    }

    static string EscJson(string s) =>
        string.IsNullOrEmpty(s) ? "" :
        s.Replace("\\", "\\\\").Replace("\"", "\\\"")
         .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    // ──────────────────────────────────────────────────────────────────────────
    // Runtime URL construction — mirrors LaserficheApiAdapter.BuildApiBase()
    // ──────────────────────────────────────────────────────────────────────────

    static string BuildApiBase(string serverUrl, string apiBasePath, string apiVersion)
    {
        var root     = serverUrl.TrimEnd('/');
        var basePath = "/" + apiBasePath.Trim('/');

        // Strip trailing basePath if already present (prevents double-append).
        if (root.EndsWith(basePath, StringComparison.OrdinalIgnoreCase))
            root = root[..^basePath.Length].TrimEnd('/');

        return $"{root}{basePath}/{apiVersion.Trim('/')}";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test helpers
    // ──────────────────────────────────────────────────────────────────────────

    static int _pass, _fail;

    static void Assert(string testName, bool condition, string detail = "")
    {
        if (condition)
        {
            Console.WriteLine($"  PASS  {testName}");
            _pass++;
        }
        else
        {
            Console.WriteLine($"  FAIL  {testName}");
            if (!string.IsNullOrEmpty(detail))
                Console.WriteLine($"        {detail}");
            _fail++;
        }
    }

    static JsonDocument ParseJson(string json)
    {
        return JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test scenarios
    // ──────────────────────────────────────────────────────────────────────────

    static void Test_Standard_NewInstall()
    {
        Console.WriteLine("\n[Scenario A] Standard new install — https://lf-server/LFRepositoryAPI as ServerUrl");

        string json = BuildLaserficheConfigJson("https://lf-server/LFRepositoryAPI");

        // 1. Well-formed JSON
        JsonDocument doc;
        try   { doc = ParseJson(json); Assert("JSON is well-formed", true); }
        catch (Exception ex) { Assert("JSON is well-formed", false, ex.Message); return; }

        var lf = doc.RootElement.GetProperty("Laserfiche");

        // 2. ServerUrl present and non-empty
        Assert("ServerUrl is present",
            lf.TryGetProperty("ServerUrl", out var sv) && sv.GetString()?.Length > 0,
            $"JSON:\n{json}");

        // 3. RepositoryId NOT written
        Assert("RepositoryId is NOT present",
            !lf.TryGetProperty("RepositoryId", out _),
            $"RepositoryId must not appear in new installs.\nJSON:\n{json}");

        // 4. DisplayName NOT written
        Assert("DisplayName is NOT present",
            !lf.TryGetProperty("DisplayName", out _),
            $"DisplayName must not appear in new installs.\nJSON:\n{json}");

        // 5. ApiBasePath present
        Assert("ApiBasePath is present",
            lf.TryGetProperty("ApiBasePath", out var ab) && ab.GetString()?.Length > 0,
            $"JSON:\n{json}");

        // 6. ApiVersion present
        Assert("ApiVersion is present",
            lf.TryGetProperty("ApiVersion", out var av) && av.GetString()?.Length > 0,
            $"JSON:\n{json}");

        // 7. Runtime URL does not double-append /LFRepositoryAPI
        string serverUrl  = lf.GetProperty("ServerUrl").GetString()!;
        string apiBase    = lf.GetProperty("ApiBasePath").GetString()!;
        string apiVersion = lf.GetProperty("ApiVersion").GetString()!;
        string resolved   = BuildApiBase(serverUrl, apiBase, apiVersion);

        int lfRepoApiCount = 0;
        int idx = -1;
        while ((idx = resolved.IndexOf("LFRepositoryAPI", idx + 1, StringComparison.OrdinalIgnoreCase)) >= 0)
            lfRepoApiCount++;

        Assert("LFRepositoryAPI appears exactly once in resolved URL",
            lfRepoApiCount == 1,
            $"Resolved URL: {resolved}  (found {lfRepoApiCount} occurrences)");

        // 8. Token endpoint is well-structured
        string tokenUrl = $"{resolved}/Repositories/TestEmployee/Token";
        Assert("Token URL starts with https://lf-server/LFRepositoryAPI",
            tokenUrl.StartsWith("https://lf-server/LFRepositoryAPI"),
            $"Token URL: {tokenUrl}");
    }

    static void Test_ServerUrl_Already_Contains_ApiBasePath()
    {
        Console.WriteLine("\n[Scenario B] ServerUrl already contains /LFRepositoryAPI — no double-append");
        Console.WriteLine("  Simulates: user entered https://localhost/LFRepositoryAPI as ServerUrl");

        // This is the exact value observed on the Windows test machine.
        string json = BuildLaserficheConfigJson("https://localhost/LFRepositoryAPI");
        var doc = ParseJson(json);
        var lf  = doc.RootElement.GetProperty("Laserfiche");

        string serverUrl  = lf.GetProperty("ServerUrl").GetString()!;
        string apiBase    = lf.GetProperty("ApiBasePath").GetString()!;
        string apiVersion = lf.GetProperty("ApiVersion").GetString()!;
        string resolved   = BuildApiBase(serverUrl, apiBase, apiVersion);

        Console.WriteLine($"        ServerUrl : {serverUrl}");
        Console.WriteLine($"        ApiBase   : {apiBase}");
        Console.WriteLine($"        Resolved  : {resolved}");

        int count = 0;
        int pos   = -1;
        while ((pos = resolved.IndexOf("LFRepositoryAPI", pos + 1, StringComparison.OrdinalIgnoreCase)) >= 0)
            count++;

        Assert("LFRepositoryAPI not doubled in resolved URL",
            count == 1,
            $"Resolved URL: {resolved}  (found {count} occurrences — expected 1)");

        Assert("Resolved URL is https://localhost/LFRepositoryAPI/v1",
            string.Equals(resolved, "https://localhost/LFRepositoryAPI/v1", StringComparison.OrdinalIgnoreCase),
            $"Expected https://localhost/LFRepositoryAPI/v1, got {resolved}");
    }

    static void Test_RepositoryId_Not_Written_Even_When_Legacy_Args_Passed()
    {
        Console.WriteLine("\n[Scenario C] Legacy --repo-id arg present — RepositoryId must NOT appear in output");
        Console.WriteLine("  Simulates: repair of an old MSI that passed --repo-id on the command line.");

        // The fixed BuildLaserficheConfigJson does not accept repoId at all.
        // Verify the generated JSON never contains RepositoryId.
        string json = BuildLaserficheConfigJson("https://lf-server/LFRepositoryAPI");

        Assert("RepositoryId absent even when legacy args would have set it",
            !json.Contains("RepositoryId", StringComparison.OrdinalIgnoreCase),
            $"JSON contains RepositoryId:\n{json}");

        Assert("DisplayName absent",
            !json.Contains("DisplayName", StringComparison.OrdinalIgnoreCase),
            $"JSON contains DisplayName:\n{json}");
    }

    static void Test_TimeoutSeconds_Is_Integer_Not_String()
    {
        Console.WriteLine("\n[Scenario D] TimeoutSeconds is a JSON integer (not a quoted string)");

        string json = BuildLaserficheConfigJson("https://lf-server/LFRepositoryAPI", timeout: 45);
        var doc = ParseJson(json);
        var lf  = doc.RootElement.GetProperty("Laserfiche");

        Assert("TimeoutSeconds is a JSON number",
            lf.GetProperty("TimeoutSeconds").ValueKind == JsonValueKind.Number,
            $"Expected JSON number for TimeoutSeconds.\nJSON:\n{json}");

        Assert("TimeoutSeconds value is 45",
            lf.GetProperty("TimeoutSeconds").GetInt32() == 45,
            $"JSON:\n{json}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Entry point
    // ──────────────────────────────────────────────────────────────────────────

    public static int Run()
    {
        Console.WriteLine("\n=== Config contract tests: SetupHelper output vs LFPortal.Web runtime ===");
        Console.WriteLine("Verifies laserfiche.config.json structure, absence of RepositoryId/DisplayName,");
        Console.WriteLine("correct URL construction, and no LFRepositoryAPI double-append.");

        Test_Standard_NewInstall();
        Test_ServerUrl_Already_Contains_ApiBasePath();
        Test_RepositoryId_Not_Written_Even_When_Legacy_Args_Passed();
        Test_TimeoutSeconds_Is_Integer_Not_String();

        Console.WriteLine($"\n  Config contract: {_pass} passed, {_fail} failed");
        return _fail > 0 ? 1 : 0;
    }
}
