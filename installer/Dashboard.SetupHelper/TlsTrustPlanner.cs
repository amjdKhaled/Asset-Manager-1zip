// TlsTrustPlanner.cs  --  Dashboard.SetupHelper
//
// PURE decision logic for the installer's certificate trust preparation.
// This class deliberately contains NO Windows certificate-store access, no
// networking, and no WinForms so it can be compile-linked into the net8
// unit-test project and exercised on any OS.
//
// The rules implement the security policy for automatic trust:
//
//   ALLOW installing the PUBLIC certificate into LocalMachine\Root only when
//   ALL of the following hold:
//     1. the certificate is the one actually presented by the Laserfiche API
//        HTTPS endpoint (caller guarantees this by construction),
//     2. it is currently valid by date,
//     3. the requested host name matches the certificate identity (SAN DNS,
//        RFC-style single-label wildcards, or IP SAN for IP hosts),
//     4. it is SELF-SIGNED (Subject == Issuer),
//     5. the only chain failure is the missing self-signed trust
//        (UntrustedRoot), and
//     6. the process is elevated and the operator consented.
//
//   NEVER: add a CA-issued leaf to Root, bypass validation, or touch the
//   store when the chain already validates.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Dashboard.SetupHelper
{
    public sealed class TrustPlanInput
    {
        // Subject == Issuer on the endpoint certificate.
        public bool IsSelfSigned { get; set; }

        // Machine-context chain builds cleanly (already trusted).
        public bool ChainTrusted { get; set; }

        // The ONLY chain status flag is UntrustedRoot (classic self-signed
        // "not in LocalMachine\Root" failure).
        public bool ChainUntrustedRootOnly { get; set; }

        // Human-readable chain status summary, e.g. "UntrustedRoot" or
        // "PartialChain, RevocationStatusUnknown".
        public string ChainStatusSummary { get; set; } = "";

        public DateTime NotBeforeUtc { get; set; }
        public DateTime NotAfterUtc  { get; set; }
        public DateTime NowUtc       { get; set; }

        // Host component of the ServerUrl the Dashboard will use.
        public string RequestedHost { get; set; } = "";

        public IList<string> DnsSans { get; set; } = new List<string>();
        public IList<string> IpSans  { get; set; } = new List<string>();

        // Certificate (by thumbprint) already present in LocalMachine\Root.
        public bool AlreadyInRootStore { get; set; }

        // Process has administrative rights (deferred elevated CA => true).
        public bool IsElevated { get; set; }

        // Operator consent (wizard checkbox / --trust-selfsigned 1).
        public bool OperatorConsented { get; set; }

        // Certificate issuer, for CA-chain problem messages.
        public string Issuer { get; set; } = "";
    }

    public enum TrustPlanAction
    {
        // Do not touch the trust store (already trusted / already present /
        // operator declined).
        None = 0,

        // Install ONLY the public certificate into LocalMachine\Root.
        InstallPublicCertificateToRoot = 1,

        // CA-issued certificate with an untrusted chain: report the missing
        // root/intermediate; NEVER add the leaf to Root.
        ReportCaChainProblem = 2,

        // Automatic trust is denied (expired, identity mismatch, unexpected
        // chain failure, not elevated).
        Deny = 3
    }

    public sealed class TrustPlan
    {
        public TrustPlanAction Action { get; set; }
        public string Reason { get; set; } = "";
    }

    public static class TlsTrustPlanner
    {
        public static TrustPlan Decide(TrustPlanInput i)
        {
            if (i == null) throw new ArgumentNullException(nameof(i));

            // 1. Already trusted: never modify the store.
            if (i.ChainTrusted)
                return new TrustPlan
                {
                    Action = TrustPlanAction.None,
                    Reason = "Certificate already trusted; no trust-store change required."
                };

            // 2. Idempotence: thumbprint already in LocalMachine\Root.
            if (i.AlreadyInRootStore)
                return new TrustPlan
                {
                    Action = TrustPlanAction.None,
                    Reason = "Certificate already present in LocalMachine\\Root; no change required."
                };

            // 3. Date validity: never trust an expired / not-yet-valid cert.
            if (i.NowUtc < i.NotBeforeUtc)
                return new TrustPlan
                {
                    Action = TrustPlanAction.Deny,
                    Reason = $"Certificate is not yet valid (NotBefore {i.NotBeforeUtc:yyyy-MM-dd} UTC)."
                };
            if (i.NowUtc > i.NotAfterUtc)
                return new TrustPlan
                {
                    Action = TrustPlanAction.Deny,
                    Reason = $"Certificate is expired (NotAfter {i.NotAfterUtc:yyyy-MM-dd} UTC)."
                };

            // 4. Identity: the requested host must match the certificate.
            if (!IdentityMatches(i.RequestedHost, i.DnsSans, i.IpSans))
                return new TrustPlan
                {
                    Action = TrustPlanAction.Deny,
                    Reason = $"Certificate identity does not cover host '{i.RequestedHost}' " +
                             $"(SAN DNS: {Join(i.DnsSans)}; SAN IP: {Join(i.IpSans)})."
                };

            // 5. CA-issued certificate: never turn a leaf into a trusted root.
            if (!i.IsSelfSigned)
                return new TrustPlan
                {
                    Action = TrustPlanAction.ReportCaChainProblem,
                    Reason = $"The Laserfiche HTTPS certificate is issued by {i.Issuer}, but Windows " +
                             "does not currently trust its certificate chain " +
                             $"({i.ChainStatusSummary}). Install the issuing/root CA certificate " +
                             "in the Local Computer certificate store and retry."
                };

            // 6. Self-signed, but the chain failure is NOT the plain
            //    missing-root case (e.g. revocation problems, invalid
            //    signature): do not auto-trust.
            if (!i.ChainUntrustedRootOnly)
                return new TrustPlan
                {
                    Action = TrustPlanAction.Deny,
                    Reason = "Self-signed certificate has chain problems other than the missing " +
                             $"LocalMachine root trust ({i.ChainStatusSummary}); automatic trust denied."
                };

            // 7. Store mutation requires elevation.
            if (!i.IsElevated)
                return new TrustPlan
                {
                    Action = TrustPlanAction.Deny,
                    Reason = "Process is not elevated; cannot modify LocalMachine\\Root."
                };

            // 8. Operator consent (wizard checkbox).
            if (!i.OperatorConsented)
                return new TrustPlan
                {
                    Action = TrustPlanAction.None,
                    Reason = "Operator declined automatic trust of the self-signed certificate; " +
                             "Dashboard authentication may fail until the certificate is trusted."
                };

            return new TrustPlan
            {
                Action = TrustPlanAction.InstallPublicCertificateToRoot,
                Reason = "Valid self-signed endpoint certificate matching the requested host; " +
                         "installing the PUBLIC certificate into LocalMachine\\Root."
            };
        }

        // True when the requested host matches the certificate identity.
        //   - IP-literal hosts match ONLY an IP SAN with the same address
        //     (a DNS SAN such as "localhost" is NOT valid for 192.168.x.x).
        //   - DNS hosts match SAN DNS entries case-insensitively, including
        //     RFC 6125-style single-label left-most wildcards (*.domain.tld
        //     matches host.domain.tld but never a.b.domain.tld or domain.tld).
        public static bool IdentityMatches(
            string host, IList<string> dnsSans, IList<string> ipSans)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            host = host.Trim().TrimEnd('.');

            if (IsIpLiteral(host))
            {
                return ipSans != null && ipSans.Any(ip =>
                    string.Equals(NormalizeIp(ip), NormalizeIp(host),
                                  StringComparison.OrdinalIgnoreCase));
            }

            if (dnsSans == null) return false;
            foreach (var rawSan in dnsSans)
            {
                var san = (rawSan ?? "").Trim().TrimEnd('.');
                if (san.Length == 0) continue;

                if (string.Equals(san, host, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Wildcard: only "*.<suffix>", only the left-most label, and
                // the host must have exactly one more label than the suffix.
                if (san.StartsWith("*.", StringComparison.Ordinal))
                {
                    var suffix = san.Substring(2);
                    if (suffix.Length == 0 || suffix.Contains("*")) continue;

                    int firstDot = host.IndexOf('.');
                    if (firstDot <= 0) continue; // single-label host: no wildcard match
                    var hostSuffix = host.Substring(firstDot + 1);
                    if (string.Equals(hostSuffix, suffix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        public static bool IsIpLiteral(string host)
        {
            System.Net.IPAddress? _;
            return System.Net.IPAddress.TryParse(host.Trim('[', ']'), out _);
        }

        private static string NormalizeIp(string ip)
        {
            System.Net.IPAddress? parsed;
            return System.Net.IPAddress.TryParse(ip.Trim('[', ']'), out parsed)
                ? parsed!.ToString()
                : ip;
        }

        private static string Join(IList<string> items) =>
            items == null || items.Count == 0 ? "(none)" : string.Join(", ", items);
    }
}
