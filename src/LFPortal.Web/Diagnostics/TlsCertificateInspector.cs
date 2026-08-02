using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace LFPortal.Web.Diagnostics;

/// <summary>
/// SAFE, diagnostic-only TLS certificate inspection for the configured
/// Laserfiche host. Invoked ONLY after a login/probe request has already
/// failed with a TLS classification, so administrators can read the actual
/// certificate facts (subject, issuer, SAN coverage, validity, policy
/// errors) from the log instead of guessing.
///
/// SECURITY: this inspection NEVER affects the real HTTP pipeline. The
/// permissive validation callback exists only inside this probe to let the
/// handshake complete far enough to read the certificate; the authentication
/// request itself keeps full validation and has already failed by the time
/// this runs.
/// </summary>
public static class TlsCertificateInspector
{
    /// <summary>
    /// Connects to the host/port of <paramref name="serverUri"/>, records the
    /// presented certificate and the <see cref="SslPolicyErrors"/> the OS
    /// reported, and logs them. Never throws.
    /// </summary>
    public static async Task InspectAndLogAsync(
        Uri serverUri, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(serverUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return;

        var host = serverUri.Host;
        var port = serverUri.IsDefaultPort ? 443 : serverUri.Port;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);

            SslPolicyErrors recorded = SslPolicyErrors.None;
            X509Certificate2? presented = null;

            await using var ssl = new SslStream(
                tcp.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (_, cert, _, errs) =>
                {
                    recorded = errs;
                    if (cert is not null) presented = new X509Certificate2(cert);
                    return true; // diagnostic probe only — see class remarks
                });

            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = host }, cts.Token)
                .ConfigureAwait(false);

            if (presented is null)
            {
                logger.LogWarning(
                    "[LF TLS DIAG] {Host}:{Port} handshake completed but no certificate was presented.",
                    host, port);
                return;
            }

            var sanNames = GetSanDnsNames(presented);

            logger.LogWarning(
                "[LF TLS DIAG] Host {Host}:{Port} SslPolicyErrors={PolicyErrors}; " +
                "Subject={Subject}; Issuer={Issuer}; Thumbprint={Thumbprint}; " +
                "NotBefore={NotBefore:u}; NotAfter={NotAfter:u}; SAN=[{San}]",
                host, port, recorded,
                presented.Subject, presented.Issuer, presented.Thumbprint,
                presented.NotBefore.ToUniversalTime(), presented.NotAfter.ToUniversalTime(),
                string.Join(", ", sanNames));
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "[LF TLS DIAG] Certificate inspection of {Host}:{Port} failed before a " +
                "certificate could be read: {ErrorType}: {Message}",
                host, port, ex.GetType().Name, ex.Message);
        }
    }

    private static List<string> GetSanDnsNames(X509Certificate2 cert)
    {
        var names = new List<string>();
        foreach (var ext in cert.Extensions)
        {
            if (ext is X509SubjectAlternativeNameExtension san)
                names.AddRange(san.EnumerateDnsNames());
        }
        return names;
    }
}
