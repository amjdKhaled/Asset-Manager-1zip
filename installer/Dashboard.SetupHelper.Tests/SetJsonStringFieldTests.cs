// SetJsonStringFieldTests.cs
// Tests for JsonHelpers.SetJsonStringField, JsonHelpers.ReadPortFromAppsettings,
// and related helpers used by WriteConfigAction.
//
// These tests cover the code paths that matter for port preservation during
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
//   PORT PRESERVATION (Task #34) — when the MSI is repaired directly without
//                    the Burn bundle UI (msiexec /fa), DASHBOARD_PORT has no
//                    default and is therefore absent from the WriteConfig command
//                    line.  ReadPortFromAppsettings must read back the port
//                    already written in appsettings.json so it is never reset.
//
// The logic lives in installer/Dashboard.SetupHelper/JsonHelpers.cs and is
// compiled into this project via <Compile Include> source-linking.  There is
// no copy of the code here; changes to the helper are immediately reflected.

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// Bring the shared helpers into scope without requiring qualification on
// every call.  The class is compiled into this assembly via source-linking.
using Dashboard.SetupHelper;

static class SetJsonStringFieldTests
{
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
    // SetJsonStringField tests
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

        string result = JsonHelpers.SetJsonStringField(freshSettings, "Urls", "http://0.0.0.0:8080");

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

        string result = JsonHelpers.SetJsonStringField(freshSettings, "Urls", "http://0.0.0.0:5000");

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

        string result = JsonHelpers.SetJsonStringField(existingSettings, "Urls", "http://0.0.0.0:8080");

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

        string result = JsonHelpers.SetJsonStringField(settingsWithOldPort, "Urls", "http://0.0.0.0:8080");

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
        string afterV1Install = JsonHelpers.SetJsonStringField(freshV1, "Urls", "http://0.0.0.0:8080");

        Assert("v1 install: Urls inserted correctly",
            afterV1Install.Contains("\"http://0.0.0.0:8080\""),
            $"After v1 install:\n{afterV1Install}");

        // MajorUpgrade: old product removed, new publish output re-laid (fresh file again),
        // then WriteConfig re-runs with the same DASHBOARD_PORT=8080 value from the Bundle.
        string freshV2 = "{\r\n  \"AllowedHosts\": \"*\"\r\n}\r\n"; // fresh from v2 publish
        string afterV2Upgrade = JsonHelpers.SetJsonStringField(freshV2, "Urls", "http://0.0.0.0:8080");

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
        string afterRepair = JsonHelpers.SetJsonStringField(reinstalled, "Urls", "http://0.0.0.0:8080");

