namespace LFPortal.Web.Authentication;

internal static class ExternalShareAuthenticationDefaults
{
    public const string Scheme = "ExternalShare.Cookie";
    public const string CookieName = ".Dashboard.ExternalShare";
    public const string RepositoryClaim = "external_share_repository";
    public const string AuthenticationMethodClaim = "authentication_method";
    public const string AuthenticationMethod = "RepositoryPassword";
}
