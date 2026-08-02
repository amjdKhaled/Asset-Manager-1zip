namespace LFPortal.Web.Diagnostics;

/// <summary>
/// Classifies transport-level failures when contacting the Laserfiche API into
/// a stable machine-readable code plus a human-readable detail message.
/// Shared by the login flow and the <c>/api/diagnostics/laserfiche</c> probe so
/// both surfaces report the same, precise cause.
/// </summary>
/// <remarks>
/// Classification order matters: <see cref="HttpRequestException.HttpRequestError"/>
/// is evaluated FIRST because it is the most precise signal on .NET 8 —
/// a DNS failure surfaces as <c>NameResolutionError</c> with an inner
/// <see cref="System.Net.Sockets.SocketException"/>, and checking the inner
/// exception first would collapse it into a generic network error.
/// Inner-exception inspection is the fallback for exceptions that are not
/// <see cref="HttpRequestException"/> (e.g. wrapped by resilience handlers).
/// </remarks>
public static class LaserficheErrorClassifier
{
    /// <summary>A stable classification code plus a human-readable detail.</summary>
    public sealed record Classification(string Code, string Detail);

    /// <summary>Classifies a transport-level exception.</summary>
    public static Classification Classify(Exception ex)
    {
        // ── 1. HttpRequestError — the most precise signal (.NET 8) ───────────
        if (TryClassifyHttpRequestError(ex) is { } fromHttpError)
            return fromHttpError;

        // ── 2. Inner-exception chain — TLS and raw socket causes ─────────────
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            switch (cur)
            {
                case System.Security.Authentication.AuthenticationException:
                    return new Classification("tls-error",
                        "A secure (TLS) connection to the Laserfiche server could not be established. " +
                        "The server's certificate may be invalid or untrusted on this machine.");

                case System.Net.Sockets.SocketException se:
                    return ClassifySocketError(se.SocketErrorCode);
            }
        }

        // ── 3. Timeouts ───────────────────────────────────────────────────────
        if (ex is TaskCanceledException or TimeoutException)
            return new Classification("timeout",
                "The connection to the Laserfiche server timed out. " +
                "The server may be overloaded or unreachable.");

        // ── 4. Any remaining HttpRequestException ────────────────────────────
        if (ex is HttpRequestException)
            return new Classification("connection-failed",
                "Could not connect to the Laserfiche server. " +
                "Check the API URL in Settings and your network connection.");

        return new Classification("unknown-error",
            "An unexpected error occurred while contacting the Laserfiche server.");
    }

    /// <summary>
    /// Walks the exception chain for an <see cref="HttpRequestException"/> carrying
    /// a specific <see cref="HttpRequestError"/> and classifies it. Returns null
    /// when no specific error kind is available.
    /// </summary>
    private static Classification? TryClassifyHttpRequestError(Exception ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is not HttpRequestException hre)
                continue;

            switch (hre.HttpRequestError)
            {
                case HttpRequestError.NameResolutionError:
                    return new Classification("dns-error",
                        "The Laserfiche server's host name could not be resolved (DNS). " +
                        "Check the API URL in Settings.");

                case HttpRequestError.SecureConnectionError:
                    return new Classification("tls-error",
                        "A secure (TLS) connection to the Laserfiche server could not be established. " +
                        "The server's certificate may be invalid or untrusted on this machine.");

                case HttpRequestError.ConnectionError:
                    // Refine using the inner socket error when available —
                    // ConnectionError covers both refused connections and other
                    // socket-level failures.
                    for (Exception? inner = hre.InnerException; inner is not null; inner = inner.InnerException)
                    {
                        if (inner is System.Net.Sockets.SocketException se)
                            return ClassifySocketError(se.SocketErrorCode);
                    }
                    return new Classification("connection-refused",
                        "The connection to the Laserfiche server was refused or could not be established. " +
                        "Check that the Laserfiche API Server is running and the port is correct.");
            }
            // Unspecified HttpRequestError — fall through to the inner-chain checks.
            return null;
        }

        return null;
    }

    /// <summary>Maps a socket error code to a classification.</summary>
    private static Classification ClassifySocketError(System.Net.Sockets.SocketError code) =>
        code switch
        {
            System.Net.Sockets.SocketError.ConnectionRefused =>
                new Classification("connection-refused",
                    "The Laserfiche server actively refused the connection. " +
                    "Check that the API Server is running and the port is correct."),

            System.Net.Sockets.SocketError.HostNotFound or
            System.Net.Sockets.SocketError.NoData or
            System.Net.Sockets.SocketError.TryAgain =>
                new Classification("dns-error",
                    "The Laserfiche server's host name could not be resolved (DNS). " +
                    "Check the API URL in Settings."),

            System.Net.Sockets.SocketError.TimedOut =>
                new Classification("timeout",
                    "The connection to the Laserfiche server timed out. " +
                    "The server may be overloaded or unreachable."),

            _ => new Classification("network-error",
                $"The Laserfiche server could not be reached (network error: {code}). " +
                "Check that the server is online and the API URL in Settings is correct.")
        };
}
