using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dashboard.SetupHelper
{
    /// <summary>
    /// Writes the complete first-run configuration selected in the setup wizard.
    /// Credentials arrive only as a machine-DPAPI encrypted temporary package;
    /// plain text never enters MSI properties, command lines, JSON, or logs.
    /// </summary>
    internal static class WriteConfigAction
    {
        public static int Execute(Dictionary<string, string> opts)
        {
            string dashboardUrl = Opt(opts, "url");
            string fullApiUrl = Opt(opts, "lf-api");
            string serverUrl = Opt(opts, "server-url");
            string apiBasePath = Opt(opts, "api-base-path");
            string apiVersion = Opt(opts, "api-version");
            string repositoryId = Opt(opts, "repo-id");
            string displayName = Opt(opts, "display-name");
            string rootEntryId = Opt(opts, "root-entry-id");
            string timeoutSeconds = Opt(opts, "timeout-seconds");
            string credentialFile = PathUtil.SanitizeDir(Opt(opts, "credential-file"));
            string portText = Opt(opts, "port");
            string webAppPath = PathUtil.SanitizeDir(Opt(opts, "webapp-path"));
            string configDirOverride = PathUtil.SanitizeDir(Opt(opts, "config-dir"));

            SetupLog.Info(
                $"WriteConfig: dashboard='{dashboardUrl}' server='{serverUrl}' api-base='{apiBasePath}' " +
                $"api-version='{apiVersion}' repository='{repositoryId}' root='{rootEntryId}' " +
                $"timeout='{timeoutSeconds}' credential-package=" +
                (string.IsNullOrEmpty(credentialFile) ? "absent" : "present") +
                $" port='{portText}' webapp-path='{webAppPath}' config-dir='{configDirOverride}'");

            int port = ResolvePort(portText, webAppPath);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dashboardDir = string.IsNullOrEmpty(configDirOverride)
                ? Path.Combine(programData, "Dashboard")
                : configDirOverride;
            Directory.CreateDirectory(dashboardDir);

            Console.WriteLine("[SetupHelper] Config directory: " + dashboardDir);
            Console.WriteLine("[SetupHelper] Port: " + port);

            if (!string.IsNullOrEmpty(dashboardUrl))
            {
                string extensionPath = Path.Combine(dashboardDir, "extension.config.json");
                WriteUtf8Atomic(extensionPath, BuildExtensionConfig(dashboardUrl));
                Console.WriteLine("[SetupHelper] Wrote: " + extensionPath);
            }

            if (!string.IsNullOrEmpty(fullApiUrl) || !string.IsNullOrEmpty(serverUrl))
            {
                string installerPath = Path.Combine(dashboardDir, "laserfiche.config.json");
                string runtimePath = Path.Combine(dashboardDir, "laserfiche.runtime.json");
                string existingPath = File.Exists(runtimePath) ? runtimePath : installerPath;

                string json = BuildLaserficheConfig(
                    serverUrl,
                    fullApiUrl,
                    apiBasePath,
                    apiVersion,
                    repositoryId,
                    displayName,
                    rootEntryId,
                    timeoutSeconds,
                    dashboardUrl,
                    existingPath);

                // Runtime config is last in the application's configuration
                // order. Writing both files ensures installer choices replace
                // stale Settings-page overrides during upgrade. The Settings
                // page itself remains unchanged and can edit runtime config later.
                WriteUtf8Atomic(installerPath, json);
                WriteUtf8Atomic(runtimePath, json);
                Console.WriteLine("[SetupHelper] Wrote: " + installerPath);
                Console.WriteLine("[SetupHelper] Wrote: " + runtimePath);
            }
            else
            {
                Console.WriteLine("[SetupHelper] Warning: connection configuration was not updated.");
            }

            if (!string.IsNullOrEmpty(credentialFile))
                ImportCredentials(credentialFile, dashboardDir);

            PatchApplicationUrl(webAppPath, port);
            return 0;
        }

        private static int ResolvePort(string portText, string webAppPath)
        {
            int port;
            if (!string.IsNullOrEmpty(portText))
            {
                if (int.TryParse(portText, out port) && port >= 1 && port <= 65535)
                    return port;
                SetupLog.Warn("Invalid port '" + portText + "'; using 5000.");
                return 5000;
            }

            int existing = JsonHelpers.ReadPortFromAppsettings(webAppPath);
            if (existing > 0)
            {
                SetupLog.Info("Port omitted during direct MSI repair; preserved " + existing + ".");
                return existing;
            }
            return 5000;
        }

        private static void PatchApplicationUrl(string webAppPath, int port)
        {
            if (string.IsNullOrEmpty(webAppPath))
            {
                Console.WriteLine("[SetupHelper] Web application path not supplied; Urls was not patched.");
                return;
            }

            string appsettings = Path.Combine(webAppPath, "appsettings.json");
            try
            {
                if (File.Exists(appsettings))
                {
                    string updated = JsonHelpers.SetJsonStringField(
                        File.ReadAllText(appsettings, Encoding.UTF8),
                        "Urls",
                        "http://0.0.0.0:" + port);
                    WriteUtf8Atomic(appsettings, updated);
                }
                else
                {
                    WriteUtf8Atomic(appsettings,
                        "{\r\n  \"Urls\": \"http://0.0.0.0:" + port + "\"\r\n}\r\n");
                }
                Console.WriteLine("[SetupHelper] Patched: " + appsettings);
            }
            catch (Exception ex)
            {
                // IIS is authoritative. A standalone binding record is useful
                // but must not roll back an otherwise valid IIS installation.
                SetupLog.Warn("Could not patch appsettings.json Urls: " + ex.Message);
            }
        }

        private static string BuildExtensionConfig(string dashboardUrl)
        {
            return "{\r\n" +
                   "  \"portalUrl\": \"" + JsonHelpers.EscJson(dashboardUrl.TrimEnd('/')) + "\",\r\n" +
                   "  \"buttonLabel\": \"Dashboard\",\r\n" +
                   "  \"iconPath\": \"\"\r\n" +
                   "}\r\n";
        }

        internal static string BuildLaserficheConfig(
            string serverUrl,
            string fullApiUrl,
            string apiBasePath,
            string apiVersion,
            string repositoryId,
            string displayName,
            string rootEntryId,
            string timeoutSeconds,
            string dashboardUrl,
            string existingPath)
        {
            string existingServerUrl = "";
            string existingApiBase = "/LFRepositoryAPI";
            string existingApiVersion = "Auto";
            string existingRepository = "";
            string existingDisplay = "";
            string existingDashboardUrl = "";
            string authenticationMode = "RepositoryPassword";
            string lfdsBaseUrl = "";
            string ssoClientId = "LFDashboard";
            string ssoRedirectUri = "";
            int existingRoot = 1;
            int existingTimeout = 30;

            if (File.Exists(existingPath))
            {
                try
                {
                    string existing = File.ReadAllText(existingPath, Encoding.UTF8);
                    existingServerUrl = ReadString(existing, "ServerUrl", existingServerUrl);
                    existingApiBase = ReadString(existing, "ApiBasePath", existingApiBase);
                    existingApiVersion = ReadString(existing, "ApiVersion", existingApiVersion);
                    existingRepository = ReadString(existing, "RepositoryId", existingRepository);
                    existingDisplay = ReadString(existing, "DisplayName", existingDisplay);
                    existingDashboardUrl = ReadString(existing, "DashboardPublicBaseUrl", existingDashboardUrl);
                    authenticationMode = ReadString(existing, "AuthenticationMode", authenticationMode);
                    lfdsBaseUrl = ReadString(existing, "LfdsBaseUrl", lfdsBaseUrl);
                    ssoClientId = ReadString(existing, "ClientId", ssoClientId);
                    ssoRedirectUri = ReadString(existing, "RedirectUri", ssoRedirectUri);
                    existingRoot = ReadInt(existing, "RootEntryId", existingRoot, 1, int.MaxValue);
                    existingTimeout = ReadInt(existing, "TimeoutSeconds", existingTimeout, 5, 300);
                }
                catch
                {
                    // Invalid old JSON is replaced by the validated wizard model.
                }
            }

            if (string.IsNullOrWhiteSpace(serverUrl) && !string.IsNullOrWhiteSpace(fullApiUrl))
                SplitFullApiUrl(fullApiUrl, out serverUrl, out apiBasePath);

            string effectiveServer = FirstNonEmpty(serverUrl, existingServerUrl);
            string effectiveApiBase = FirstNonEmpty(apiBasePath, existingApiBase, "/LFRepositoryAPI");
            string effectiveVersion = FirstNonEmpty(apiVersion, existingApiVersion, "Auto");
            string effectiveRepository = FirstNonEmpty(repositoryId, existingRepository);
            string effectiveDisplay = FirstNonEmpty(displayName, existingDisplay, effectiveRepository);
            string effectiveDashboardUrl = FirstNonEmpty(dashboardUrl, existingDashboardUrl);
            int effectiveRoot = ParseIntOrDefault(rootEntryId, existingRoot, 1, int.MaxValue);
            int effectiveTimeout = ParseIntOrDefault(timeoutSeconds, existingTimeout, 5, 300);

            return "{\r\n" +
                   "  \"Laserfiche\": {\r\n" +
                   "    \"ServerUrl\": \"" + JsonHelpers.EscJson(effectiveServer.TrimEnd('/')) + "\",\r\n" +
                   "    \"AuthenticationMode\": \"" + JsonHelpers.EscJson(authenticationMode) + "\",\r\n" +
                   "    \"DashboardPublicBaseUrl\": \"" + JsonHelpers.EscJson(effectiveDashboardUrl.TrimEnd('/')) + "\",\r\n" +
                   "    \"RepositoryId\": \"" + JsonHelpers.EscJson(effectiveRepository) + "\",\r\n" +
                   "    \"DisplayName\": \"" + JsonHelpers.EscJson(effectiveDisplay) + "\",\r\n" +
                   "    \"ApiBasePath\": \"/" + JsonHelpers.EscJson(effectiveApiBase.Trim('/')) + "\",\r\n" +
                   "    \"ApiVersion\": \"" + JsonHelpers.EscJson(effectiveVersion) + "\",\r\n" +
                   "    \"DetectedApiVersion\": \"\",\r\n" +
                   "    \"RootEntryId\": " + effectiveRoot + ",\r\n" +
                   "    \"TimeoutSeconds\": " + effectiveTimeout + ",\r\n" +
                   "    \"CredentialProvider\": \"DPAPI\",\r\n" +
                   "    \"Sso\": {\r\n" +
                   "      \"LfdsBaseUrl\": \"" + JsonHelpers.EscJson(lfdsBaseUrl) + "\",\r\n" +
                   "      \"ClientId\": \"" + JsonHelpers.EscJson(ssoClientId) + "\",\r\n" +
                   "      \"RedirectUri\": \"" + JsonHelpers.EscJson(ssoRedirectUri) + "\"\r\n" +
                   "    }\r\n" +
                   "  }\r\n" +
                   "}\r\n";
        }

        private static void ImportCredentials(string sourcePath, string dashboardDir)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("The encrypted credential package was not found.", sourcePath);

            byte[] encrypted = File.ReadAllBytes(sourcePath);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            try
            {
                string payload = Encoding.UTF8.GetString(plain);
                int separator = payload.IndexOf('\n');
                // Empty passwords are valid for some Laserfiche repository
                // accounts; only the username and separator are mandatory.
                if (separator <= 0)
                    throw new InvalidDataException("The encrypted credential package is invalid.");

                string credentialDirectory = Path.Combine(dashboardDir, "credentials");
                Directory.CreateDirectory(credentialDirectory);
                string destination = Path.Combine(credentialDirectory, HashFilename("default"));
                string temporaryDestination = destination + ".new";
                File.WriteAllBytes(temporaryDestination, encrypted);
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporaryDestination, destination);
                File.Delete(sourcePath);
                try
                {
                    string sourceDirectory = Path.GetDirectoryName(sourcePath);
                    if (!string.IsNullOrWhiteSpace(sourceDirectory)) Directory.Delete(sourceDirectory, false);
                }
                catch { }
                Console.WriteLine("[SetupHelper] Imported DPAPI-protected service credentials.");
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        private static string HashFilename(string repositoryKey)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(repositoryKey));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) hex.Append(value.ToString("x2"));
                return hex + ".dpapi";
            }
        }

        private static void WriteUtf8Atomic(string path, string content)
        {
            string temporary = path + ".new";
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        private static void SplitFullApiUrl(string fullApiUrl, out string serverUrl, out string apiBasePath)
        {
            serverUrl = fullApiUrl.TrimEnd('/');
            apiBasePath = "/LFRepositoryAPI";
            Uri parsed;
            if (!Uri.TryCreate(fullApiUrl, UriKind.Absolute, out parsed)) return;
            string path = parsed.AbsolutePath.TrimEnd('/');
            int marker = path.IndexOf("/LFRepositoryAPI", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return;
            serverUrl = parsed.GetLeftPart(UriPartial.Authority) + path.Substring(0, marker);
            apiBasePath = path.Substring(marker);
        }

        private static string ReadString(string json, string field, string fallback)
        {
            string value = JsonHelpers.ExtractJsonString(json, field);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static int ReadInt(string json, string field, int fallback, int min, int max)
        {
            return ParseIntOrDefault(JsonHelpers.ExtractJsonString(json, field), fallback, min, max);
        }

        private static int ParseIntOrDefault(string value, int fallback, int min, int max)
        {
            int result;
            return int.TryParse(value, out result) && result >= min && result <= max ? result : fallback;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }

        private static string Opt(Dictionary<string, string> values, string key, string fallback = "")
        {
            string value;
            return values.TryGetValue(key, out value) ? value : fallback;
        }
    }
}
