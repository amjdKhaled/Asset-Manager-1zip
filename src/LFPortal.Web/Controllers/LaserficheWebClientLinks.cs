namespace LFPortal.Web.Controllers;

public static class LaserficheWebClientLinks
{
    public const string OriginSessionKey = "LaserficheWebClientOrigin";

    public static string? ValidateOrigin(string serverUrl, string? candidate)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var server) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var origin) ||
            origin.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(origin.UserInfo) ||
            !string.Equals(server.Host, origin.Host, StringComparison.OrdinalIgnoreCase)) return null;
        return origin.GetLeftPart(UriPartial.Authority);
    }

    public static string BaseUrl(string serverUrl, string repositoryId, string? webClientOrigin = null)
    {
        var origin = ValidateOrigin(serverUrl, webClientOrigin)
            ?? new Uri(serverUrl, UriKind.Absolute).GetLeftPart(UriPartial.Authority);
        var client = ValidateClientUrl(serverUrl, webClientOrigin) ?? origin + "/laserfiche/Browse.aspx";
        return $"{client}?db={Uri.EscapeDataString(repositoryId)}#";
    }

    public static string? ValidateClientUrl(string serverUrl, string? candidate)
    {
        var origin = ValidateOrigin(serverUrl, candidate);
        if (origin is null || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        var path = uri.AbsolutePath;
        var slash = path.LastIndexOf('/');
        var page = path[(slash + 1)..];
        if (!page.Equals("Browse.aspx", StringComparison.OrdinalIgnoreCase) &&
            !page.Equals("index.aspx", StringComparison.OrdinalIgnoreCase)) return null;
        // Cookie paths are case-sensitive even when IIS routes are not. Keep the
        // exact virtual-directory spelling used by the authenticated Web Client.
        return origin + path[..(slash + 1)] + "Browse.aspx";
    }

    public static string EntrySearch(string serverUrl, string repositoryId, int entryId, string? origin = null)
    {
        if (entryId <= 0) throw new ArgumentOutOfRangeException(nameof(entryId));
        return BaseUrl(serverUrl, repositoryId, origin) + "search=" + Uri.EscapeDataString($"{{LF:ID={entryId}}}") + ";view=search";
    }
}
