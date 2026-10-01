using System.Security.Claims;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LFPortal.Web.Authentication;

public sealed class RepositoryAuthenticationFilter(
    ILaserficheAuthService auth, IRepositoryContext repositories,
    ILogger<RepositoryAuthenticationFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var method = http.User.FindFirst(ClaimTypes.AuthenticationMethod)?.Value;
        if (http.User.Identity?.IsAuthenticated != true || method is not ("LFDS" or "RepositoryPassword") ||
            new[] { "/Login", "/Launch", "/Settings", "/Share", "/Home", "/SetCulture", "/health", "/Archive/OpenInLaserfiche" }
                .Any(path => http.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase)))
        {
            await next(); return;
        }
        if (await RecoverAsync(http) is {} before) { context.Result = before; return; }
        var executed = await next();
        // A controller may have caught an expired-token exception and built an error
        // view. Recover before MVC renders it, preserving the requested destination.
        if (!http.Response.HasStarted && await RecoverAsync(http) is {} after)
        {
            executed.ExceptionHandled = true;
            executed.Result = after;
        }
    }

    private async Task<IActionResult?> RecoverAsync(HttpContext http)
    {
        try
        {
            var repo = await repositories.GetActiveRepositoryAsync(http.RequestAborted);
            await auth.GetTokenAsync(repo, http.RequestAborted);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or LaserficheException { StatusCode: 401 })
        {
            logger.LogInformation("Dashboard repository session expired; returning to sign-in.");
            http.Session.Remove("AuthenticatedRepositoryId");
            http.Session.Remove("AuthenticatedLaserficheUser");
            await http.SignOutAsync(DashboardAuthenticationDefaults.Scheme);
            http.User = new ClaimsPrincipal(new ClaimsIdentity());
            var isPartial = http.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                http.Request.Path.StartsWithSegments("/Document/Preview");
            var destination = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
            if (isPartial)
            {
                destination = "/Archive";
                if (Uri.TryCreate(http.Request.Headers.Referer.ToString(), UriKind.Absolute, out var referer) &&
                    referer.GetLeftPart(UriPartial.Authority).Equals(
                        $"{http.Request.Scheme}://{http.Request.Host}", StringComparison.OrdinalIgnoreCase))
                    destination = referer.PathAndQuery;
            }
            var loginUrl = "/Login?sessionExpired=true&returnUrl=" + Uri.EscapeDataString(destination);
            if (isPartial)
                return new JsonResult(new { loginUrl, message = "انتهت جلسة Laserfiche. سجّل الدخول للمتابعة." }) { StatusCode = 401 };
            return new LocalRedirectResult(loginUrl);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // A network/server failure is not proof that the user's identity expired.
            logger.LogWarning(ex, "Repository token availability check failed.");
            return null;
        }
    }
}
