using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace LFPortal.Web.Authentication;

public interface IOAuthTransactionCookie
{
    OAuthTransactionCookieWriteResult Write(HttpContext context, OAuthTransaction transaction);
    OAuthTransactionCookieResult Read(HttpContext context);
    void Delete(HttpContext context);
}

public sealed record OAuthTransaction(string State, string CodeVerifier, string RepositoryId,
    string ReturnUrl, DateTimeOffset CreatedAtUtc, string? LaunchSource, string RedirectUri);

public sealed record OAuthTransactionCookieResult(OAuthTransaction? Transaction, bool CookiePresent, bool IsValid);

public sealed record OAuthTransactionCookieWriteResult(
    string CookieName,
    bool Written,
    SameSiteMode SameSite,
    bool Secure,
    string Path,
    DateTimeOffset ExpiresUtc);

/// <summary>Data-Protection-encrypted, short-lived browser correlation for LFDS PKCE.</summary>
public sealed class OAuthTransactionCookie : IOAuthTransactionCookie
{
    internal const string CookieName = ".Dashboard.OAuth.Correlation";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector;

    public OAuthTransactionCookie(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("LFPortal.Web.LFDS.OAuthTransaction.v1");

    public OAuthTransactionCookieWriteResult Write(HttpContext context, OAuthTransaction transaction)
    {
        var options = CookieOptions(context);
        context.Response.Cookies.Append(CookieName,
            _protector.Protect(JsonSerializer.Serialize(transaction)), options);

        var written = context.Response.Headers.SetCookie.Any(value =>
            value?.StartsWith(CookieName + "=", StringComparison.Ordinal) == true);
        return new(
            CookieName,
            written,
            options.SameSite,
            options.Secure,
            options.Path!,
            options.Expires!.Value);
    }

    public OAuthTransactionCookieResult Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrWhiteSpace(value))
            return new(null, false, false);
        try
        {
            var transaction = JsonSerializer.Deserialize<OAuthTransaction>(_protector.Unprotect(value));
            var valid = transaction is not null && transaction.CreatedAtUtc <= DateTimeOffset.UtcNow &&
                transaction.CreatedAtUtc >= DateTimeOffset.UtcNow.Subtract(Lifetime);
            return new(valid ? transaction : null, true, valid);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return new(null, true, false);
        }
    }

    public void Delete(HttpContext context) => context.Response.Cookies.Delete(CookieName, CookieOptions(context));

    private static CookieOptions CookieOptions(HttpContext context) => new()
    {
        HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps, Path = "/", Expires = DateTimeOffset.UtcNow.Add(Lifetime),
    };
}
