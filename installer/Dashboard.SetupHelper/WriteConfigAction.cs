// WriteConfigAction.cs
// Writes configuration files to %ProgramData%\Dashboard\ during installation.
//
// Called by the MSI WriteConfig custom action as:
//   Dashboard.SetupHelper.exe --write-config
//       --url          <dashboard-url>
//       --lf-api       <laserfiche-api-url>
//       --repo-id      <repository-id>       (LEGACY, optional; ignored by new MSI)
//       --display-name <display-name>        (LEGACY, optional; ignored by new MSI)
//       --port         <tcp-port>          (optional; omitted on direct-MSI repair)
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
// PORT PRESERVATION ON DIRECT-MSI REPAIR:
//   When the MSI is repaired or upgraded directly (msiexec /fa, not via the
//   Burn bundle), the DASHBOARD_PORT property has no default value — it is
//   intentionally left blank, exactly like LF_API_VERSION.  This means --port
//   is absent from the WriteConfig command line.  In that case this action
//   reads the port already written in appsettings.json and re-uses it, so a
//   non-default port (e.g. 8080) is never silently reset to 5000.
//   If appsettings.json has no Urls key (fresh file), the fallback is 5000.
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
            string apiVersion  = Opt(opts, "api-version");
            string repoId      = Opt(opts, "repo-id");
            string displayName = Opt(opts, "display-name");
            // INTENTIONALLY NO DEFAULT: when the property is empty (direct-MSI
            // repair / upgrade without the bundle UI), WriteConfig reads the
            // current port from appsettings.json and preserves it.  A non-default
            // port (e.g. 8080) is never silently reset to 5000.
            // Fresh installs with no existing appsettings.json default to 5000
            // inside the port-resolution block below.
            // This mirrors exactly how LF_API_VERSION is handled.
            string portStr     = Opt(opts, "port");
            // SanitizeDir: strips stray '"' characters produced by the MSI
            // trailing-backslash-quote (\") escaping bug and any other invalid
            // path characters.  Without this, Path.Combine on net48 throws
            // "Illegal characters in path." and the install rolls back (1722).
            string webAppPath  = PathUtil.SanitizeDir(Opt(opts, "webapp-path"));
            // Optional override of the config directory (used by the build
            // smoke test so it never touches the real %ProgramData%).
            string configDirOverride = PathUtil.SanitizeDir(Opt(opts, "config-dir"));

            SetupLog.Info($"WriteConfig: url='{dashUrl}' lf-api='{lfApiUrl}' api-version='{apiVersion}' " +
                          $"port='{portStr}' webapp-path='{webAppPath}' config-dir='{configDirOverride}'");

            // --repo-id / --display-name are LEGACY arguments kept only so old
            // command lines (repairs of previous MSIs) do not fail.  The
            // repository is runtime session context, never install config.
            // When absent, any RepositoryId already present in an existing
            // laserfiche.config.json is preserved as a fallback default.

            // Resolve the port:
            //   1. If --port was supplied and is valid, use it (new install / bundle repair).
            //   2. If --port was omitted or empty (direct-MSI repair), read the
            //      current port from appsettings.json so the existing setting is
            //      preserved rather than silently reset to 5000.
            //   3. Fall back to 5000 only when neither source has a usable value.
            int port;
            if (!string.IsNullOrEmpty(portStr))
            {
                if (!int.TryParse(portStr, out port) || port < 1 || port > 65535)
                {
                    Console.Error.WriteLine($"[SetupHelper] Warning: invalid --port value '{portStr}'; defaulting to 5000.");
                    port = 5000;
                }
            }
            else
            {
                // --port not supplied: preserve the port already in appsettings.json
                // (direct-MSI repair path — Burn persisted variables are not passed
                // when the MSI is invoked directly without the bundle wizard).
                int existing = JsonHelpers.ReadPortFromAppsettings(webAppPath);
                if (existing > 0)
                {
                    port = existing;
                    SetupLog.Info($"WriteConfig: --port not supplied; preserved existing port {port} from appsettings.json.");
                }
                else
                {
                    port = 5000;
                    SetupLog.Info("WriteConfig: --port not supplied and no existing Urls in appsettings.json; defaulting to 5000.");
                }
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

                // Merge: preserve ServerUrl/ApiBasePath/ApiVersion/Timeout from any
                // existing file.  RepositoryId and DisplayName are intentionally NOT
                // preserved — they are runtime session context, never install config.
                // Any legacy values in an existing file are actively dropped on write.
                string lfJson = BuildLaserficheConfig(
                    serverUrl:    lfApiUrl,
                    apiVersion:   apiVersion,
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
                        string updated = JsonHelpers.SetJsonStringField(
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
                   $"  \"portalUrl\": \"{JsonHelpers.EscJson(dashUrl.TrimEnd('/'))}\",\r\n" +
                   "  \"buttonLabel\": \"Dashboard\",\r\n" +
                   "  \"iconPath\": \"\"\r\n" +
                   "}\r\n";
        }

        private static string BuildLaserficheConfig(
            string serverUrl,
            string apiVersion,
            string existingPath)
        {
            // Load existing values so we do not lose fields not provided by the wizard.
            // RepositoryId and DisplayName are intentionally NOT loaded or written:
            // the repository is runtime session context (Desktop/Web Client launch URL
            // or login-page selection) and must never be frozen at install time.
            // Any legacy RepositoryId/DisplayName values in an existing config are
            // silently dropped when this method rewrites the file.
            // Fresh installs default to Auto (version auto-detection); a merge below
            // preserves any explicit version already in the existing file — so
            // upgrades of installs pinned to "v1" stay pinned (backward compatible).
            string existingServerUrl  = "https://YOUR-LF-SERVER/LFRepositoryAPI";
            string existingApiBase    = "/LFRepositoryAPI";
            string existingApiVersion = "Auto";
            int    existingTimeout    = 30;

            if (File.Exists(existingPath))
            {
                try { ParseExistingLFConfig(existingPath, ref existingServerUrl, ref existingApiBase, ref existingApiVersion, ref existingTimeout); }
                catch { /* parse failed -- use defaults */ }
            }

            // Wizard-provided values always win over existing.
            if (!string.IsNullOrEmpty(serverUrl))   existingServerUrl  = serverUrl;
            if (!string.IsNullOrEmpty(apiVersion))  existingApiVersion = apiVersion;

            return "{\r\n" +
                   "  \"Laserfiche\": {\r\n" +
                   $"    \"ServerUrl\": \"{JsonHelpers.EscJson(existingServerUrl)}\",\r\n" +
                   $"    \"ApiBasePath\": \"{JsonHelpers.EscJson(existingApiBase)}\",\r\n" +
                   $"    \"ApiVersion\": \"{JsonHelpers.EscJson(existingApiVersion)}\",\r\n" +
                   $"    \"TimeoutSeconds\": {existingTimeout},\r\n" +
                   "    \"CredentialProvider\": \"DPAPI\"\r\n" +
                   "  }\r\n" +
                   "}\r\n";
        }

        // Minimal JSON field extractor for the Laserfiche config file.
        // Uses simple string scanning -- avoids any JSON library dependency.
        // NOTE: RepositoryId and DisplayName are intentionally NOT read here.
        // They are runtime session state and must not be round-tripped through
        // the installer even when present in a legacy config file.
        private static void ParseExistingLFConfig(
            string path,
            ref string serverUrl,
            ref string apiBase,
            ref string apiVersion,
            ref int timeout)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            serverUrl  = JsonHelpers.ExtractJsonString(text, "ServerUrl")    ?? serverUrl;
            apiBase    = JsonHelpers.ExtractJsonString(text, "ApiBasePath")  ?? apiBase;
            apiVersion = JsonHelpers.ExtractJsonString(text, "ApiVersion")   ?? apiVersion;
            string? ts = JsonHelpers.ExtractJsonString(text, "TimeoutSeconds");
            if (ts != null && int.TryParse(ts, out int t)) timeout = t;
        }

        // Dictionary helper: net48-compatible alternative to GetValueOrDefault
        // (that method was added in .NET Core 2.0 / .NET Standard 2.1 only).
        private static string Opt(Dictionary<string, string> d, string key, string def = "")
        {
            string v;
            return d.TryGetValue(key, out v) ? v : def;
        }
    }
}
