// WriteConfigAction.cs
// Writes configuration files to %ProgramData%\Dashboard\ during installation.
//
// Called by the MSI WriteConfig custom action as:
//   Dashboard.SetupHelper.exe --write-config
//       --url   <dashboard-url>
//       --lf-api <laserfiche-api-url>
//       --repo-id <repository-id>
//       --display-name <display-name>
//
// Both files are always written (overwriting any existing content).
// This is intentional: the wizard-entered values are the single source of truth.
// NeverOverwrite in Configuration.wxs places the initial template files;
// this action then writes the admin-specified values on top of them.
//
// Credentials are NEVER handled here (entered via Dashboard Settings page,
// encrypted with Windows DPAPI, stored in %ProgramData%\Dashboard\credentials\).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dashboard.SetupHelper
{
    internal static class WriteConfigAction
    {
        public static int Execute(Dictionary<string, string> opts)
        {
            string dashUrl     = Opt(opts, "url");
            string lfApiUrl    = Opt(opts, "lf-api");
            string repoId      = Opt(opts, "repo-id");
            string displayName = Opt(opts, "display-name");

            if (string.IsNullOrEmpty(displayName))
                displayName = repoId;

            // Resolve %ProgramData%\Dashboard\ without hard-coding C:\ProgramData
            string programData  = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            string dashboardDir = Path.Combine(programData, "Dashboard");
            Directory.CreateDirectory(dashboardDir);

            Console.WriteLine($"[SetupHelper] Config directory: {dashboardDir}");

            int rc = 0;

            // -- extension.config.json (Desktop Extension popup URL) ----------
            if (!string.IsNullOrEmpty(dashUrl))
            {
                string extPath = Path.Combine(dashboardDir, "extension.config.json");
                string extJson = BuildExtensionConfig(dashUrl);
                File.WriteAllText(extPath, extJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Console.WriteLine($"[SetupHelper] Wrote: {extPath}");
            }
            else
            {
                Console.WriteLine("[SetupHelper] Warning: --url not provided; extension.config.json not updated.");
            }

            // -- laserfiche.config.json (Web app connection settings) ----------
            if (!string.IsNullOrEmpty(lfApiUrl) || !string.IsNullOrEmpty(repoId))
            {
                string lfPath = Path.Combine(dashboardDir, "laserfiche.config.json");

                // Merge: preserve any existing fields the wizard did not change.
                string lfJson = BuildLaserficheConfig(
                    serverUrl:   lfApiUrl,
                    repoId:      repoId,
                    displayName: displayName,
                    existingPath: lfPath);

                File.WriteAllText(lfPath, lfJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Console.WriteLine($"[SetupHelper] Wrote: {lfPath}");
            }
            else
            {
                Console.WriteLine("[SetupHelper] Warning: no Laserfiche parameters provided; laserfiche.config.json not updated.");
            }

            return rc;
        }

        // ----------------------------------------------------------------
        // JSON builders -- no external dependencies, pure BCL.
        // ----------------------------------------------------------------

        private static string BuildExtensionConfig(string dashUrl)
        {
            return "{\r\n" +
                   $"  \"portalUrl\": \"{EscJson(dashUrl.TrimEnd('/'))}\",\r\n" +
                   "  \"buttonLabel\": \"Dashboard\",\r\n" +
                   "  \"iconPath\": \"\"\r\n" +
                   "}\r\n";
        }

        private static string BuildLaserficheConfig(
            string serverUrl,
            string repoId,
            string displayName,
            string existingPath)
        {
            // Load existing values so we do not lose fields not provided by the wizard.
            string existingServerUrl  = "https://YOUR-LF-SERVER/LFRepositoryAPI";
            string existingRepoId     = "YourRepositoryId";
            string existingDisplay    = "Your Repository";
            string existingApiBase    = "/LFRepositoryAPI";
            string existingApiVersion = "v1";
            int    existingTimeout    = 30;

            if (File.Exists(existingPath))
            {
                try { ParseExistingLFConfig(existingPath, ref existingServerUrl, ref existingRepoId, ref existingDisplay, ref existingApiBase, ref existingApiVersion, ref existingTimeout); }
                catch { /* parse failed -- use defaults */ }
            }

            // Wizard-provided values always win over existing.
            if (!string.IsNullOrEmpty(serverUrl))  existingServerUrl = serverUrl;
            if (!string.IsNullOrEmpty(repoId))      existingRepoId   = repoId;
            if (!string.IsNullOrEmpty(displayName)) existingDisplay  = displayName;

            return "{\r\n" +
                   "  \"Laserfiche\": {\r\n" +
                   $"    \"ServerUrl\": \"{EscJson(existingServerUrl)}\",\r\n" +
                   $"    \"RepositoryId\": \"{EscJson(existingRepoId)}\",\r\n" +
                   $"    \"DisplayName\": \"{EscJson(existingDisplay)}\",\r\n" +
                   $"    \"ApiBasePath\": \"{EscJson(existingApiBase)}\",\r\n" +
                   $"    \"ApiVersion\": \"{EscJson(existingApiVersion)}\",\r\n" +
                   $"    \"TimeoutSeconds\": {existingTimeout},\r\n" +
                   "    \"CredentialProvider\": \"DPAPI\"\r\n" +
                   "  }\r\n" +
                   "}\r\n";
        }

        // Minimal JSON field extractor for the Laserfiche config file.
        // Uses simple string scanning -- avoids any JSON library dependency.
        private static void ParseExistingLFConfig(
            string path,
            ref string serverUrl,
            ref string repoId,
            ref string displayName,
            ref string apiBase,
            ref string apiVersion,
            ref int timeout)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            serverUrl   = ExtractJsonString(text, "ServerUrl")   ?? serverUrl;
            repoId      = ExtractJsonString(text, "RepositoryId") ?? repoId;
            displayName = ExtractJsonString(text, "DisplayName")  ?? displayName;
            apiBase     = ExtractJsonString(text, "ApiBasePath")  ?? apiBase;
            apiVersion  = ExtractJsonString(text, "ApiVersion")   ?? apiVersion;
            string? ts  = ExtractJsonString(text, "TimeoutSeconds");
            if (ts != null && int.TryParse(ts, out int t)) timeout = t;
        }

        // Extracts the value of a JSON string or number field by name.
        // Returns null if the field is not found.
        private static string? ExtractJsonString(string json, string fieldName)
        {
            string search = $"\"{fieldName}\":";
            int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int valueStart = idx + search.Length;
            // skip whitespace
            while (valueStart < json.Length && json[valueStart] == ' ') valueStart++;
            if (valueStart >= json.Length) return null;
            if (json[valueStart] == '"')
            {
                // String value
                int end = json.IndexOf('"', valueStart + 1);
                if (end < 0) return null;
                return json.Substring(valueStart + 1, end - valueStart - 1)
                           .Replace("\\\"", "\"")
                           .Replace("\\\\", "\\")
                           .Replace("\\/", "/");
            }
            else
            {
                // Numeric value: read until comma, newline, or }
                int end = valueStart;
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\r' && json[end] != '\n')
                    end++;
                return json.Substring(valueStart, end - valueStart).Trim();
            }
        }

        // Dictionary helper: net48-compatible alternative to GetValueOrDefault
        // (that method was added in .NET Core 2.0 / .NET Standard 2.1 only).
        private static string Opt(Dictionary<string, string> d, string key, string def = "")
        {
            string v;
            return d.TryGetValue(key, out v) ? v : def;
        }

        // JSON string escaping (no external dependencies).
        private static string EscJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
        }
    }
}
