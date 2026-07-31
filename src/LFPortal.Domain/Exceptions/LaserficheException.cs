namespace LFPortal.Domain.Exceptions;

/// <summary>
/// Thrown when the Laserfiche Repository API returns an error response.
/// Callers can inspect <see cref="StatusCode"/> and <see cref="LFErrorCode"/> to determine
/// whether to retry, report a user-facing error, or escalate.
/// </summary>
public sealed class LaserficheException : Exception
{
    /// <summary>HTTP status code returned by the Laserfiche API, e.g. 401, 403, 404.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// Laserfiche-specific error code from the response body, if available.
    /// Null when the API returned an error without a structured body.
    /// </summary>
    public string? LFErrorCode { get; }

    /// <summary>
    /// Initialises a new instance with an error message, HTTP status code, and optional
    /// Laserfiche error code.
    /// </summary>
    public LaserficheException(string message, int statusCode, string? lfErrorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        LFErrorCode = lfErrorCode;
    }

    /// <summary>
    /// Initialises a new instance with an inner exception that caused the failure.
    /// </summary>
    public LaserficheException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
