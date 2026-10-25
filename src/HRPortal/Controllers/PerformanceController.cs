using ClosedXML.Excel;
using HRPortal.Data;
using HRPortal.Modules.Performance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = PerformanceModule.ViewPermission)]
[Route("Performance")]
public sealed class PerformanceController(HRPortalDbContext db, PerformanceService service) : Controller
{
    public sealed record QuestionAverage(string Title, int Evaluations, decimal Percent);
    public sealed record DashboardVm(List<PerformancePeriod> Periods, PerformancePeriod? Selected,
        List<PerformanceService.AssignmentRow> Rows, decimal TotalMax, decimal TotalScore, decimal Deducted,
        int TotalAssignments, int FilteredCount, int Page, int PageSize, string Search,
        List<PerformanceService.AssignmentRow> TopPerformers,
        List<PerformanceService.AssignmentRow> ImprovementCandidates,
        List<QuestionAverage> QuestionAverages);
    public sealed record EvaluationFormVm(int EmployeeId, string EmployeeName, PerformancePeriod Period,
        bool CanEdit, List<PerformanceService.ScoreDisplay> Questions,
        List<PerformanceScoreChange> History);
    public sealed class ScoreEditInput
    {
        public int QuestionId { get; set; }
        public decimal Score { get; set; }
        public string? Comment { get; set; }
    }
    public sealed class SaveForm
    {
        public int PeriodId { get; set; }
        public int EmployeeId { get; set; }
        public string? Reason { get; set; }
        public List<ScoreEditInput> Rows { get; set; } = [];
    }

