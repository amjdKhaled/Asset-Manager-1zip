using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Handles language switching.  Sets the ASP.NET Core culture cookie and
/// redirects back to the originating page.  Supports GET so language links
/// work without a form.
/// </summary>
public sealed class LocalizationController : Controller
{
    /// <summary>
    /// Stores <paramref name="culture"/> in the culture cookie (1-year expiry)
    /// and redirects to <paramref name="returnUrl"/>.
    /// </summary>
    [HttpGet("/SetCulture")]
    public IActionResult SetCulture(string culture, string? returnUrl = "/")
    {
        // Guard: only accept supported cultures.
        var supported = new[] { "en", "ar" };
        if (!supported.Contains(culture, StringComparer.OrdinalIgnoreCase))
            culture = "en";

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires     = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite    = SameSiteMode.Lax,
            });

        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
    }
}
