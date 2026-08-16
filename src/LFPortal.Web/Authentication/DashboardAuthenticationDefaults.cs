namespace LFPortal.Web.Authentication;

/// <summary>Constants for the Dashboard's persistent authenticated browser session.</summary>
public static class DashboardAuthenticationDefaults
{
    /// <summary>Cookie authentication scheme used after Laserfiche authentication succeeds.</summary>
    public const string Scheme = "Dashboard.Cookie";

    /// <summary>Claim that binds an authenticated identity to its Laserfiche repository.</summary>
    public const string RepositoryClaimType = "lf:repository";

    /// <summary>Claim value used for identities established by the LFDS code flow.</summary>
    public const string LfdsAuthenticationMethod = "LFDS";

    /// <summary>Claim value used for identities established by the password flow.</summary>
    public const string PasswordAuthenticationMethod = "RepositoryPassword";
}
