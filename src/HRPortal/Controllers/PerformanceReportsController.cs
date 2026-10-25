using HRPortal.Data;
using HRPortal.Modules.Performance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = PerformanceModule.ReportsPermission)]
[Route("PerformanceReports")]
public sealed class PerformanceReportsController(HRPortalDbContext db, PerformanceAnalyticsService analytics) : Controller
{
    public sealed record ReportRow(string Employee, string PersonnelNumber, string Evaluator,
        decimal Score, decimal Maximum, decimal Deducted, decimal Percentage);
    public sealed record ReportVm(List<PerformancePeriod> Periods, PerformancePeriod? Selected,
        List<ReportRow> Rows, decimal TotalMax, decimal TotalScore, decimal Deducted,
        int TotalCount, int FilteredCount, int Page, int PageSize, string Search,
        PerformanceAnalyticsService.Analytics Analytics);

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(int? periodId, string? q, int page = 1, int pageSize = 25)
    {
        var periods = await db.PerformancePeriods.AsNoTracking()
            .Where(x => !x.IsDeleted).OrderByDescending(x => x.StartAt).ToListAsync();
        var selected = periods.FirstOrDefault(x => x.Id == periodId) ?? periods.FirstOrDefault();

        if (selected == null) return View(new ReportVm(periods, null, [], 0, 0, 0, 0, 0, 1, 25, "", PerformanceAnalyticsService.Analytics.Empty));

        var rawRows = await (
            from ev in db.PerformanceEvaluations.AsNoTracking()
            join emp in db.Employees.AsNoTracking() on ev.EmployeeId equals emp.Id
            join evaluator in db.Employees.AsNoTracking() on ev.EvaluatorEmployeeId equals evaluator.Id
            where ev.PeriodId == selected.Id
            orderby emp.LastName, emp.FirstName
            select new { emp.FirstName, emp.LastName, emp.PersonnelNumber,
                EvaluatorFirst = evaluator.FirstName, EvaluatorLast = evaluator.LastName,
                ev.FinalScore, ev.FinalMaxScore }
        ).ToListAsync();

        var rows = rawRows.Select(x => new ReportRow(
            $"{x.FirstName} {x.LastName}", x.PersonnelNumber,
            $"{x.EvaluatorFirst} {x.EvaluatorLast}",
            x.FinalScore, x.FinalMaxScore, Math.Max(0, x.FinalMaxScore - x.FinalScore),
            x.FinalMaxScore == 0 ? 0 : Math.Round(100 * x.FinalScore / x.FinalMaxScore, 2))).ToList();

        var maximum = rows.Sum(x => x.Maximum);
        var total = rows.Sum(x => x.Score);
        q = (q ?? "").Trim();
        var filtered = q == "" ? rows : rows.Where(x =>
            x.Employee.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            x.Evaluator.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            x.PersonnelNumber.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        pageSize = pageSize is 25 or 50 or 75 or 100 ? pageSize : 25;
        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var pageRows = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return View(new ReportVm(periods, selected, pageRows, maximum, total, Math.Max(0, maximum - total),
            rows.Count, filtered.Count, page, pageSize, q,
            await analytics.BuildAsync(selected.Id)));
    }
}
