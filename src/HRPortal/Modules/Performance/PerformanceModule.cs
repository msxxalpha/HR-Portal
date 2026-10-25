namespace HRPortal.Modules.Performance;

/// <summary>
/// Integration boundary: all personnel and organizational data must be resolved
/// from HRPortal rather than copied from the standalone performance application.
/// </summary>
public static class PerformanceModule
{
    public const string RoutePrefix = "Performance";
    public const string ViewPermission = "Performance.View";
    public const string EvaluatePermission = "Performance.Evaluate";
    public const string ManagePermission = "Performance.Manage";
    public const string ReportsPermission = "Performance.Reports";
}
