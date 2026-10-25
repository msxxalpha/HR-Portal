using ClosedXML.Excel;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

// Aggregates only the most recent score snapshots, never historical superseded values.
[Authorize(Policy = "AdminOnly")]
public class MonthlyPerformanceReportsController(HRPortalDbContext db) : Controller
{
    public sealed class ReportRow
    {
        public int EvaluationId { get; set; }
        public string PersonnelNumber { get; set; } = "";
        public string Employee { get; set; } = "";
        public string Evaluator { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Rank { get; set; } = "";
        public decimal Score { get; set; }
        public decimal Maximum { get; set; }
        public decimal Percent => Maximum > 0 ? Math.Round(Score * 100m / Maximum, 1) : 0;
    }
    public sealed record Metric(string Title, decimal Percent, int Count);
    public sealed class ReportVm
    {
        public List<MonthlyPerformancePeriod> Periods { get; set; } = [];
        public MonthlyPerformancePeriod? Period { get; set; }
        public List<ReportRow> Rows { get; set; } = [];
        public List<Metric> QuestionAverages { get; set; } = [];
        public List<Metric> DomainAverages { get; set; } = [];
        public List<Metric> UnitAverages { get; set; } = [];
        public int ActiveCount { get; set; }
        public int EvaluatedCount => Rows.Count;
        public decimal Average => Rows.Count == 0 ? 0 : Math.Round(Rows.Average(x => x.Percent), 1);
        public decimal Coverage => ActiveCount == 0 ? 0 : Math.Round(Rows.Count * 100m / ActiveCount, 1);
        public decimal TotalAllocated => Rows.Sum(x => x.Score);
        public decimal TotalDeducted => Rows.Sum(x => x.Maximum - x.Score);
        public int Excellent => Rows.Count(x => x.Percent >= 90);
        public int Good => Rows.Count(x => x.Percent >= 75 && x.Percent < 90);
        public int Moderate => Rows.Count(x => x.Percent >= 60 && x.Percent < 75);
        public int NeedsImprovement => Rows.Count(x => x.Percent < 60);
    }

    private async Task<ReportVm> Build(int? periodId)
    {
        var periods = await db.MonthlyPerformancePeriods.AsNoTracking()
            .Where(x => !x.IsDeleted).OrderByDescending(x => x.StartAt).ToListAsync();
        var chosen = periods.FirstOrDefault(x => x.Id == periodId) ?? periods.FirstOrDefault();
        var result = new ReportVm
        {
            Periods = periods, Period = chosen,
            ActiveCount = await db.Employees.CountAsync(x => x.Status == "فعال")
        };
        if (chosen is null) return result;
        var evaluations = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == chosen.Id && !x.Period.IsDeleted)
            .Include(x => x.Employee).ThenInclude(x => x.OrganizationUnit)
            .Include(x => x.Scores).ToListAsync();
        var evaluatorIds = evaluations.Select(x => x.LastEvaluatorId).Distinct().ToArray();
        var names = await db.Employees.AsNoTracking().Where(x => evaluatorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName);
        result.Rows = evaluations.Select(x => new ReportRow
        {
            EvaluationId = x.Id,
            Employee = x.Employee.FirstName + " " + x.Employee.LastName,
            PersonnelNumber = x.Employee.PersonnelNumber,
            Evaluator = names.GetValueOrDefault(x.LastEvaluatorId, "—"),
            Unit = x.Employee.OrganizationUnit?.Title ?? "بدون واحد",
            Rank = x.RankTitleSnapshot,
            Score = x.TotalScore,
            Maximum = x.MaxScore
        }).OrderBy(x => x.Employee).ToList();