    private int? ActorId => int.TryParse(User.FindFirst("EmployeeId")?.Value, out var id) ? id : null;

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(int? periodId, string? q, int page = 1, int pageSize = 25)
    {
        var periods = await service.PeriodsAsync();
        var selected = periods.FirstOrDefault(x => x.Id == periodId)
            ?? periods.FirstOrDefault(x => PerformanceService.IsEditable(x, DateTime.Now))
            ?? periods.FirstOrDefault();
        if (!ActorId.HasValue)
            return View(new DashboardVm(periods, selected, [], 0, 0, 0, 0, 0, 1, 25, "", [], [], []));

        var allRows = selected == null ? [] : await service.AssignmentRowsAsync(selected.Id, ActorId.Value);
        var max = allRows.Sum(x => x.MaxScore);
        var actual = allRows.Sum(x => x.Score);
        q = (q ?? "").Trim();
        var filtered = q == "" ? allRows : allRows.Where(x =>
            x.EmployeeName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            x.PersonnelNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            x.Position.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        pageSize = pageSize is 25 or 50 or 75 or 100 ? pageSize : 25;
        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var rows = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var evaluated = allRows.Where(x => x.EvaluationId.HasValue && x.MaxScore > 0m).ToList();
        var highest = evaluated.OrderByDescending(x => x.Score / x.MaxScore).Take(5).ToList();
        var lowest = evaluated.OrderBy(x => x.Score / x.MaxScore).Take(5).ToList();
        var evaluationIds = allRows.Where(x => x.EvaluationId.HasValue)
            .Select(x => x.EvaluationId!.Value).ToList();
        var scoreRows = await db.PerformanceScores.AsNoTracking()
            .Where(x => evaluationIds.Contains(x.EvaluationId))
            .Select(x => new { x.QuestionTitleSnapshot, x.Score, x.MaxScore })
            .ToListAsync();
        var questionAverages = scoreRows.GroupBy(x => x.QuestionTitleSnapshot)
            .Select(g => new QuestionAverage(g.Key, g.Count(),
                g.Sum(x => x.MaxScore) == 0 ? 0m : Math.Round(g.Sum(x => x.Score) * 100m / g.Sum(x => x.MaxScore), 2)))
            .OrderBy(x => x.Percent).ThenBy(x => x.Title).Take(15).ToList();
        return View(new DashboardVm(periods, selected, rows, max, actual,
            Math.Max(0, max - actual), allRows.Count, filtered.Count, page, pageSize, q,
            highest, lowest, questionAverages));
    }


    [HttpGet("ExportMyList")]
    public async Task<IActionResult> ExportMyList(int periodId)
    {
        if (!ActorId.HasValue) return Forbid();
        var period = await db.PerformancePeriods.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == periodId && !x.IsDeleted);
        if (period == null) return NotFound();
        var rows = await service.AssignmentRowsAsync(periodId, ActorId.Value);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("کارکنان تحت ارزیابی");
        sheet.RightToLeft = true;
        var columns = new[] { "شماره پرسنلی", "نام و نام خانوادگی", "رده ارزیابی",
            "وضعیت ارزیابی", "امتیاز نهایی", "سقف امتیاز", "امتیاز کسرشده" };
        for (var i = 0; i < columns.Length; i++)
            sheet.Cell(1, i + 1).Value = columns[i];
        var rowNumber = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowNumber, 1).Value = row.PersonnelNumber;
            sheet.Cell(rowNumber, 2).Value = row.EmployeeName;
            sheet.Cell(rowNumber, 3).Value = row.Position;
            sheet.Cell(rowNumber, 4).Value = row.EvaluationId.HasValue ? "ثبت شده" : "فاقد ارزیابی";
            sheet.Cell(rowNumber, 5).Value = row.Score;
            sheet.Cell(rowNumber, 6).Value = row.MaxScore;
            sheet.Cell(rowNumber, 7).Value = Math.Max(0m, row.MaxScore - row.Score);
            rowNumber++;
        }
        sheet.Range(1, 1, 1, columns.Length).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, columns.Length).Style.Fill.BackgroundColor = XLColor.LightBlue;
        sheet.Columns().AdjustToContents();
        if (rowNumber > 2)
            sheet.Range(1, 1, rowNumber - 1, columns.Length).SetAutoFilter();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"MyPerformanceAssignments-{periodId}.xlsx");
    }

    [HttpPost("QuickEvaluateAll"), Authorize(Policy = PerformanceModule.EvaluatePermission), ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickEvaluateAll(int periodId)
    {
        if (!ActorId.HasValue) return Forbid();
        try
        {
            var (created, existing, skipped) = await service.QuickEvaluateAsync(periodId, ActorId.Value);
            TempData["Result"] = $"{created} ارزیابی با سقف کامل ثبت شد؛ {existing} ارزیابی قبلی بدون تغییر و {skipped} مورد بدون سؤال باقی ماند.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index), new { periodId });
    }

    [HttpGet("Form"), Authorize(Policy = PerformanceModule.ViewPermission)]
    public async Task<IActionResult> Form(int periodId, int employeeId)
    {
        if (!ActorId.HasValue) return Forbid();
        var period = await db.PerformancePeriods.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == periodId && !x.IsDeleted);
        if (period == null) return NotFound();
        if (!await service.CanViewAsync(periodId, employeeId, ActorId.Value)) return Forbid();

        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == employeeId);
        if (employee == null) return NotFound();
        var rows = await service.ScoreRowsAsync(periodId, employeeId, ActorId.Value);
        var evaluationId = await db.PerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == periodId && x.EmployeeId == employeeId)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync();
        var history = evaluationId.HasValue
            ? await db.PerformanceScoreChanges.AsNoTracking().Where(x => x.EvaluationId == evaluationId.Value)
                .OrderByDescending(x => x.ChangedAt).ToListAsync()
            : new List<PerformanceScoreChange>();
        return View(new EvaluationFormVm(employeeId, $"{employee.FirstName} {employee.LastName}",
            period, PerformanceService.IsEditable(period, DateTime.Now) &&
            (User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", PerformanceModule.EvaluatePermission)) &&
            await service.CanEditAsync(periodId, employeeId, ActorId.Value), rows, history));
    }

    [HttpPost("Save"), Authorize(Policy = PerformanceModule.EvaluatePermission), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SaveForm form)
    {
        if (!ActorId.HasValue) return Forbid();
        try
        {
            await service.SaveAsync(form.PeriodId, form.EmployeeId, ActorId.Value,
                form.Rows.Select(x => new PerformanceService.ScoreInput(x.QuestionId, x.Score, x.Comment)).ToList(),
                form.Reason);
            TempData["Result"] = "ارزیابی و امتیاز نهایی ثبت شد.";
            return RedirectToAction(nameof(Index), new { periodId = form.PeriodId });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Form), new { periodId = form.PeriodId, employeeId = form.EmployeeId });
    }
}
