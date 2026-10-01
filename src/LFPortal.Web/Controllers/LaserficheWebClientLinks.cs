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
        return $"{origin}/Laserfiche/browse.aspx?db={Uri.EscapeDataString(repositoryId)}#?";
    }

    public static string EntrySearch(string serverUrl, string repositoryId, int entryId, string? origin = null)
    {
        if (entryId <= 0) throw new ArgumentOutOfRangeException(nameof(entryId));
        return BaseUrl(serverUrl, repositoryId, origin) + "search=" + Uri.EscapeDataString($"{{LF:ID={entryId}}}");
    }
}
