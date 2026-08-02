// DetectionService.cs
// Scans the local machine for components needed by Dashboard.
// Runs on a BackgroundWorker thread; must not touch WinForms controls directly.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace Dashboard.BA
{
    internal static class DetectionService
    {
        // Entry point: runs all detections and returns a result object.
        // Safe to call from a background thread.
        public static DetectionResult Detect()
        {
            var r = new DetectionResult();

            // Properties cannot be passed as out parameters in C#; use locals then assign.
            string aspNetVersion = "";
            string webView2Ver   = "";
            string desktopPath   = "";
            string webClientPath = "";

            r.IisInstalled          = DetectIis();
            r.AspNetCore8Installed  = DetectAspNetCore8(out aspNetVersion);
            r.AspNetCore8Version    = aspNetVersion;
            r.WebView2Installed     = DetectWebView2(out webView2Ver);
            r.WebView2Version       = webView2Ver;
            r.DesktopClientFound    = DetectDesktopClient(out desktopPath);
            r.DesktopClientPath     = desktopPath;
            r.WebClientFound        = DetectWebClient(out webClientPath);
            r.WebClientPath         = webClientPath;
            r.SuggestedDashboardUrl = BuildSuggestedUrl();

            return r;
        }

        // ------------------------------------------------------------------ IIS
        private static bool DetectIis()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\W3SVC", writable: false);
                if (key == null) return false;
                // Start = 4 means Disabled
                var start = key.GetValue("Start");
                if (start is int s && s == 4) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------------------------------- ASP.NET Core 8
        private static bool DetectAspNetCore8(out string version)
        {
            version = "";
            try
            {
                // Same registry key the MSI uses in its LaunchCondition check.
                using var key = RegistryKey.OpenBaseKey(
                        RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(
                        @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost",
                        writable: false);
                if (key == null) return false;
                var v = key.GetValue("Version");
                if (v == null) return false;
                version = v.ToString() ?? "";
                return version.StartsWith("8.");
            }
            catch
            {
                return false;
            }
        }

        // ----------------------------------------------------------- WebView2
        private static bool DetectWebView2(out string version)
        {
            version = "";
            // System-wide WebView2 Runtime GUID
            const string ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

            // Check HKLM 32-bit (most common install location for WebView2 machine-level)
            var paths = new[]
            {
                $@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{ClientGuid}",
                $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{ClientGuid}"
            };

            foreach (var path in paths)
            {
                try
                {
                    using var key = RegistryKey.OpenBaseKey(
                            RegistryHive.LocalMachine, RegistryView.Registry64)
                        .OpenSubKey(path, writable: false);
                    if (key == null) continue;
                    var pv = key.GetValue("pv");
                    if (pv != null && pv.ToString() != "0.0.0.0")
                    {
                        version = pv.ToString() ?? "";
                        return true;
                    }
                }
                catch { /* continue */ }
            }

            // Also check HKCU (per-user install)
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{ClientGuid}", writable: false);
                if (key != null)
                {
                    var pv = key.GetValue("pv");
                    if (pv != null && pv.ToString() != "0.0.0.0")
                    {
                        version = pv.ToString() ?? "";
                        return true;
                    }
                }
            }
            catch { /* continue */ }

            return false;
        }

        // ---------------------------------------------- Laserfiche Desktop Client
        private static bool DetectDesktopClient(out string path)
        {
            path = "";
            var regPaths = new[]
            {
                @"SOFTWARE\Laserfiche\ClientAutomation",
                @"SOFTWARE\WOW6432Node\Laserfiche\ClientAutomation",
                @"SOFTWARE\Laserfiche\Desktop Client",
                @"SOFTWARE\WOW6432Node\Laserfiche\Desktop Client"
            };

            foreach (var regPath in regPaths)
            {
                try
                {
                    using var key = RegistryKey.OpenBaseKey(
                            RegistryHive.LocalMachine, RegistryView.Registry64)
                        .OpenSubKey(regPath, writable: false);
                    if (key == null) continue;
                    foreach (var valueName in new[] { "InstallPath", "Path", "Location", "ClientPath" })
                    {
                        var val = key.GetValue(valueName);
                        if (val is string s && s.Length > 0 && Directory.Exists(s))
                        {
                            path = s;
                            return true;
                        }
                    }
                    // Key exists but no path value -- still counts as installed
                    if (key.ValueCount > 0 || key.SubKeyCount > 0)
                        return true;
                }
                catch { /* continue */ }
            }

            // Fallback: known install paths
            var knownPaths = new[]
            {
                @"C:\Program Files\Laserfiche\Desktop Client",
                @"C:\Program Files (x86)\Laserfiche\Desktop Client",
                @"C:\Program Files\Laserfiche\LaserficheClient",
            };
            foreach (var p in knownPaths)
            {
                if (Directory.Exists(p)) { path = p; return true; }
            }

            return false;
        }

        // ---------------------------------------------- Laserfiche Web Client
        public static bool DetectWebClient(out string path)
        {
            path = "";

            // Priority 1: IIS applicationHost.config (covers non-default physical paths
            // such as IIS applications at /Laserfiche whose path is not in the registry).
            if (DetectWebClientViaIis(out path)) return true;

            var candidates = new List<string>();

            // Priority 2: Registry search (same logic as Deploy-WebClientButton.ps1)
            var regPaths = new[]
            {
                @"SOFTWARE\Laserfiche\WebAccess",
                @"SOFTWARE\WOW6432Node\Laserfiche\WebAccess",
                @"SOFTWARE\Laserfiche\WebAccess\10",
                @"SOFTWARE\Laserfiche\WebAccess\11",
                @"SOFTWARE\Laserfiche\WebAccess\12"
            };
            foreach (var rp in regPaths)
            {
                try
                {
                    using var key = RegistryKey.OpenBaseKey(
                            RegistryHive.LocalMachine, RegistryView.Registry64)
                        .OpenSubKey(rp, writable: false);
                    if (key == null) continue;
                    foreach (var vn in new[] { "WebFilesPath", "InstallPath", "Path", "WebPath", "Directory" })
                    {
                        var val = key.GetValue(vn);
                        if (val is string s && s.Length > 0)
                            candidates.Add(s.TrimEnd('\\'));
                    }
                }
                catch { /* continue */ }
            }

            // Priority 3: Known default paths
            candidates.AddRange(new[]
            {
                @"C:\Program Files\Laserfiche\Web Access\Web Files",
                @"C:\Program Files (x86)\Laserfiche\Web Access\Web Files",
                @"C:\Program Files\Laserfiche\Web Access",
                @"C:\Program Files (x86)\Laserfiche\Web Access",
                @"C:\Laserfiche\Web Access\Web Files",
                @"C:\Laserfiche\Web Files"
            });

            foreach (var c in candidates)
            {
                if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "Browse.aspx")))
                {
                    path = c;
                    return true;
                }
            }
            return false;
        }

        // Searches IIS configuration for a virtual directory that contains Browse.aspx.
        // Uses two strategies in order:
        //   1. Parse %SystemRoot%\system32\inetsrv\config\applicationHost.config directly
        //      (XML, no process spawn, works for any elevated or read-permitted caller).
        //   2. Fall back to running appcmd.exe list vdir (works when config file is
        //      access-restricted but the caller has IIS administration rights).
        private static bool DetectWebClientViaIis(out string path)
        {
            path = "";

            // Strategy 1: read applicationHost.config
            try
            {
                string systemRoot  = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                string configPath  = Path.Combine(systemRoot, "system32", "inetsrv", "config", "applicationHost.config");
                if (File.Exists(configPath))
                {
                    var doc = new XmlDocument();
                    doc.Load(configPath);
                    var nodes = doc.GetElementsByTagName("virtualDirectory");
                    foreach (XmlNode node in nodes)
                    {
                        string? raw = node.Attributes?["physicalPath"]?.Value;
                        if (string.IsNullOrEmpty(raw)) continue;
                        // Expand %SystemDrive% / %SystemRoot% environment tokens used
                        // in some IIS config paths (e.g. %SystemDrive%\inetpub\wwwroot).
                        string expanded = Environment.ExpandEnvironmentVariables(raw!);
                        string candidate = expanded.TrimEnd('\\');
                        if (!string.IsNullOrEmpty(candidate) &&
                            File.Exists(Path.Combine(candidate, "Browse.aspx")))
                        {
                            path = candidate;
                            return true;
                        }
                    }
                }
            }
            catch { /* fall through to appcmd */ }

            // Strategy 2: appcmd list vdir
            // Output lines look like: VDIR "Default Web Site/App/" (physicalPath:C:\...\App)
            try
            {
                string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                string appcmd = Path.Combine(systemRoot, "system32", "inetsrv", "appcmd.exe");
                if (!File.Exists(appcmd)) return false;

                var psi = new ProcessStartInfo
                {
                    FileName               = appcmd,
                    Arguments              = "list vdir",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(8000);

                var physPathRx = new Regex(@"\(physicalPath:([^)]+)\)", RegexOptions.IgnoreCase);
                foreach (Match m in physPathRx.Matches(output))
                {
                    string expanded  = Environment.ExpandEnvironmentVariables(m.Groups[1].Value.Trim());
                    string candidate = expanded.TrimEnd('\\');
                    if (!string.IsNullOrEmpty(candidate) &&
                        File.Exists(Path.Combine(candidate, "Browse.aspx")))
                    {
                        path = candidate;
                        return true;
                    }
                }
            }
            catch { /* detection unavailable */ }

            return false;
        }

        // ----------------------------------------------- Suggested URL
        private static string BuildSuggestedUrl()
        {
            try
            {
                string host = Dns.GetHostName();
                return $"http://{host}:5000";
            }
            catch
            {
                return "http://localhost:5000";
            }
        }
    }
}
