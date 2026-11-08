using System.Diagnostics;
using HRPortal.Models;
using HRPortal.Services;

namespace HRPortal.Middleware;

/// <summary>
/// Records each authenticated page/action request without reading request bodies or query strings.
/// </summary>
public sealed class ActivityAuditMiddleware(RequestDelegate next, ILogger<ActivityAuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, AuditLogQueue queue)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        catch
        {
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            try
            {
                var path = context.Request.Path.Value ?? "/";
                var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
                var isAuthenticationAttempt = path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/Account/AdminLogin", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/Account/EmployeeLogin", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/Account/EmployeePasswordLogin", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/Account/Verify", StringComparison.OrdinalIgnoreCase);

                if (isAuthenticated || isAuthenticationAttempt)
                {
                    var route = context.Request.RouteValues;
                    var controller = route.TryGetValue("controller", out var c) ? c?.ToString() : null;
                    var actionName = route.TryGetValue("action", out var a) ? a?.ToString() : null;
                    var isAdmin = context.User.HasClaim("IsAdmin", "1");
                    int? employeeId = int.TryParse(context.User.FindFirst("EmployeeId")?.Value, out var eid) ? eid : null;
                    int? adminId = int.TryParse(context.User.FindFirst("AdminId")?.Value, out var aid) ? aid : null;
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var status = context.Response.StatusCode;
                    var authenticationRedirect = !isAuthenticated && status is 301 or 302 && !isAuthenticationAttempt;
                    var failedRequest = status >= 400 || authenticationRedirect;
                    var userName = isAdmin
                        ? context.User.FindFirst("AdminUsername")?.Value ?? context.User.Identity?.Name
                        : context.User.Identity?.Name ?? context.User.FindFirst("PersonnelNumber")?.Value;
                    var routeEntityId = route.TryGetValue("id", out var routeId) ? routeId?.ToString() : null;

                    var entry = new AuditLog
                    {
                        EmployeeId = employeeId,
                        AdminUserId = adminId,
                        UserName = Limit(userName, 200),
                        UserType = isAdmin ? "Admin" :
                            context.User.FindFirst("UserType")?.Value ?? (isAuthenticated ? "Employee" : "Anonymous"),
                        Action = Limit(actionName ?? context.Request.Method, 100) ?? context.Request.Method,
                        EntityName = Limit(controller ?? "HTTP", 100) ?? "HTTP",
                        EntityId = Limit(routeEntityId, 200),
                        Description = Limit($"درخواست {context.Request.Method} {path}", 2000),
                        IpAddress = Limit(context.Connection.RemoteIpAddress?.ToString(), 64),
                        HttpMethod = Limit(context.Request.Method, 10),
                        RequestPath = Limit(path, 500),
                        Controller = Limit(controller, 100),
                        ActionName = Limit(actionName, 100),
                        StatusCode = status,
                        DurationMs = (long)Math.Max(0, elapsed),
                        Category = failedRequest ? "Failure" : CategoryFor(controller, actionName, path, status),
                        CorrelationId = Limit(context.TraceIdentifier, 100),
                        UserAgent = Limit(context.Request.Headers["User-Agent"].ToString(), 500),
                        IsSuccess = !failedRequest
                    };
                    queue.TryEnqueue(entry);
                }
            }
            catch (Exception ex)
            {
                // Audit instrumentation must never change the outcome of the user's operation.
                logger.LogWarning(ex, "Failed to enqueue request audit record.");
            }
        }
    }

    private static string CategoryFor(string? controller, string? action, string path, int status)
    {
        if (status >= 400) return "Failure";
        if (string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase)) return "Security";
        if (new[] { "Workflow", "EmployeeContracts", "ContractRenewalTask", "Suggestions",
                "SuggestionsReports", "MonthlyPerformance", "MonthlyPerformanceAdmin", "MonthlyPerformanceReports" }
            .Contains(controller, StringComparer.OrdinalIgnoreCase)) return "Workflow";
        if (new[] { "Users", "Organization", "OrganizationPositions", "SystemSettings",
                "SystemForms", "DatabaseMaintenance" }
            .Contains(controller, StringComparer.OrdinalIgnoreCase)) return "Administration";
        if (new[] { "Payroll", "PersonnelOrder", "Reports" }
            .Contains(controller, StringComparer.OrdinalIgnoreCase)) return "EmployeeServices";
        return "Application";
    }

    private static string? Limit(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
