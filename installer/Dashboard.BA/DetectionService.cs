// DetectionService.cs
// Scans the local machine for components needed by Dashboard.
// Runs on a BackgroundWorker thread; must not touch WinForms controls directly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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

            r.IisInstalled         = DetectIis();
            r.AspNetCore8Installed = DetectAspNetCore8(out r.AspNetCore8Version);
            r.WebView2Installed    = DetectWebView2(out r.WebView2Version);
            r.DesktopClientFound   = DetectDesktopClient(out r.DesktopClientPath);
            r.WebClientFound       = DetectWebClient(out r.WebClientPath);
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
            var candidates = new List<string>();

            // 1. Registry search (same logic as Deploy-WebClientButton.ps1)
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

            // 2. Known default paths
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
