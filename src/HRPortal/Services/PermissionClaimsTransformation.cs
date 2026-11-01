using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HRPortal.Services;

/// <summary>
/// Refreshes employee permission claims from active direct roles and organizational-rank roles.
/// This prevents stale cookie claims from retaining old access after administrators change assignments.
/// </summary>
public sealed class PermissionClaimsTransformation(RoleService roles) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.HasClaim("IsAdmin", "1"))
            return principal;

        var identity = principal.Identities.FirstOrDefault(x =>
            x.IsAuthenticated &&
            string.Equals(x.AuthenticationType, CookieAuthenticationDefaults.AuthenticationScheme,
                StringComparison.Ordinal));

        if (identity is null ||
            !int.TryParse(identity.FindFirst("EmployeeId")?.Value, out var employeeId))
            return principal;

        var permissions = await roles.GetEmployeePermissionsAsync(employeeId);
        foreach (var claim in identity.FindAll("Permission").ToArray())
            identity.RemoveClaim(claim);
        foreach (var permission in permissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim("Permission", permission));

        var isEvaluator = await roles.IsPerformanceEvaluatorAsync(employeeId);
        foreach (var claim in identity.FindAll("IsPerformanceEvaluator").ToArray())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim("IsPerformanceEvaluator", isEvaluator ? "1" : "0"));

        return principal;
    }
}
