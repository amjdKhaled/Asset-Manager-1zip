// ApiHostSelector.cs
// Pure, dependency-free host-name selection for the Laserfiche Repository API URL.
//
// The installer must NOT default to https://localhost/LFRepositoryAPI blindly:
// the IIS certificate frequently does not contain "localhost", which makes the
// Dashboard's HttpClient fail TLS validation even though the API itself works.
//
// Selection preference (per requirements):
//   1. HTTPS binding host name, if configured and certificate-valid.
//   2. Machine FQDN, if present in the certificate SAN/CN.
//   3. Machine name, if present in the certificate SAN/CN.
//   4. localhost ONLY if the certificate explicitly covers localhost.
//
// This class is intentionally pure (no IIS, no cert stores, no I/O) so it can
// be unit-tested on any platform.  DetectionService gathers the inputs on
// Windows and calls SelectHost.
//
// NOTE: this file is compile-linked into LFPortal.Web.Tests so the tests run
// against THIS code, not a copy.  Keep it free of WinForms/WiX dependencies.

using System;
using System.Collections.Generic;

namespace Dashboard.BA
{
    /// <summary>Certificate facts needed for host selection (no X509 types).</summary>
    public sealed class ApiCertificateInfo
    {
        /// <summary>DNS identities: SAN DNS names, or the CN when no SAN exists.</summary>
        public IList<string> DnsNames { get; set; } = new List<string>();

        public DateTime NotBeforeUtc { get; set; }
        public DateTime NotAfterUtc { get; set; }

        /// <summary>True when the chain builds in LocalMachine context.</summary>
        public bool ChainTrusted { get; set; }
    }

    /// <summary>Result of host selection.</summary>
    public sealed class ApiHostSelection
    {
        /// <summary>The certificate-valid host to use, or null when none is valid.</summary>
        public string? Host { get; set; }

        /// <summary>Non-fatal warning to surface in the wizard (may be set even on success).</summary>
        public string Warning { get; set; } = "";
    }

    public static class ApiHostSelector
    {
        /// <summary>
        /// Picks the first candidate host name that the certificate covers.
        /// Never returns "localhost" unless the certificate explicitly covers it.
        /// </summary>
        /// <param name="bindingHost">Host from the IIS HTTPS binding ("" when the binding has no host).</param>
        /// <param name="machineFqdn">Machine FQDN ("" when the machine has no domain suffix).</param>
        /// <param name="machineName">NetBIOS machine name.</param>
        /// <param name="cert">Certificate facts; null when no certificate could be resolved.</param>
        /// <param name="nowUtc">Current UTC time (parameter for testability).</param>
        public static ApiHostSelection SelectHost(
            string bindingHost,
            string machineFqdn,
            string machineName,
            ApiCertificateInfo? cert,
            DateTime nowUtc)
        {
            if (cert == null)
            {
                return new ApiHostSelection
                {
                    Host = null,
                    Warning = "The HTTPS certificate for the Laserfiche API site could not be inspected. " +
                              "Verify the API URL manually."
                };
            }

            if (nowUtc < cert.NotBeforeUtc || nowUtc > cert.NotAfterUtc)
            {
                return new ApiHostSelection
                {
                    Host = null,
                    Warning = "The Laserfiche API HTTPS certificate is expired or not yet valid " +
                              $"(valid {cert.NotBeforeUtc:yyyy-MM-dd} to {cert.NotAfterUtc:yyyy-MM-dd} UTC). " +
                              "Renew the certificate before installing."
                };
            }

            // Preference order; skip empty candidates.  localhost is LAST and
            // still requires an explicit certificate match like every other host.
            var candidates = new[] { bindingHost, machineFqdn, machineName, "localhost" };
            string? chosen = null;
            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                if (Matches(c.Trim(), cert.DnsNames)) { chosen = c.Trim(); break; }
            }

            if (chosen == null)
            {
                return new ApiHostSelection
                {
                    Host = null,
                    Warning = "None of this machine's host names match the Laserfiche API HTTPS " +
                              "certificate (" + string.Join(", ", cert.DnsNames) + "). " +
                              "Enter the URL using a host name the certificate covers."
                };
            }

            var result = new ApiHostSelection { Host = chosen };
            if (!cert.ChainTrusted)
            {
                // Identity matches but the machine does not trust the chain.
                // Never weaken TLS for this: tell the administrator to fix trust.
                result.Warning =
                    "The Laserfiche HTTPS certificate is not trusted by the local machine. " +
                    "Install/trust the issuing certificate in LocalMachine (not CurrentUser) " +
                    "before continuing.";
            }
            return result;
        }

        /// <summary>Case-insensitive host match including single-label wildcards.</summary>
        internal static bool Matches(string host, IList<string> certNames)
        {
            foreach (var name in certNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var n = name.Trim();

                if (string.Equals(n, host, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Wildcard: *.domain.tld matches host.domain.tld but not a.b.domain.tld
                if (n.StartsWith("*.", StringComparison.Ordinal))
                {
                    var suffix = n.Substring(1); // ".domain.tld"
                    if (host.Length > suffix.Length &&
                        host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        var label = host.Substring(0, host.Length - suffix.Length);
                        if (label.IndexOf('.') < 0) return true;
                    }
                }
            }
            return false;
        }
    }
}
