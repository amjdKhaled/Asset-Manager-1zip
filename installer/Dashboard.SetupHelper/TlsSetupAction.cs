// TlsSetupAction.cs  --  Dashboard.SetupHelper
//
// Implements the "--prepare-tls" verb: the installer's certificate/TLS
// preparation stage, executed as an ELEVATED deferred MSI custom action
// BEFORE laserfiche.config.json is written.
//
// Flow:
//   1. Probe the Laserfiche API HTTPS endpoint and capture the certificate
//      IIS actually presents (discovery only; validation happens in step 2).
//   2. Build the certificate chain in MACHINE trust context (the Dashboard
//      app pool identity, e.g. NetworkService, uses LocalMachine trust).
//   3. Run the PURE TlsTrustPlanner decision (unit tested separately).
//   4. If, and only if, the planner allows it: install ONLY the PUBLIC
//      certificate into LocalMachine\Root (idempotent, no private key).
//   5. Rebuild the chain, recycle the Dashboard app pool, and run a final
//      HTTPS verification with FULL default validation (no callbacks).
//
// SECURITY:
//   - There is NO certificate-validation bypass in any production request
//     path. The single recording callback below exists only to RETRIEVE the
//     endpoint certificate for inspection; the connection is discarded and
//     trust is evaluated explicitly via X509Chain + TlsTrustPlanner.
//   - Private keys are never exported and never copied to Root.
//   - CA-issued leaf certificates are never added to Root.
//   - Uninstall never removes certificates from LocalMachine\Root: the
//     trust may be shared with other products; removing machine trust is
//     more dangerous than leaving a public certificate behind (documented).
//
// EXIT CODE: always 0. Trust preparation is best-effort by design -- the
// wizard already blocks interactively on hard TLS failures, and a headless
// related-bundle/repair execution must never roll back a whole installation
// because a certificate could not be prepared. Every outcome is logged to
// %ProgramData%\Dashboard\Logs\SetupHelper.log with the [TLS SETUP] prefix.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Dashboard.SetupHelper
{
    internal static class TlsSetupAction
    {
        private const string P = "[TLS SETUP] ";

        public static int Execute(Dictionary<string, string> opts)
        {
            string url;
            if (!opts.TryGetValue("lf-api", out url) || string.IsNullOrWhiteSpace(url))
            {
                SetupLog.Error(P + "Missing --lf-api argument; nothing to prepare.");
                return 0;
            }
            url = url.Trim().TrimEnd('/');

            string consentRaw;
            bool consented = opts.TryGetValue("trust-selfsigned", out consentRaw) &&
                             consentRaw.Trim() == "1";

            SetupLog.Info(P + "API URL: " + url);
            SetupLog.Info(P + "Operator consent to trust self-signed certificate: " + consented);

            try
            {
                return ExecuteCore(url, consented);
            }
            catch (Exception ex)
            {
                // Never fail the installation from trust preparation.
                SetupLog.Error(ex);
                SetupLog.Error(P + "Unexpected error during TLS preparation; continuing install.");
                return 0;
            }
        }

        private static int ExecuteCore(string url, bool consented)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                SetupLog.Error(P + "Invalid URL; skipping TLS preparation.");
                return 0;
            }
            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                SetupLog.Info(P + "URL is not HTTPS; no TLS preparation required.");
                return 0;
            }

            string host = uri.Host;
            int    port = uri.Port; // 443 when unspecified

            // ---- 1. TCP reachability -------------------------------------
            if (!TcpReachable(host, port))
            {
                SetupLog.Warn(P + $"TCP reachable: FALSE ({host}:{port}). " +
                              "Server may be down; TLS preparation skipped, " +
                              "final verification: FAIL (unreachable).");
                return 0;
            }
            SetupLog.Info(P + $"TCP reachable: TRUE ({host}:{port})");

            // ---- 2. Retrieve the certificate the endpoint presents --------
            var endpointCert = RetrieveEndpointCertificate(host, port);
            if (endpointCert == null)
            {
                SetupLog.Warn(P + "Could not retrieve the endpoint certificate; " +
                              "TLS preparation skipped.");
                VerifyHttps(url); // still log what the Dashboard will experience
                return 0;
            }

            var dnsSans = new List<string>();
            var ipSans  = new List<string>();
            ExtractSans(endpointCert, dnsSans, ipSans);

            bool selfSigned = string.Equals(endpointCert.Subject, endpointCert.Issuer,
                                            StringComparison.Ordinal);
            bool inMyStore  = ExistsInStore(StoreName.My, endpointCert.Thumbprint) ||
                              ExistsInStore("WebHosting", endpointCert.Thumbprint);

            SetupLog.Info(P + "Certificate: " + endpointCert.Subject);
            SetupLog.Info(P + "Issuer: " + endpointCert.Issuer);
            SetupLog.Info(P + "Thumbprint: " + endpointCert.Thumbprint);
            SetupLog.Info(P + "SAN DNS: " + (dnsSans.Count == 0 ? "(none)" : string.Join(", ", dnsSans)));
            SetupLog.Info(P + "SAN IP: "  + (ipSans.Count  == 0 ? "(none)" : string.Join(", ", ipSans)));
            SetupLog.Info(P + $"NotBefore: {endpointCert.NotBefore.ToUniversalTime():yyyy-MM-dd HH:mm} UTC");
            SetupLog.Info(P + $"NotAfter: {endpointCert.NotAfter.ToUniversalTime():yyyy-MM-dd HH:mm} UTC");
            SetupLog.Info(P + "SelfSigned: " + selfSigned);
            SetupLog.Info(P + "Present in LocalMachine My/WebHosting: " + inMyStore);

            // ---- 3. Chain state BEFORE any change -------------------------
            string chainBefore;
            List<string> chainFlagsBefore;
            bool   trustedBefore = BuildMachineChain(endpointCert, out chainBefore, out chainFlagsBefore);
            bool   rootPresent   = ExistsInStore(StoreName.Root, endpointCert.Thumbprint);
            SetupLog.Info(P + "LocalMachine Root present: " + rootPresent);
            SetupLog.Info(P + "Chain before: " + chainBefore);

            // ---- 4. Pure trust decision -----------------------------------
            var input = new TrustPlanInput
            {
                IsSelfSigned           = selfSigned,
                ChainTrusted           = trustedBefore,
                // Flag-based (not string-compare): the ONLY failure flag must
                // be UntrustedRoot for the safe self-signed trust case.
                ChainUntrustedRootOnly = chainFlagsBefore.Count == 1 &&
                                         chainFlagsBefore[0] == X509ChainStatusFlags.UntrustedRoot.ToString(),
                ChainStatusSummary     = chainBefore,
                NotBeforeUtc           = endpointCert.NotBefore.ToUniversalTime(),
                NotAfterUtc            = endpointCert.NotAfter.ToUniversalTime(),
                NowUtc                 = DateTime.UtcNow,
                RequestedHost          = host,
                DnsSans                = dnsSans,
                IpSans                 = ipSans,
                AlreadyInRootStore     = rootPresent,
                IsElevated             = IsElevated(),
                OperatorConsented      = consented,
                Issuer                 = endpointCert.Issuer
            };
            var plan = TlsTrustPlanner.Decide(input);
            SetupLog.Info(P + "Trust decision: " + plan.Action + " -- " + plan.Reason);

            // ---- 5. Execute the plan --------------------------------------
            if (plan.Action == TrustPlanAction.InstallPublicCertificateToRoot)
            {
                InstallPublicCertToRoot(endpointCert);

                string chainAfter;
                List<string> ignoredFlags;
                BuildMachineChain(endpointCert, out chainAfter, out ignoredFlags);
                SetupLog.Info(P + "LocalMachine Root present: " +
                              ExistsInStore(StoreName.Root, endpointCert.Thumbprint));
                SetupLog.Info(P + "Chain after: " + chainAfter);

                // .NET caches chain state per process: recycle the Dashboard
                // app pool so the web app picks up the new trust immediately.
                RecycleDashboardAppPool();
            }

            // ---- 6. Final verification (full default validation) ----------
            VerifyHttps(url);
            VerifyHttps(url + "/v1/Repositories");
            return 0;
        }

        // --------------------------------------------------------------------

        private static bool TcpReachable(string host, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var task = client.ConnectAsync(host, port);
                    return task.Wait(5000) && client.Connected;
                }
            }
            catch { return false; }
        }

        // Retrieves the certificate the endpoint presents.
        //
        // PROBE ONLY -- the recording callback below is NOT a validation
        // bypass: the connection is used solely to obtain the certificate
        // bytes and is closed immediately. All trust evaluation happens
        // explicitly afterwards (X509Chain machine context + TlsTrustPlanner),
        // and the final verification uses full default validation.
        private static X509Certificate2 RetrieveEndpointCertificate(string host, int port)
        {
            try
            {
                using (var client = new TcpClient(host, port))
                using (var ssl = new SslStream(
                    client.GetStream(),
                    leaveInnerStreamOpen: false,
                    userCertificateValidationCallback: (s, cert, chain, errs) =>
                    {
                        // Record-and-accept for RETRIEVAL only (see note above).
                        return true;
                    }))
                {
                    ssl.AuthenticateAsClient(host);
                    var remote = ssl.RemoteCertificate;
                    return remote == null ? null : new X509Certificate2(remote.Export(X509ContentType.Cert));
                }
            }
            catch (Exception ex)
            {
                SetupLog.Warn(P + "Certificate retrieval failed: " + ex.Message);
                return null;
            }
        }

        private static void ExtractSans(
            X509Certificate2 cert, List<string> dnsSans, List<string> ipSans)
        {
            foreach (var ext in cert.Extensions)
            {
                if (ext.Oid == null || ext.Oid.Value != "2.5.29.17") continue;
                foreach (var rawLine in ext.Format(true).Split('\n'))
                {
                    var line = rawLine.Trim();
                    AddMarked(line, "DNS Name=", dnsSans);
                    AddMarked(line, "IP Address=", ipSans);
                }
            }
            if (dnsSans.Count == 0)
            {
                var cn = cert.GetNameInfo(X509NameType.DnsName, false);
                if (!string.IsNullOrEmpty(cn)) dnsSans.Add(cn);
            }
        }

        private static void AddMarked(string line, string marker, List<string> target)
        {
            int pos = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (pos < 0) return;
            var value = line.Substring(pos + marker.Length).Trim().TrimEnd(',');
            if (value.Length > 0) target.Add(value);
        }

        // Builds the chain in MACHINE trust context (the app pool identity
        // uses LocalMachine trust, never the installing user's store).
        private static bool BuildMachineChain(
            X509Certificate2 cert, out string statusSummary, out List<string> statusFlags)
        {
            using (var chain = new X509Chain(useMachineContext: true))
            {
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                bool ok = chain.Build(cert);
                statusFlags = chain.ChainStatus
                    .Select(s => s.Status.ToString())
                    .Where(s => s != X509ChainStatusFlags.NoError.ToString())
                    .Distinct()
                    .ToList();
                statusSummary = ok
                    ? "Valid"
                    : (statusFlags.Count == 0 ? "Invalid" : string.Join(", ", statusFlags));
                return ok;
            }
        }

        private static bool ExistsInStore(StoreName storeName, string thumbprint)
        {
            using (var store = new X509Store(storeName, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates
                    .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                    .Count > 0;
            }
        }

        private static bool ExistsInStore(string storeName, string thumbprint)
        {
            try
            {
                using (var store = new X509Store(storeName, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadOnly);
                    return store.Certificates
                        .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                        .Count > 0;
                }
            }
            catch { return false; }
        }

        // Installs ONLY the public certificate (X509ContentType.Cert export
        // strips any private key) into LocalMachine\Root. Idempotent.
        private static void InstallPublicCertToRoot(X509Certificate2 cert)
        {
            using (var root = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                root.Open(OpenFlags.ReadWrite);
                var existing = root.Certificates
                    .Find(X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false);
                if (existing.Count > 0)
                {
                    SetupLog.Info(P + "Root already contains the thumbprint; skipping add.");
                    return;
                }
                SetupLog.Info(P + "Installing public certificate into LocalMachine\\Root.");
                var publicOnly = new X509Certificate2(cert.Export(X509ContentType.Cert));
                root.Add(publicOnly);
            }
        }

        private static bool IsElevated()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        private static void RecycleDashboardAppPool()
        {
            try
            {
                string appcmd = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "inetsrv", "appcmd.exe");
                if (!File.Exists(appcmd))
                {
                    SetupLog.Warn(P + "appcmd.exe not found; app pool not recycled.");
                    return;
                }
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = appcmd,
                    Arguments       = "recycle apppool /apppool.name:\"Dashboard\"",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    proc.WaitForExit(15000);
                    SetupLog.Info(P + "Dashboard app pool recycle requested (exit " +
                                  (proc.HasExited ? proc.ExitCode.ToString() : "timeout") + ").");
                }
            }
            catch (Exception ex)
            {
                SetupLog.Warn(P + "App pool recycle failed (non-fatal): " + ex.Message);
            }
        }

        // Final verification with the SAME Windows trust rules the Dashboard's
        // HttpClient uses: default validation, no callbacks, no bypass.
        // Any HTTP status (200/400/401/403/404) proves the TLS handshake and
        // certificate trust succeeded.
        private static void VerifyHttps(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method  = "GET";
                request.Timeout = 10000;
                try
                {
                    using (var resp = (HttpWebResponse)request.GetResponse())
                    {
                        SetupLog.Info(P + $"HTTPS verification: PASS ({url} -> HTTP {(int)resp.StatusCode})");
                    }
                }
                catch (WebException wex)
                {
                    if (wex.Response != null)
                    {
                        var code = (int)((HttpWebResponse)wex.Response).StatusCode;
                        SetupLog.Info(P + $"HTTPS verification: PASS ({url} -> HTTP {code}; " +
                                      "TLS handshake and certificate trust succeeded)");
                    }
                    else if (wex.Status == WebExceptionStatus.TrustFailure ||
                             wex.Status == WebExceptionStatus.SecureChannelFailure)
                    {
                        SetupLog.Error(P + $"HTTPS verification: FAIL ({url} -> {wex.Status}: {wex.Message})");
                    }
                    else
                    {
                        SetupLog.Warn(P + $"HTTPS verification: INCONCLUSIVE ({url} -> {wex.Status}: {wex.Message})");
                    }
                }
            }
            catch (Exception ex)
            {
                SetupLog.Warn(P + "HTTPS verification error: " + ex.Message);
            }
        }
    }
}
