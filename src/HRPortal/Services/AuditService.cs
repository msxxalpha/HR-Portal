using HRPortal.Models;

namespace HRPortal.Services;

/// <summary>
/// Semantic audit events emitted by business services. Persistence is queued and
/// deliberately decoupled from the business DbContext/transaction.
/// </summary>
public class AuditService(AuditLogQueue queue, IHttpContextAccessor http)
{
    public Task WriteAsync(string action, string entity, string? entityId = null, string? description = null, int? employeeId = null)
    {
        try
        {
            var context = http.HttpContext;
            var principal = context?.User;
            if (employeeId is null && int.TryParse(principal?.FindFirst("EmployeeId")?.Value, out var id))
                employeeId = id;
            int? adminId = int.TryParse(principal?.FindFirst("AdminId")?.Value, out var parsedAdminId)
                ? parsedAdminId : null;
            var isAdmin = principal?.HasClaim("IsAdmin", "1") == true;
            var failedLogin = action.Contains("ناموفق", StringComparison.Ordinal);
            var userName = principal?.FindFirst("AdminUsername")?.Value ??
                principal?.Identity?.Name ??
                (action.Contains("ورود موفق", StringComparison.Ordinal) ? description :
                 failedLogin ? entityId : null);

            queue.TryEnqueue(new AuditLog
            {
                EmployeeId = employeeId,
                AdminUserId = adminId,
                UserName = Limit(userName, 200),
                UserType = isAdmin || entity == "AdminUser" || action.Contains("مدیر", StringComparison.Ordinal)
                    ? "Admin"
                    : principal?.FindFirst("UserType")?.Value ?? (failedLogin && employeeId is null ? "Anonymous" : "Employee"),
                Action = Limit(action, 100) ?? action,
                EntityName = Limit(entity, 100) ?? entity,
                EntityId = Limit(entityId, 200),
                Description = Limit(description, 2000),
                IpAddress = Limit(context?.Connection.RemoteIpAddress?.ToString(), 64),
                HttpMethod = Limit(context?.Request.Method, 10),
                RequestPath = Limit(context?.Request.Path.Value, 500),
                Controller = "BusinessEvent",
                ActionName = Limit(action, 100),
                Category = failedLogin ? "Security" : "Business",
                CorrelationId = Limit(context?.TraceIdentifier, 100),
                IsSuccess = !failedLogin
            });
        }
        catch
        {
            // Audit failures must not break the business operation.
        }

        return Task.CompletedTask;
    }

    private static string? Limit(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
