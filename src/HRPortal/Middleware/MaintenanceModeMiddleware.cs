using HRPortal.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Middleware;

/// <summary>
/// Restricts ordinary application requests during a maintenance window.
/// This middleware must run after UseAuthentication so the administrator's
/// signed-in identity and IsAdmin claim are available.
/// </summary>
public sealed class MaintenanceModeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, HRPortalDbContext db)
    {
        var isAuthenticatedAdministrator =
            context.User.Identity?.IsAuthenticated == true &&
            context.User.HasClaim("IsAdmin", "1");

        if (isAuthenticatedAdministrator || IsExemptPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var maintenanceEnabled = await db.SystemSettings
            .AsNoTracking()
            .Select(x => (bool?)x.MaintenanceMode)
            .FirstOrDefaultAsync(context.RequestAborted);

        if (maintenanceEnabled == true)
        {
            context.Response.Redirect("/Home/Maintenance");
            return;
        }

        await next(context);
    }

    public static bool IsExemptPath(PathString path) =>
        // During maintenance, only the dedicated administrator sign-in,
        // safe sign-out, and maintenance notice itself remain public.
        path.StartsWithSegments("/Account/AdminLogin", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Account/Logout", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Home/Maintenance", StringComparison.OrdinalIgnoreCase);
}
