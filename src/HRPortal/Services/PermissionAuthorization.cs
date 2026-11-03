using System.Security.Claims;

namespace HRPortal.Services;

/// <summary>Central authorization rule shared by policies, controllers and smoke tests.</summary>
public static class PermissionAuthorization
{
    public static bool HasPermission(ClaimsPrincipal user, string permissionCode)
    {
        if (user.HasClaim("IsAdmin", "1")) return true;
        if (user.HasClaim("Permission", permissionCode)) return true;

        // Backward compatibility: the historical Manage permission is a full
        // module grant and continues to authorize all granular renewal actions.
        if (permissionCode.StartsWith("ContractRenewal.", StringComparison.Ordinal) &&
            user.HasClaim("Permission", "ContractRenewal.Manage"))
            return true;

        if (permissionCode.StartsWith("Suggestions.", StringComparison.Ordinal) &&
            user.HasClaim("Permission", "Suggestions.Manage"))
            return true;

        return false;
    }
}