        var all = evaluations.SelectMany(x => x.Scores)
            .Where(x => x.MaxScore > 0).ToList();
        result.QuestionAverages = all.GroupBy(x => new { x.QuestionId, x.QuestionTitleSnapshot })
            .Select(x => new Metric(x.Key.QuestionTitleSnapshot,
                Math.Round(x.Average(y => y.Score * 100m / y.MaxScore), 1), x.Count()))
            .OrderBy(x => x.Percent).ToList();
        result.DomainAverages = all.GroupBy(x => x.DomainTitleSnapshot)
            .Select(x => new Metric(x.Key,
                Math.Round(x.Average(y => y.Score * 100m / y.MaxScore), 1), x.Count()))
            .OrderByDescending(x => x.Percent).ToList();
        result.UnitAverages = result.Rows.GroupBy(x => x.Unit)
            .Select(x => new Metric(x.Key, Math.Round(x.Average(y => y.Percent), 1), x.Count()))
            .OrderByDescending(x => x.Percent).ToList();
        return result;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? periodId, string? q, string? unit, string? rank, string? evaluator)
    {
        var model = await Build(periodId);
        ViewBag.UnitOptions = model.Rows.Select(x => x.Unit).Distinct().OrderBy(x => x).ToArray();
        ViewBag.RankOptions = model.Rows.Select(x => x.Rank).Distinct().OrderBy(x => x).ToArray();
        ViewBag.EvaluatorOptions = model.Rows.Select(x => x.Evaluator).Distinct().OrderBy(x => x).ToArray();
        ViewBag.Q = q; ViewBag.Unit = unit; ViewBag.Rank = rank; ViewBag.Evaluator = evaluator;
        if (!string.IsNullOrWhiteSpace(q))
            model.Rows = model.Rows.Where(x => (x.Employee + " " + x.PersonnelNumber + " " + x.Unit + " " + x.Evaluator)
                .Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(unit)) model.Rows = model.Rows.Where(x => x.Unit == unit).ToList();
        if (!string.IsNullOrWhiteSpace(rank)) model.Rows = model.Rows.Where(x => x.Rank == rank).ToList();
        if (!string.IsNullOrWhiteSpace(evaluator)) model.Rows = model.Rows.Where(x => x.Evaluator == evaluator).ToList();
        // Keep all charts and table aligned with the selected period. The advanced row filters
        // apply to detailed results, not to organization-wide KPI benchmarks.
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export(int? periodId)
    {
        var data = await Build(periodId);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("گزارش مدیریتی");
        var titles = new[] { "شماره پرسنلی", "کارمند", "ارزیاب نهایی", "واحد سازمانی", "رده", "امتیاز", "سقف", "کسر امتیاز", "درصد" };
        for (var i = 0; i < titles.Length; i++) ws.Cell(1, i + 1).Value = titles[i];
        for (var i = 0; i < data.Rows.Count; i++)
        {
            var p = data.Rows[i]; var r = i + 2;
            ws.Cell(r, 1).Value = p.PersonnelNumber;
            ws.Cell(r, 2).Value = p.Employee;
            ws.Cell(r, 3).Value = p.Evaluator;
            ws.Cell(r, 4).Value = p.Unit;
            ws.Cell(r, 5).Value = p.Rank;
            ws.Cell(r, 6).Value = p.Score; ws.Cell(r, 7).Value = p.Maximum;
            ws.Cell(r, 8).Value = p.Maximum - p.Score; ws.Cell(r, 9).Value = p.Percent;
        }
        var averages = wb.Worksheets.Add("میانگین سؤالات");
        averages.Cell(1, 1).Value = "سؤال"; averages.Cell(1, 2).Value = "میانگین درصد"; averages.Cell(1, 3).Value = "پاسخ‌ها";
        for (var i = 0; i < data.QuestionAverages.Count; i++)
        {
            var metric = data.QuestionAverages[i];
            averages.Cell(i + 2, 1).Value = metric.Title;
            averages.Cell(i + 2, 2).Value = metric.Percent;
            averages.Cell(i + 2, 3).Value = metric.Count;
        }
        foreach (var sheet in wb.Worksheets) { sheet.RightToLeft = true; sheet.Row(1).Style.Font.Bold = true; sheet.Columns().AdjustToContents(); sheet.SheetView.FreezeRows(1); }
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "performance-management-report.xlsx");
    }
}
