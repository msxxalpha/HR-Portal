using HRPortal.Data;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "AdminOnly")]
public class DatabaseMaintenanceController(
    HRPortalDbContext db,
    AuditService audit) : Controller
{
    private static readonly string[] OperationalTables =
    [
        "WorkflowFieldValues",
        "WorkflowHistory",
        "EmployeePerformanceEvaluationScores",
        "WorkflowTasks",
        "ContractRenewalRequestRevisions",
        "ContractRenewalRequests",
        "WorkflowInstances",
        "OrganizationChanges",
        "OtpChallenges",
        "Announcements",
        "AuditLogs"
    ];

    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cleanup(string? confirmation, bool acknowledged)
    {
        if (!acknowledged || !string.Equals(confirmation?.Trim(), "پاکسازی کامل", StringComparison.Ordinal))
        {
            TempData["Error"] = "برای اجرای پاکسازی، تیک تأیید را فعال و عبارت «پاکسازی کامل» را دقیقاً وارد کنید.";
            return RedirectToAction(nameof(Index));
        }

        var clearedTables = new List<string>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var table in OperationalTables)
            {
                var sql = $@"
IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL
BEGIN
    EXEC(N'DELETE FROM [dbo].[{table}]; DBCC CHECKIDENT (''dbo.{table}'', RESEED, 0);');
END;";
                await db.Database.ExecuteSqlRawAsync(sql);
                clearedTables.Add(table);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            TempData["Error"] = "پاکسازی انجام نشد؛ تغییرات پایگاه داده برگشت داده شد. وجود جداول و مجوزهای SQL Server را بررسی کنید.";
            return RedirectToAction(nameof(Index));
        }

        await audit.WriteAsync(
            "پاکسازی اطلاعات عملیاتی پایگاه داده",
            "Database",
            "OperationalData",
            $"پاکسازی جداول عملیاتی: {string.Join(", ", clearedTables)}");

        TempData["Success"] = $"پاکسازی اطلاعات عملیاتی با موفقیت انجام شد. {clearedTables.Count} جدول بررسی شد؛ اطلاعات پایه و تنظیمات سامانه حفظ شدند.";
        return RedirectToAction(nameof(Index));
    }
}