        Assert("Repair: Urls re-inserted with correct port",
            afterRepair.Contains("\"http://0.0.0.0:8080\""),
            $"After repair:\n{afterRepair}");
    }

    static void Test_InsertOnMinimalJson()
    {
        Console.WriteLine("\n[Scenario G] Minimal JSON object '{}' — INSERT branch edge case");

        string minimal = "{}";
        string result = JsonHelpers.SetJsonStringField(minimal, "Urls", "http://0.0.0.0:8080");

        Assert("Urls inserted into minimal object",
            result.Contains("\"Urls\"") && result.Contains("\"http://0.0.0.0:8080\""),
            $"Result:\n{result}");

        Assert("No leading comma (empty object → no comma before first field)",
            !result.Contains(",\r\n  \"Urls\""),
            $"Result:\n{result}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ReadPortFromAppsettings tests (Task #34: direct-MSI repair port
    // preservation — when --port is absent from the WriteConfig command line,
    // WriteConfigAction reads the existing port from appsettings.json instead
    // of resetting to the 5000 default).
    // ──────────────────────────────────────────────────────────────────────────

    static void Test_ReadPort_FromExistingAppsettings()
    {
        Console.WriteLine("\n[Scenario H] ReadPortFromAppsettings — reads port from existing Urls key");
        Console.WriteLine("  Simulates: direct-MSI repair; --port absent; appsettings.json has Urls=8080");

        string dir = Path.Combine(Path.GetTempPath(), "DashTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string appsettings = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(appsettings,
                "{\r\n  \"AllowedHosts\": \"*\",\r\n  \"Urls\": \"http://0.0.0.0:8080\"\r\n}\r\n",
                new UTF8Encoding(false));

            int port = JsonHelpers.ReadPortFromAppsettings(dir);

            Assert("ReadPortFromAppsettings returns 8080",
                port == 8080,
                $"Returned: {port}");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static void Test_ReadPort_FreshFile_NoUrls()
    {
        Console.WriteLine("\n[Scenario I] ReadPortFromAppsettings — fresh publish file has no Urls key → returns 0");
        Console.WriteLine("  Simulates: MajorUpgrade re-laid appsettings.json before WriteConfig ran");

        string dir = Path.Combine(Path.GetTempPath(), "DashTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string appsettings = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(appsettings,
                "{\r\n  \"Logging\": {},\r\n  \"AllowedHosts\": \"*\"\r\n}\r\n",
                new UTF8Encoding(false));

            int port = JsonHelpers.ReadPortFromAppsettings(dir);

            Assert("ReadPortFromAppsettings returns 0 when Urls absent",
                port == 0,
                $"Returned: {port}  (expected 0 — caller should fall back to 5000)");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static void Test_ReadPort_FileMissing()
    {
        Console.WriteLine("\n[Scenario J] ReadPortFromAppsettings — appsettings.json missing → returns 0");
        Console.WriteLine("  Simulates: --webapp-path points to a directory without appsettings.json");

        string dir = Path.Combine(Path.GetTempPath(), "DashTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Do NOT create appsettings.json
            int port = JsonHelpers.ReadPortFromAppsettings(dir);

            Assert("ReadPortFromAppsettings returns 0 when file missing",
                port == 0,
                $"Returned: {port}  (expected 0 — file does not exist)");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static void Test_ReadPort_DirectRepair_PreservesNonDefault()
    {
        Console.WriteLine("\n[Scenario K] Full direct-MSI repair simulation: port 8080 preserved without --port flag");
        Console.WriteLine("  Verifies the complete Task #34 fix:");
        Console.WriteLine("    1. Install wrote port 8080 into appsettings.json");
        Console.WriteLine("    2. Repair re-lays fresh appsettings.json (no Urls)");
        Console.WriteLine("    3. WriteConfig runs without --port; reads existing port from pre-repair file");
        Console.WriteLine("  NOTE: Steps 1-2 are simulated here; step 3 uses ReadPortFromAppsettings.");

        string dir = Path.Combine(Path.GetTempPath(), "DashTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Simulate the state BEFORE the MSI re-lays the file: appsettings.json
            // has Urls=8080 from the original install's WriteConfig run.
            // (In a real repair, WriteConfig reads this BEFORE the file is re-laid,
            //  but for this unit test we just verify the reading works correctly.)
            string appsettings = Path.Combine(dir, "appsettings.json");
            File.WriteAllText(appsettings,
                "{\r\n  \"AllowedHosts\": \"*\",\r\n  \"Urls\": \"http://0.0.0.0:8080\"\r\n}\r\n",
                new UTF8Encoding(false));

            // ReadPortFromAppsettings is what WriteConfigAction calls when --port is absent.
            int preservedPort = JsonHelpers.ReadPortFromAppsettings(dir);

            Assert("Preserved port matches original install (8080, not 5000)",
                preservedPort == 8080,
                $"Returned: {preservedPort}  (expected 8080 — non-default port must survive direct-MSI repair)");

            // Simulate: MSI re-lays fresh appsettings.json (no Urls).
            File.WriteAllText(appsettings,
                "{\r\n  \"AllowedHosts\": \"*\"\r\n}\r\n",
                new UTF8Encoding(false));

            // WriteConfig re-applies the preserved port to the fresh file.
            string updated = JsonHelpers.SetJsonStringField(
                File.ReadAllText(appsettings, Encoding.UTF8),
                "Urls",
                $"http://0.0.0.0:{preservedPort}");
            File.WriteAllText(appsettings, updated, new UTF8Encoding(false));

            // Verify the result
            int finalPort = JsonHelpers.ReadPortFromAppsettings(dir);
            Assert("Port 8080 written back into re-laid appsettings.json",
                finalPort == 8080,
                $"Final port: {finalPort}  (expected 8080 — WriteConfig must not reset to 5000)");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Entry point
    // ──────────────────────────────────────────────────────────────────────────

    static int Main()
    {
        Console.WriteLine("=== SetJsonStringField / WriteConfig port preservation tests ===");
        Console.WriteLine("Verifies the REAL JsonHelpers code (source-linked from Dashboard.SetupHelper).");
        Console.WriteLine("Covers INSERT + REPLACE branches and direct-MSI repair port preservation.");

        Test_FreshFile_Insert_Port8080();
        Test_FreshFile_Insert_Port5000();
        Test_ExistingUrls_Replace_SamePort();
        Test_ExistingUrls_Replace_DifferentPort();
        Test_UpgradeScenario_TwoWriteConfigRuns();
        Test_RepairScenario();
        Test_InsertOnMinimalJson();
        Test_ReadPort_FromExistingAppsettings();
        Test_ReadPort_FreshFile_NoUrls();
        Test_ReadPort_FileMissing();
        Test_ReadPort_DirectRepair_PreservesNonDefault();

        Console.WriteLine($"\n  JsonHelpers: {_pass} passed, {_fail} failed");

        // Also run config contract tests (SetupHelper ↔ LFPortal.Web contract).
        int contractResult = ConfigContractTests.Run();

        int totalFail = _fail + (contractResult != 0 ? 1 : 0);
        Console.WriteLine($"\n=== Overall: {(totalFail == 0 ? "ALL PASSED" : $"{totalFail} SUITE(S) FAILED")} ===");
        return totalFail > 0 ? 1 : 0;
    }
}
