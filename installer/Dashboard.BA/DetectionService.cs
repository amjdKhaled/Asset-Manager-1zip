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

            DetectLaserficheApi(r);

            return r;
        }

        // ------------------------------------------------- Laserfiche API URL
        //
        // Determines a CERTIFICATE-VALID URL for the LFRepositoryAPI IIS
        // application instead of blindly defaulting to https://localhost/...:
        //   1. Find the IIS site containing an application with path
        //      "/LFRepositoryAPI" (applicationHost.config).
        //   2. Read its HTTPS binding (host + port).
        //   3. Resolve the binding's certificate (netsh http show sslcert ->
        //      LocalMachine cert stores) and extract SAN DNS names + validity.
        //   4. Validate the chain in LocalMachine context.
        //   5. Let ApiHostSelector pick binding-host > FQDN > machine name >
        //      localhost, requiring an explicit certificate match for each.
        private static void DetectLaserficheApi(DetectionResult r)
        {
            try
            {
                string bindingIp, bindingHost, port;
                if (!TryFindApiHttpsBinding(out bindingIp, out bindingHost, out port))
                {
                    r.LaserficheApiWarning =
                        "No HTTPS binding was found for the IIS application /LFRepositoryAPI. " +
                        "Enter the Laserfiche API URL manually.";
                    return;
                }

                var certInfo = TryLoadBindingCertificate(bindingIp, bindingHost, port);

                string machineName = Environment.MachineName;
                string fqdn = "";
                try
                {
                    var ip = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                    if (!string.IsNullOrEmpty(ip.DomainName))
                        fqdn = ip.HostName + "." + ip.DomainName;
                }
                catch { /* no domain */ }

                var sel = ApiHostSelector.SelectHost(
                    bindingHost, fqdn, machineName, certInfo, DateTime.UtcNow);

                r.LaserficheApiWarning = sel.Warning;
                if (!string.IsNullOrEmpty(sel.Host))
                {
                    string portSuffix = (port == "443") ? "" : ":" + port;
                    r.LaserficheApiUrl = "https://" + sel.Host + portSuffix + "/LFRepositoryAPI";
                }
                StartupLogger.Log(
                    $"LF API detection: bindingHost='{bindingHost}' port={port} " +
                    $"selectedHost='{sel.Host ?? "(none)"}' warning='{sel.Warning}'");
            }
            catch (Exception ex)
            {
                r.LaserficheApiWarning =
                    "Automatic Laserfiche API detection failed: " + ex.Message;
            }
        }

        // Finds the HTTPS binding (host, port) of the IIS site that contains
        // an application with path "/LFRepositoryAPI".
        private static bool TryFindApiHttpsBinding(out string ip, out string host, out string port)
        {
            ip   = "";
            host = "";
            port = "";
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "system32", "inetsrv", "config", "applicationHost.config");
                if (!File.Exists(configPath)) return false;

                var doc = new System.Xml.XmlDocument();
                doc.Load(configPath);

                var sites = doc.SelectNodes("//sites/site");
                if (sites == null) return false;

                foreach (System.Xml.XmlNode site in sites)
                {
                    bool hasApi = false;
                    foreach (System.Xml.XmlNode app in site.SelectNodes("application") ?? EmptyNodeList())
                    {
                        var p = app.Attributes?["path"]?.Value ?? "";
                        if (string.Equals(p, "/LFRepositoryAPI", StringComparison.OrdinalIgnoreCase))
                        {
                            hasApi = true;
                            break;
                        }
                    }
                    if (!hasApi) continue;

                    foreach (System.Xml.XmlNode b in site.SelectNodes("bindings/binding") ?? EmptyNodeList())
                    {
                        var protocol = b.Attributes?["protocol"]?.Value ?? "";
                        if (!string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // bindingInformation: "ip:port:host"
                        var info = b.Attributes?["bindingInformation"]?.Value ?? "";
                        var parts = info.Split(':');
                        if (parts.Length >= 2)
                        {
                            ip   = parts[0];
                            port = parts[1];
                            host = parts.Length >= 3 ? parts[2] : "";
                            return true;
                        }
                    }
                    return false; // site found but no https binding
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static System.Xml.XmlNodeList EmptyNodeList()
        {
            var d = new System.Xml.XmlDocument();
            d.LoadXml("<x/>");
            return d.DocumentElement!.ChildNodes;
        }

        // Resolves the certificate bound to the given HTTPS endpoint via
        // "netsh http show sslcert" and the LocalMachine cert stores, and
        // extracts SAN DNS names / validity / chain trust.
        //
        // Endpoint matching is EXACT first (hostname:port for SNI bindings,
        // ip:port otherwise) so multi-binding/SNI hosts resolve the right
        // certificate; a port-only match is used only as a fallback when no
        // exact endpoint matched (e.g. binding registered on 0.0.0.0 while
        // applicationHost.config shows *).
        private static ApiCertificateInfo? TryLoadBindingCertificate(
            string bindingIp, string bindingHost, string port)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = "netsh",
                    Arguments              = "http show sslcert",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true
                };

                // endpoint (lower-case) -> certificate hash
                var blocks = new List<KeyValuePair<string, string>>();
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    if (proc == null) return null;
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(15000);

                    string currentEndpoint = "";
                    foreach (var rawLine in output.Split('\n'))
                    {
                        var line = rawLine.Trim();
                        // Block header lines look like "IP:port : 0.0.0.0:443"
                        // or "Hostname:port : host:443" depending on binding type.
                        if (line.IndexOf(":port", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var idx = line.IndexOf(':', line.IndexOf(":port", StringComparison.OrdinalIgnoreCase) + 5);
                            currentEndpoint = idx > 0
                                ? line.Substring(idx + 1).Trim().ToLowerInvariant()
                                : "";
                            continue;
                        }
                        if (currentEndpoint.Length > 0 &&
                            line.StartsWith("Certificate Hash", StringComparison.OrdinalIgnoreCase))
                        {
                            var idx = line.IndexOf(':');
                            if (idx > 0)
                            {
                                blocks.Add(new KeyValuePair<string, string>(
                                    currentEndpoint,
                                    line.Substring(idx + 1).Trim().ToUpperInvariant()));
                            }
                            currentEndpoint = "";
                        }
                    }
                }

                // Exact endpoint candidates, most specific first.
                var candidates = new List<string>();
                if (!string.IsNullOrEmpty(bindingHost))
                    candidates.Add((bindingHost + ":" + port).ToLowerInvariant());
                if (!string.IsNullOrEmpty(bindingIp) && bindingIp != "*")
                    candidates.Add((bindingIp + ":" + port).ToLowerInvariant());
                candidates.Add("0.0.0.0:" + port);
                candidates.Add("[::]:" + port);

                string thumb = "";
                foreach (var c in candidates)
                {
                    foreach (var b in blocks)
                    {
                        if (b.Key == c) { thumb = b.Value; break; }
                    }
                    if (thumb.Length > 0) break;
                }
                if (thumb.Length == 0)
                {
                    // Fallback: any endpoint on this port.
                    foreach (var b in blocks)
                    {
                        if (b.Key.EndsWith(":" + port, StringComparison.Ordinal))
                        {
                            thumb = b.Value;
                            break;
                        }
                    }
                }
                if (thumb.Length == 0) return null;

                var cert = FindCertByThumbprint(thumb);
                if (cert == null) return null;

                var info = new ApiCertificateInfo
                {
                    NotBeforeUtc = cert.NotBefore.ToUniversalTime(),
                    NotAfterUtc  = cert.NotAfter.ToUniversalTime()
                };

                // SAN DNS names (OID 2.5.29.17); fall back to CN.
                foreach (var ext in cert.Extensions)
                {
                    if (ext.Oid?.Value != "2.5.29.17") continue;
                    foreach (var rawLine in ext.Format(true).Split('\n'))
                    {
                        var line = rawLine.Trim();
                        var marker = "DNS Name=";
                        var pos = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                        if (pos >= 0)
                        {
                            var name = line.Substring(pos + marker.Length).Trim().TrimEnd(',');
                            if (name.Length > 0) info.DnsNames.Add(name);
                        }
                    }
                }
                if (info.DnsNames.Count == 0)
                {
                    var cn = cert.GetNameInfo(
                        System.Security.Cryptography.X509Certificates.X509NameType.DnsName, false);
                    if (!string.IsNullOrEmpty(cn)) info.DnsNames.Add(cn);
                }

                // Chain trust in MACHINE context -- the Dashboard runs under
                // an IIS app-pool identity, so CurrentUser trust is irrelevant.
                // (Revocation skipped: offline servers must not fail detection
                // on CRL access.)
                using (var chain = new System.Security.Cryptography.X509Certificates.X509Chain(useMachineContext: true))
                {
                    chain.ChainPolicy.RevocationMode =
                        System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
                    info.ChainTrusted = chain.Build(cert);
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        private static System.Security.Cryptography.X509Certificates.X509Certificate2?
            FindCertByThumbprint(string thumbprint)
        {
            foreach (var storeName in new[] { "My", "WebHosting" })
            {
                try
                {
                    using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                        storeName,
                        System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
                    store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                    foreach (var c in store.Certificates)
                    {
                        if (string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                            return c;
                    }
                }
                catch { /* try next store */ }
            }
            return null;
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
