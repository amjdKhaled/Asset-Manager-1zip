// ConfigContractTests.cs
// Validates the configuration contract between Dashboard.SetupHelper (which
// generates laserfiche.config.json at install time) and the LFPortal.Web runtime
// (which binds it to LaserficheOptions via Microsoft.Extensions.Configuration).
//
// Rules under test:
//   1. Generated JSON is well-formed.
//   2. Laserfiche:ServerUrl is present and non-empty.
//   3. Laserfiche:RepositoryId is written from the installer wizard.
//   4. Laserfiche:DisplayName is written from the installer wizard.
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
    // (all fields collected by the installer are persisted).
    // ──────────────────────────────────────────────────────────────────────────

    static string BuildLaserficheConfigJson(
        string serverUrl,
        string repositoryId = "TestEmployee",
        string displayName = "TestEmployee",
        string apiBasePath = "/LFRepositoryAPI",
        string apiVersion  = "Auto",
        int    rootEntryId = 1,
        int    timeout     = 30)
    {
        return "{\r\n" +
               "  \"Laserfiche\": {\r\n" +
               $"    \"ServerUrl\": \"{EscJson(serverUrl)}\",\r\n" +
               $"    \"RepositoryId\": \"{EscJson(repositoryId)}\",\r\n" +
               $"    \"DisplayName\": \"{EscJson(displayName)}\",\r\n" +
               $"    \"ApiBasePath\": \"{EscJson(apiBasePath)}\",\r\n" +
               $"    \"ApiVersion\": \"{EscJson(apiVersion)}\",\r\n" +
               $"    \"RootEntryId\": {rootEntryId},\r\n" +
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

        // 3. RepositoryId written
        Assert("RepositoryId is present",
            lf.TryGetProperty("RepositoryId", out var repository) &&
            repository.GetString() == "TestEmployee", $"JSON:\n{json}");

        // 4. DisplayName written
        Assert("DisplayName is present",
            lf.TryGetProperty("DisplayName", out var display) &&
            display.GetString() == "TestEmployee", $"JSON:\n{json}");

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

    static void Test_InstallerRepositorySettings_AreWritten()
    {
        Console.WriteLine("\n[Scenario C] Installer repository settings are persisted");

        string json = BuildLaserficheConfigJson(
            "https://lf-server/LFRepositoryAPI", "Records", "Corporate Records");
        var lf = ParseJson(json).RootElement.GetProperty("Laserfiche");

        Assert("RepositoryId matches installer input",
            lf.GetProperty("RepositoryId").GetString() == "Records", $"JSON:\n{json}");

        Assert("DisplayName matches installer input",
            lf.GetProperty("DisplayName").GetString() == "Corporate Records", $"JSON:\n{json}");
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
        Console.WriteLine("Verifies laserfiche.config.json structure and installer repository settings,");
        Console.WriteLine("correct URL construction, and no LFRepositoryAPI double-append.");

        Test_Standard_NewInstall();
        Test_ServerUrl_Already_Contains_ApiBasePath();
        Test_InstallerRepositorySettings_AreWritten();
        Test_TimeoutSeconds_Is_Integer_Not_String();

        Console.WriteLine($"\n  Config contract: {_pass} passed, {_fail} failed");
        return _fail > 0 ? 1 : 0;
    }
}
