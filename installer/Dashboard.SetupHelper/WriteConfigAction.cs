// WriteConfigAction.cs
// Writes configuration files to %ProgramData%\Dashboard\ during installation.
//
// Called by the MSI WriteConfig custom action as:
//   Dashboard.SetupHelper.exe --write-config
//       --url          <dashboard-url>
//       --lf-api       <laserfiche-api-url>
//       --repo-id      <repository-id>       (LEGACY, optional; ignored by new MSI)
//       --display-name <display-name>        (LEGACY, optional; ignored by new MSI)
//       --port         <tcp-port>          (optional; default 5000)
//       --webapp-path  <path-to-webappfolder>  (optional; required to write Urls)
//
// Both ProgramData files are always written (overwriting any existing content).
// This is intentional: the wizard-entered values are the single source of truth.
// NeverOverwrite in Configuration.wxs places the initial template files;
// this action then writes the admin-specified values on top of them.
//
// When --webapp-path is supplied the action also patches the "Urls" key in
// <webappfolder>\appsettings.json so the ASP.NET Core app binds to the
// correct port when started outside IIS (e.g. via dotnet run or a service
// wrapper).  Under IIS/ANCM the Urls value is overridden by IIS and is
// therefore harmless but useful as a human-readable record of the chosen port.
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
            string portStr     = Opt(opts, "port", "5000");
            // SanitizeDir: strips stray '"' characters produced by the MSI
            // trailing-backslash-quote (\") escaping bug and any other invalid
            // path characters.  Without this, Path.Combine on net48 throws
            // "Illegal characters in path." and the install rolls back (1722).
            string webAppPath  = PathUtil.SanitizeDir(Opt(opts, "webapp-path"));
            // Optional override of the config directory (used by the build
            // smoke test so it never touches the real %ProgramData%).
            string configDirOverride = PathUtil.SanitizeDir(Opt(opts, "config-dir"));

            SetupLog.Info($"WriteConfig: url='{dashUrl}' lf-api='{lfApiUrl}' repo-id='{repoId}' " +
                          $"display-name='{(string.IsNullOrEmpty(displayName) ? "<EMPTY>" : displayName)}' " +
                          $"port='{portStr}' webapp-path='{webAppPath}'");

            // --repo-id / --display-name are LEGACY arguments kept only so old
            // command lines (repairs of previous MSIs) do not fail.  The
            // repository is runtime session context, never install config.
            // When absent, any RepositoryId already present in an existing
            // laserfiche.config.json is preserved as a fallback default.

            // Validate port: must be a positive integer in the valid TCP range.
            int port;
            if (!int.TryParse(portStr, out port) || port < 1 || port > 65535)
            {
                Console.Error.WriteLine($"[SetupHelper] Warning: invalid --port value '{portStr}'; defaulting to 5000.");
                port = 5000;
            }

            // Resolve %ProgramData%\Dashboard\ without hard-coding C:\ProgramData
            string programData  = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            string dashboardDir = string.IsNullOrEmpty(configDirOverride)
                ? Path.Combine(programData, "Dashboard")
                : configDirOverride;
            Directory.CreateDirectory(dashboardDir);

            Console.WriteLine($"[SetupHelper] Config directory: {dashboardDir}");
            Console.WriteLine($"[SetupHelper] Port: {port}");

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
            if (!string.IsNullOrEmpty(lfApiUrl))
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

            // -- appsettings.json Urls (port binding for the ASP.NET Core app) -
            // Writes "Urls": "http://0.0.0.0:<port>" so the app binds the correct
            // port when started outside IIS (e.g. via dotnet run or a service
            // wrapper).  Under IIS/ANCM the value is overridden by IIS and is
            // therefore harmless but serves as a human-readable record of the
            // chosen port.
            if (!string.IsNullOrEmpty(webAppPath))
            {
                string appSettingsPath = Path.Combine(webAppPath, "appsettings.json");
                SetupLog.Info($"Resolved appsettings path: {appSettingsPath} (exists: {File.Exists(appSettingsPath)})");
                if (File.Exists(appSettingsPath))
                {
                    try
                    {
                        string updated = SetJsonStringField(
                            File.ReadAllText(appSettingsPath, Encoding.UTF8),
                            "Urls",
                            $"http://0.0.0.0:{port}");
                        File.WriteAllText(appSettingsPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        Console.WriteLine($"[SetupHelper] Patched Urls in: {appSettingsPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[SetupHelper] Warning: could not patch {appSettingsPath}: {ex.Message}");
                        // Non-fatal: IIS binding is the authoritative port source.
                    }
                }
                else
                {
                    // NON-FATAL: appsettings.json missing must NEVER fail the
                    // install.  Under IIS/ANCM the IIS binding (SetIisBindingPort
                    // appcmd CA) is the authoritative port source.  Create a
                    // minimal valid appsettings.json so the Urls record exists;
                    // if even that fails, log a warning and continue.
                    SetupLog.Warn($"{appSettingsPath} not found; creating a minimal appsettings.json.");
                    try
                    {
                        string minimal =
                            "{\r\n" +
                            $"  \"Urls\": \"http://0.0.0.0:{port}\"\r\n" +
                            "}\r\n";
                        File.WriteAllText(appSettingsPath, minimal, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        SetupLog.Info($"Created minimal appsettings.json: {appSettingsPath}");
                    }
                    catch (Exception ex)
                    {
                        SetupLog.Warn($"Could not create {appSettingsPath}: {ex.Message}. " +
                                      "IIS binding remains the authoritative port source; installation continues.");
                    }
                    // rc stays 0 -- installation is NOT rolled back.
                }
            }
            else
            {
                Console.WriteLine("[SetupHelper] Note: --webapp-path not provided; appsettings.json Urls not updated.");
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
            // RepositoryId/DisplayName default to EMPTY: the repository is chosen at
            // runtime (Desktop/Web Client launch context or the login page).  A
            // non-empty value only survives here if an admin set one previously.
            string existingServerUrl  = "https://YOUR-LF-SERVER/LFRepositoryAPI";
            string existingRepoId     = "";
            string existingDisplay    = "";
            string existingApiBase    = "/LFRepositoryAPI";
            string existingApiVersion = "v1";
            int    existingTimeout    = 30;

            if (File.Exists(existingPath))
            {
                try { ParseExistingLFConfig(existingPath, ref existingServerUrl, ref existingRepoId, ref existingDisplay, ref existingApiBase, ref existingApiVersion, ref existingTimeout); }
                catch { /* parse failed -- use defaults */ }
            }

            // Scrub legacy placeholder sentinels shipped by older template files.
            // They must never survive as a "configured" repository.
            if (string.Equals(existingRepoId, "YourRepositoryId", StringComparison.OrdinalIgnoreCase))
                existingRepoId = "";
            if (string.Equals(existingDisplay, "Your Repository", StringComparison.OrdinalIgnoreCase))
                existingDisplay = "";

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

        // Sets (or adds) a top-level JSON string field in a JSON document.
        // Uses simple string scanning — avoids any JSON library dependency.
        //
        // If the field already exists its value is replaced in-place.
        // If it does not exist it is inserted before the closing '}' of the
        // outermost object.
        //
        // Limitations: works correctly only for top-level string fields in a
        // well-formed JSON object.  The appsettings.json written by the SDK
        // publish always satisfies this constraint.
        private static string SetJsonStringField(string json, string fieldName, string value)
        {
            string escapedValue = EscJson(value);
            string fieldKey     = $"\"{fieldName}\"";

            int idx = json.IndexOf(fieldKey, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Field exists — find the colon, then the opening quote of the value,
                // then the closing quote, and replace just the value portion.
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
                return json; // malformed; leave unchanged

            // Determine whether a trailing comma is needed (i.e. there is
            // already at least one field in the object).
            string before = json.Substring(0, lastBrace).TrimEnd();
            string insert  = $",\r\n  {fieldKey}: \"{escapedValue}\"\r\n";
            // If the object is empty (just '{' + optional whitespace) use no comma.
            if (before.EndsWith("{"))
                insert = $"\r\n  {fieldKey}: \"{escapedValue}\"\r\n";

            return before + insert + json.Substring(lastBrace);
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
