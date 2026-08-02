// WebClientAction.cs
// Deploys the Dashboard button to Laserfiche Browse.aspx (and removes it on uninstall).
//
// Called by MSI ExeCommand custom actions:
//   --deploy-webclient  --url <url>  --path <web-client-path>
//   --remove-webclient  --path <web-client-path>
//   --rollback-webclient --path <web-client-path>
//
// SAFETY RULES (mirroring Deploy-WebClientButton.ps1):
//   1. Browse.aspx backup is created BEFORE any modification.
//   2. The script tag insertion is idempotent (never inserts twice).
//   3. Removal only removes the Dashboard tag -- never touches other content.
//   4. Rollback restores the most recent .bak file.
//   5. All operations log to stdout for the MSI log.
//
// SOURCE JS FILE:
//   The SetupHelper.exe is installed in EXTENSIONFOLDER (...\Extension\).
//   The web app is installed in WEBAPPFOLDER (...\WebApp\).
//   The relative path from Extension to WebApp:
//       ..\WebApp\wwwroot\js\lf-webclient-button.js

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Dashboard.SetupHelper
{
    internal static class WebClientAction
    {
        // The script tag inserted into Browse.aspx (must match Remove pattern).
        private const string ScriptTagFragment = "lf-dashboard-button.js";
        private const string ScriptTagLine     = "<script src=\"assets/custom/lf-dashboard-button.js\"></script>";
        private const string AnchorPattern     = "browse-custom.css";

        // ----------------------------------------------------------------
        // Deploy: copies JS, patches URL, inserts script tag.
        // ----------------------------------------------------------------
        public static int Deploy(Dictionary<string, string> opts)
        {
            // SanitizeDir strips stray '"' characters produced by MSI
            // trailing-backslash-quote escaping (see PathUtil.cs).
            string dashUrl = Opt(opts, "url").TrimEnd('/');
            string rawPath = Opt(opts, "path");
            string wcPath  = PathUtil.SanitizeDir(rawPath);

            SetupLog.Info("WebClient Deploy started.");
            SetupLog.Info($"Received --path: '{rawPath}'");
            SetupLog.Info($"Sanitized path: '{wcPath}'");
            SetupLog.Info($"DeployWebClient: url='{dashUrl}' path='{wcPath}'");

            if (string.IsNullOrEmpty(wcPath))
            {
                SetupLog.Warn("--path not provided; skipping Web Client deployment.");
                return 0;
            }

            string browseAspx = Path.Combine(wcPath, "Browse.aspx");
            SetupLog.Info($"Browse.aspx resolved path: {browseAspx} (exists: {File.Exists(browseAspx)})");
            if (!File.Exists(browseAspx))
            {
                SetupLog.Error($"Browse.aspx not found at: {browseAspx}");
                SetupLog.Info("Deploy exit code: 1");
                return 1;
            }

            // Step 1: Backup Browse.aspx (MUST be first -- rollback restores from this).
            string timestamp  = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupPath = browseAspx + ".bak-" + timestamp;
            File.Copy(browseAspx, backupPath, overwrite: true);
            SetupLog.Info($"Backup created: {backupPath}");

            // Step 2: Locate source JS (installed by the MSI alongside this EXE).
            string? jsSource = FindSourceJs();
            if (jsSource == null)
            {
                SetupLog.Error("lf-webclient-button.js not found relative to this EXE.");
                SetupLog.Info("Deploy exit code: 1");
                return 1;
            }

            // Step 3: Create assets/custom/ directory.
            string customDir = Path.Combine(wcPath, "assets", "custom");
            Directory.CreateDirectory(customDir);
            SetupLog.Info($"Ensured assets/custom directory: {customDir}");

            // Step 4: Copy and patch the JS.
            string jsDest    = Path.Combine(customDir, "lf-dashboard-button.js");
            string jsContent = File.ReadAllText(jsSource, Encoding.UTF8);

            if (!string.IsNullOrEmpty(dashUrl))
            {
                string patched = Regex.Replace(
                    jsContent,
                    @"(var DASHBOARD_BASE_URL\s*=\s*)'[^']*'",
                    m => m.Groups[1].Value + "'" + dashUrl + "'"
                );
                if (patched == jsContent)
                    SetupLog.Warn("DASHBOARD_BASE_URL pattern not found in JS; URL not patched.");
                else
                    jsContent = patched;
            }

            File.WriteAllText(jsDest, jsContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SetupLog.Info($"Deployed JS: {jsDest}");

            // Step 5: Insert script tag into Browse.aspx (idempotent).
            string browseContent = File.ReadAllText(browseAspx, Encoding.UTF8);
            if (browseContent.Contains(ScriptTagFragment))
            {
                int existingCount = CountOccurrences(browseContent, ScriptTagFragment);
                SetupLog.Info($"Script tag already present in Browse.aspx (count: {existingCount}); skipping insertion.");
                if (existingCount != 1)
                    SetupLog.Warn($"Unexpected pre-existing tag count {existingCount}; manual review recommended.");
                SetupLog.Info("Verification result: PASS (tag present, JS refreshed).");
                SetupLog.Info("Deploy exit code: 0");
                return 0;
            }

            string[] lines    = File.ReadAllLines(browseAspx);
            var newLines      = new List<string>();
            bool inserted     = false;

            foreach (string line in lines)
            {
                newLines.Add(line);
                if (!inserted && line.Contains(AnchorPattern))
                {
                    // Match indentation of anchor line
                    string trimmed = line.TrimStart();
                    string indent  = line.Substring(0, line.Length - trimmed.Length);
                    newLines.Add(indent + ScriptTagLine);
                    inserted = true;
                }
            }

            if (!inserted)
            {
                // Fallback: insert before </head>
                newLines = new List<string>();
                foreach (string line in lines)
                {
                    if (!inserted && line.TrimStart().StartsWith("</head>", StringComparison.OrdinalIgnoreCase))
                    {
                        newLines.Add("    " + ScriptTagLine);
                        inserted = true;
                    }
                    newLines.Add(line);
                }
                if (!inserted)
                    SetupLog.Warn("Could not find anchor or </head>; script tag NOT inserted.");
            }

            if (inserted)
            {
                File.WriteAllLines(browseAspx, newLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                SetupLog.Info($"Script tag inserted into: {browseAspx}");
            }

            // Step 6: Verify
            string finalContent = File.ReadAllText(browseAspx, Encoding.UTF8);
            int tagCount = CountOccurrences(finalContent, ScriptTagFragment);
            bool jsOk    = File.Exists(jsDest);
            SetupLog.Info($"Verification: script tag count in Browse.aspx = {tagCount} (expected 1); JS asset exists = {jsOk}.");
            if (tagCount == 0 || !jsOk)
            {
                SetupLog.Error("Post-deploy verification FAILED -- Dashboard button was not installed.");
                SetupLog.Info("Deploy exit code: 1");
                return 1;
            }
            if (tagCount != 1)
                SetupLog.Warn($"Unexpected script tag count {tagCount} in Browse.aspx; manual review recommended.");

            SetupLog.Info("Verification result: PASS.");
            SetupLog.Info("Deploy exit code: 0");
            return 0;
        }

        // ----------------------------------------------------------------
        // Remove: strips the Dashboard script tag from Browse.aspx.
        // ----------------------------------------------------------------
        public static int Remove(Dictionary<string, string> opts)
        {
            string wcPath = PathUtil.SanitizeDir(Opt(opts, "path"));

            SetupLog.Info($"RemoveWebClient: path='{wcPath}'");

            if (string.IsNullOrEmpty(wcPath))
            {
                SetupLog.Warn("--path not provided; skipping Web Client removal.");
                return 0;
            }

            string browseAspx = Path.Combine(wcPath, "Browse.aspx");
            if (!File.Exists(browseAspx))
            {
                SetupLog.Info($"Browse.aspx not found at: {browseAspx}; nothing to remove.");
                return 0;
            }

            string[] lines    = File.ReadAllLines(browseAspx);
            string[] newLines = lines
                .Where(l => !l.Contains(ScriptTagFragment))
                .ToArray();

            if (newLines.Length == lines.Length)
            {
                SetupLog.Info("Dashboard script tag not present in Browse.aspx; nothing to remove.");
                return 0;
            }

            // Backup before modification
            string timestamp  = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupPath = browseAspx + ".bak-" + timestamp;
            File.Copy(browseAspx, backupPath, overwrite: true);

            File.WriteAllLines(browseAspx, newLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SetupLog.Info($"Dashboard script tag removed from: {browseAspx}");
            SetupLog.Info($"Pre-removal backup: {backupPath}");

            // Clean up the deployed JS asset (marker-based removal only touches
            // Dashboard-owned artifacts; Browse.aspx content is otherwise untouched).
            try
            {
                string jsDeployed = Path.Combine(wcPath, "assets", "custom", "lf-dashboard-button.js");
                if (File.Exists(jsDeployed))
                {
                    File.Delete(jsDeployed);
                    SetupLog.Info($"Removed deployed JS: {jsDeployed}");
                }
            }
            catch (Exception ex)
            {
                SetupLog.Warn($"Could not remove deployed JS: {ex.Message}");
            }

            return 0;
        }

        // ----------------------------------------------------------------
        // Rollback: restores Browse.aspx from the most recent backup.
        // Called by the MSI RollbackWebClient rollback custom action if
        // installation fails after DeployWebClient ran.
        // ----------------------------------------------------------------
        public static int Rollback(Dictionary<string, string> opts)
        {
            string wcPath = PathUtil.SanitizeDir(Opt(opts, "path"));

            SetupLog.Info($"RollbackWebClient: path='{wcPath}'");

            if (string.IsNullOrEmpty(wcPath))
            {
                SetupLog.Warn("--path not provided; skipping rollback.");
                return 0;
            }

            string browseAspx = Path.Combine(wcPath, "Browse.aspx");
            if (!Directory.Exists(wcPath))
            {
                SetupLog.Warn("Web client directory not found; skipping rollback.");
                return 0;
            }

            string[] backups = Directory.GetFiles(wcPath, "Browse.aspx.bak-*")
                                        .OrderByDescending(f => f)
                                        .ToArray();

            if (backups.Length == 0)
            {
                SetupLog.Warn("No Browse.aspx backup found; rollback skipped.");
                return 0;
            }

            string latest = backups[0];
            File.Copy(latest, browseAspx, overwrite: true);
            SetupLog.Info($"Browse.aspx restored from: {latest}");

            return 0;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        // Dictionary helper: net48-compatible alternative to GetValueOrDefault
        // (that method was added in .NET Core 2.0 / .NET Standard 2.1 only).
        private static string Opt(Dictionary<string, string> d, string key, string def = "")
        {
            string v;
            return d.TryGetValue(key, out v) ? v : def;
        }

        // Locate lf-webclient-button.js relative to this EXE.
        // This EXE lives in EXTENSIONFOLDER; WebApp is at ..\WebApp\.
        private static string? FindSourceJs()
        {
            string exeDir      = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location) ?? "";
            string installRoot = Path.GetDirectoryName(exeDir) ?? "";

            // Standard layout: ..\WebApp\wwwroot\js\lf-webclient-button.js
            var candidates = new[]
            {
                Path.Combine(installRoot, "WebApp", "wwwroot", "js", "lf-webclient-button.js"),
                // Development repo layout (for testing outside the installer)
                Path.Combine(exeDir, "..", "..", "src", "LFPortal.Web", "wwwroot", "js", "lf-webclient-button.js")
            };

            foreach (string c in candidates)
            {
                string fullPath = Path.GetFullPath(c);
                if (File.Exists(fullPath))
                {
                    SetupLog.Info($"Found source JS: {fullPath}");
                    return fullPath;
                }
            }
            return null;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int idx   = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }
    }
}
