// SslCertEndpointMatcher.cs
// Pure, dependency-free logic for deciding whether an existing http.sys TLS
// certificate binding ("netsh http show sslcert") would serve a given
// https://host:port Dashboard URL.
//
// VALIDATION-ONLY: the installer never creates HTTPS bindings or manages
// certificates.  This class only answers "does HTTPS infrastructure for this
// endpoint already exist?" so the wizard can BLOCK an https:// Dashboard URL
// that would produce ERR_SSL_PROTOCOL_ERROR in client browsers.
//
// Matching semantics (host-accurate, per code-review requirement):
//   1. Exact SNI hostname endpoint  host:port            -> match
//   2. Wildcard IP endpoints        0.0.0.0:port, [::]:port -> match
//      (http.sys serves ANY hostname on that port)
//   3. Explicit ip:port endpoint    -> match ONLY when the URL host resolves
//      to that IP (caller supplies resolved addresses; this class stays pure)
//   4. Anything else (different host, different port)   -> NO match
//
// Intentionally dependency-free (no WinForms/WiX/DNS) so it can be
// compile-linked into the net8 test project like ApiHostSelector.

using System;
using System.Collections.Generic;

namespace Dashboard.BA
{
    internal static class SslCertEndpointMatcher
    {
        /// <summary>
        /// Parses the stdout of <c>netsh http show sslcert</c> into the list of
        /// bound endpoints (lower-cased, e.g. "0.0.0.0:443", "myhost:5001").
        /// Header lines look like "IP:port : 0.0.0.0:443" or
        /// "Hostname:port : host:443" depending on binding type.
        /// </summary>
        public static List<string> ParseEndpoints(string netshOutput)
        {
            var endpoints = new List<string>();
            if (string.IsNullOrEmpty(netshOutput)) return endpoints;

            foreach (var rawLine in netshOutput.Split('\n'))
            {
                var line = rawLine.Trim();
                var portIdx = line.IndexOf(":port", StringComparison.OrdinalIgnoreCase);
                if (portIdx < 0) continue;
                var idx = line.IndexOf(':', portIdx + 5);
                if (idx > 0)
                {
                    var ep = line.Substring(idx + 1).Trim().ToLowerInvariant();
                    if (ep.Length > 0) endpoints.Add(ep);
                }
            }
            return endpoints;
        }

        /// <summary>
        /// Host-accurate match: returns true only when one of
        /// <paramref name="endpoints"/> would actually serve
        /// https://<paramref name="host"/>:<paramref name="port"/>.
        /// <paramref name="resolvedHostIps"/> are the IP addresses the host
        /// resolves to (may be null/empty when resolution failed — explicit
        /// ip:port endpoints then never match).
        /// </summary>
        public static bool Matches(
            IEnumerable<string> endpoints,
            string host,
            int port,
            IEnumerable<string>? resolvedHostIps,
            out string matchedEndpoint)
        {
            matchedEndpoint = "";
            if (endpoints == null) return false;

            string hostLower  = (host ?? "").Trim().ToLowerInvariant();
            string sniMatch   = hostLower + ":" + port;
            string wildcardV4 = "0.0.0.0:" + port;
            string wildcardV6 = "[::]:" + port;

            var ipSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (resolvedHostIps != null)
            {
                foreach (var ip in resolvedHostIps)
                {
                    var t = (ip ?? "").Trim().ToLowerInvariant();
                    if (t.Length == 0) continue;
                    ipSet.Add(t);
                    // netsh renders IPv6 endpoints in brackets.
                    if (t.Contains(":") && !t.StartsWith("[")) ipSet.Add("[" + t + "]");
                }
            }

            foreach (var rawEp in endpoints)
            {
                var ep = (rawEp ?? "").Trim().ToLowerInvariant();
                if (ep.Length == 0) continue;

                // 1. Exact SNI hostname endpoint.
                if (hostLower.Length > 0 && ep == sniMatch) { matchedEndpoint = ep; return true; }

                // 2. Wildcard IP endpoints serve every hostname on the port.
                if (ep == wildcardV4 || ep == wildcardV6) { matchedEndpoint = ep; return true; }

                // 3. Explicit ip:port — only when the URL host resolves to it.
                int sep = ep.LastIndexOf(':');
                if (sep <= 0) continue;
                if (ep.Substring(sep + 1) != port.ToString()) continue;
                var epHostPart = ep.Substring(0, sep);
                if (ipSet.Contains(epHostPart)) { matchedEndpoint = ep; return true; }
            }
            return false;
        }
    }
}
