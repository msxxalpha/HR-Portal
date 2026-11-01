using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace HRPortal.Services;

/// <summary>
/// Refreshes employee permission claims from active direct roles and organizational-rank roles.
/// This prevents stale cookie claims from retaining old access after administrators change assignments.
/// </summary>
public sealed class PermissionClaimsTransformation(RoleService roles) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (!principal.Identities.Any(x => x.IsAuthenticated) || principal.HasClaim("IsAdmin", "1"))
            return principal;

        // Locate the actual employee identity by its stable employee key. Do not
        // depend on AuthenticationType: cookie schemes can be renamed by hosting
        // or authentication configuration without changing the employee identity.
        var identity = principal.Identities.FirstOrDefault(x =>
            x.IsAuthenticated && x.HasClaim(claim => claim.Type == "EmployeeId"));

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
