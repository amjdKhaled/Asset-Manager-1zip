// SetJsonStringFieldTests.cs
// Self-contained tests for the SetJsonStringField helper used by WriteConfigAction.
//
// These tests verify the two code paths that matter for port preservation during
// a repair or MajorUpgrade:
//
//   INSERT branch — appsettings.json comes from a fresh dotnet publish and has
//                   no "Urls" key.  WriteConfig must INSERT the key.
//
//   REPLACE branch — appsettings.json already contains a "Urls" key (left over
//                    from the previous WriteConfig run, e.g. on a repair where
//                    MSI re-lays the file and then WriteConfig runs again, OR on
//                    a MajorUpgrade where the old file happened to survive).
//                    WriteConfig must REPLACE the existing value.
//
// The logic is an exact copy of SetJsonStringField / EscJson from
// installer/Dashboard.SetupHelper/WriteConfigAction.cs (private helpers).
// Any change to those helpers must be reflected here.

using System;
using System.Text.RegularExpressions;

static class SetJsonStringFieldTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Logic copied verbatim from WriteConfigAction.cs (private helpers)
    // ──────────────────────────────────────────────────────────────────────────

    static string SetJsonStringField(string json, string fieldName, string value)
    {
        string escapedValue = EscJson(value);
        string fieldKey     = $"\"{fieldName}\"";

        int idx = json.IndexOf(fieldKey, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            int colon = json.IndexOf(':', idx + fieldKey.Length);
            if (colon >= 0)
            {
                int openQuote = json.IndexOf('"', colon + 1);
                if (openQuote >= 0)
                {
                    int closeQuote = json.IndexOf('"', openQuote + 1);
                    if (closeQuote >= 0)
                    {
                        return json.Substring(0, openQuote + 1)
                             + escapedValue
                             + json.Substring(closeQuote);
                    }
                }
            }
        }

        // Field not present — insert before the last closing brace.
        int lastBrace = json.LastIndexOf('}');
        if (lastBrace < 0)
            return json;

        string before = json.Substring(0, lastBrace).TrimEnd();
        string insert  = $",\r\n  {fieldKey}: \"{escapedValue}\"\r\n";
        if (before.EndsWith("{"))
            insert = $"\r\n  {fieldKey}: \"{escapedValue}\"\r\n";

        return before + insert + json.Substring(lastBrace);
    }

    static string EscJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
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

    // ──────────────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────────────

    static void Test_FreshFile_Insert_Port8080()
    {
        Console.WriteLine("\n[Scenario A] Fresh appsettings.json from publish (no Urls key) — INSERT branch");
        Console.WriteLine("  Simulates: MajorUpgrade re-lays new publish output, WriteConfig inserts Urls");

        // Typical appsettings.json produced by 'dotnet publish' — no "Urls" key.
        string freshSettings = "{\r\n" +
            "  \"Logging\": {\r\n" +
            "    \"LogLevel\": {\r\n" +
            "      \"Default\": \"Information\",\r\n" +
            "      \"Microsoft.AspNetCore\": \"Warning\"\r\n" +
            "    }\r\n" +
            "  },\r\n" +
            "  \"AllowedHosts\": \"*\"\r\n" +
            "}\r\n";

        string result = SetJsonStringField(freshSettings, "Urls", "http://0.0.0.0:8080");

        Assert("Result contains Urls key",
            result.Contains("\"Urls\""),
            $"Result:\n{result}");

        Assert("Result contains correct port value",
            result.Contains("\"http://0.0.0.0:8080\""),
            $"Result:\n{result}");

        Assert("Existing AllowedHosts field preserved",
            result.Contains("\"AllowedHosts\""),
            $"Result:\n{result}");

        Assert("Valid JSON structure (ends with })",
            result.TrimEnd().EndsWith("}"),
            $"Result:\n{result}");

        Assert("Urls field appears after AllowedHosts (appended at end)",
            result.IndexOf("\"Urls\"") > result.IndexOf("\"AllowedHosts\""),
            $"Result:\n{result}");
    }

    static void Test_FreshFile_Insert_Port5000()
    {
        Console.WriteLine("\n[Scenario B] Fresh appsettings.json — INSERT branch with default port 5000");

        string freshSettings = "{\r\n  \"AllowedHosts\": \"*\"\r\n}\r\n";

        string result = SetJsonStringField(freshSettings, "Urls", "http://0.0.0.0:5000");

        Assert("Contains Urls key with port 5000",
            result.Contains("\"Urls\"") && result.Contains("\"http://0.0.0.0:5000\""),
            $"Result:\n{result}");
    }

    static void Test_ExistingUrls_Replace_SamePort()
    {
        Console.WriteLine("\n[Scenario C] appsettings.json already has Urls (repair, file re-laid) — REPLACE branch, same port");
        Console.WriteLine("  Simulates: repair where WriteConfig ran before; Urls key exists from prior install");

        // File already has Urls (e.g. repair re-lays the stored-in-MSI version
        // which still has the Urls line from the prior WriteConfig run).
        string existingSettings = "{\r\n" +
            "  \"AllowedHosts\": \"*\",\r\n" +
            "  \"Urls\": \"http://0.0.0.0:8080\"\r\n" +
            "}\r\n";

        string result = SetJsonStringField(existingSettings, "Urls", "http://0.0.0.0:8080");

        Assert("Urls value preserved at 8080",
            result.Contains("\"http://0.0.0.0:8080\""),
            $"Result:\n{result}");

        Assert("AllowedHosts preserved",
            result.Contains("\"AllowedHosts\""),
            $"Result:\n{result}");

        // Exactly one occurrence of Urls key (no duplicate insertion)
        int count = Regex.Matches(result, "\"Urls\"").Count;
        Assert("Exactly one Urls key (no duplicate)",
            count == 1,
            $"Found {count} occurrences of \"Urls\" in:\n{result}");
    }

    static void Test_ExistingUrls_Replace_DifferentPort()
    {
        Console.WriteLine("\n[Scenario D] appsettings.json has old port 5000 — REPLACE branch, upgrade to 8080");
        Console.WriteLine("  Simulates: appsettings.json survived upgrade with old Urls; WriteConfig must update it");

        string settingsWithOldPort = "{\r\n" +
            "  \"AllowedHosts\": \"*\",\r\n" +
            "  \"Urls\": \"http://0.0.0.0:5000\"\r\n" +
            "}\r\n";

        string result = SetJsonStringField(settingsWithOldPort, "Urls", "http://0.0.0.0:8080");

        Assert("Old port 5000 no longer present as Urls value",
            !result.Contains("\"http://0.0.0.0:5000\""),
            $"Result:\n{result}");

        Assert("New port 8080 is present",
            result.Contains("\"http://0.0.0.0:8080\""),
            $"Result:\n{result}");

        int count = Regex.Matches(result, "\"Urls\"").Count;
        Assert("Exactly one Urls key after replace",
            count == 1,
            $"Found {count} occurrences in:\n{result}");
    }

    static void Test_UpgradeScenario_TwoWriteConfigRuns()
    {
        Console.WriteLine("\n[Scenario E] Full upgrade simulation: v1 install (insert), then v2 upgrade (insert on fresh file)");
        Console.WriteLine("  Simulates the MajorUpgrade path: old removed, fresh file laid, WriteConfig runs");

        // v1 install: fresh file, WriteConfig inserts Urls=8080
        string freshV1 = "{\r\n  \"AllowedHosts\": \"*\"\r\n}\r\n";
        string afterV1Install = SetJsonStringField(freshV1, "Urls", "http://0.0.0.0:8080");

        Assert("v1 install: Urls inserted correctly",
            afterV1Install.Contains("\"http://0.0.0.0:8080\""),
            $"After v1 install:\n{afterV1Install}");

        // MajorUpgrade: old product removed, new publish output re-laid (fresh file again),
        // then WriteConfig re-runs with the same DASHBOARD_PORT=8080 value from the Bundle.
        string freshV2 = "{\r\n  \"AllowedHosts\": \"*\"\r\n}\r\n"; // fresh from v2 publish
        string afterV2Upgrade = SetJsonStringField(freshV2, "Urls", "http://0.0.0.0:8080");

        Assert("v2 upgrade: Urls inserted on fresh file with correct port",
            afterV2Upgrade.Contains("\"http://0.0.0.0:8080\""),
            $"After v2 upgrade:\n{afterV2Upgrade}");

        Assert("v2 upgrade: no Urls key duplication",
            Regex.Matches(afterV2Upgrade, "\"Urls\"").Count == 1,
            $"After v2 upgrade:\n{afterV2Upgrade}");
    }

    static void Test_RepairScenario()
    {
        Console.WriteLine("\n[Scenario F] Repair simulation: MSI re-lays fresh file, WriteConfig re-patches port");
        Console.WriteLine("  On repair, MSI reinstalls files from source (no Urls); WriteConfig re-inserts correct port");

        // Repair re-lays the file from the MSI cabinet (original publish output, no Urls).
        string reinstalled = "{\r\n  \"AllowedHosts\": \"*\"\r\n}\r\n";
        string afterRepair = SetJsonStringField(reinstalled, "Urls", "http://0.0.0.0:8080");

        Assert("Repair: Urls re-inserted with correct port",
            afterRepair.Contains("\"http://0.0.0.0:8080\""),
            $"After repair:\n{afterRepair}");
    }

    static void Test_InsertOnMinimalJson()
    {
        Console.WriteLine("\n[Scenario G] Minimal JSON object '{}' — INSERT branch edge case");

        string minimal = "{}";
        string result = SetJsonStringField(minimal, "Urls", "http://0.0.0.0:8080");

        Assert("Urls inserted into minimal object",
            result.Contains("\"Urls\"") && result.Contains("\"http://0.0.0.0:8080\""),
            $"Result:\n{result}");

        Assert("No leading comma (empty object → no comma before first field)",
            !result.Contains(",\r\n  \"Urls\""),
            $"Result:\n{result}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Entry point
    // ──────────────────────────────────────────────────────────────────────────

    static int Main()
    {
        Console.WriteLine("=== SetJsonStringField upgrade/repair port preservation tests ===");
        Console.WriteLine("Verifies WriteConfigAction correctly patches appsettings.json Urls");
        Console.WriteLine("in both INSERT (fresh publish file) and REPLACE (existing Urls) branches.");

        Test_FreshFile_Insert_Port8080();
        Test_FreshFile_Insert_Port5000();
        Test_ExistingUrls_Replace_SamePort();
        Test_ExistingUrls_Replace_DifferentPort();
        Test_UpgradeScenario_TwoWriteConfigRuns();
        Test_RepairScenario();
        Test_InsertOnMinimalJson();

        Console.WriteLine($"\n  SetJsonStringField: {_pass} passed, {_fail} failed");

        // Also run config contract tests (SetupHelper ↔ LFPortal.Web contract).
        int contractResult = ConfigContractTests.Run();

        int totalFail = _fail + (contractResult != 0 ? 1 : 0);
        Console.WriteLine($"\n=== Overall: {(totalFail == 0 ? "ALL PASSED" : $"{totalFail} SUITE(S) FAILED")} ===");
        return totalFail > 0 ? 1 : 0;
    }
}
