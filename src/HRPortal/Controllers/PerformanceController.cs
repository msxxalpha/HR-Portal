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
    public sealed record DashboardVm(List<PerformancePeriod> Periods, PerformancePeriod? Selected,
        List<PerformanceService.AssignmentRow> Rows, decimal TotalMax, decimal TotalScore, decimal Deducted);
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
    public async Task<IActionResult> Index(int? periodId)
    {
        var periods = await service.PeriodsAsync();
        var selected = periods.FirstOrDefault(x => x.Id == periodId)
            ?? periods.FirstOrDefault(x => PerformanceService.IsEditable(x, DateTime.Now))
            ?? periods.FirstOrDefault();
        if (!ActorId.HasValue)
            return View(new DashboardVm(periods, selected, [], 0, 0, 0));

        var rows = selected == null ? [] : await service.AssignmentRowsAsync(selected.Id, ActorId.Value);
        var max = rows.Sum(x => x.MaxScore);
        var actual = rows.Sum(x => x.Score);
        return View(new DashboardVm(periods, selected, rows, max, actual, Math.Max(0, max - actual)));
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
        if (!await service.HasAssignmentAsync(periodId, employeeId, ActorId.Value)) return Forbid();

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
            (User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", PerformanceModule.EvaluatePermission)), rows, history));
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
