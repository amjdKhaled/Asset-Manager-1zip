namespace LFPortal.Web.Authentication;

/// <summary>Constants for the Dashboard's persistent authenticated browser session.</summary>
public static class DashboardAuthenticationDefaults
{
    /// <summary>Cookie authentication scheme used after Laserfiche authentication succeeds.</summary>
    public const string Scheme = "Dashboard.Cookie";

    /// <summary>Claim that binds an authenticated identity to its Laserfiche repository.</summary>
    public const string RepositoryClaimType = "lf:repository";
}
