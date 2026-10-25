using ClosedXML.Excel;
using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public class MonthlyPerformanceController(HRPortalDbContext db, MonthlyPerformanceService performance) : Controller
{
    private bool TryEmployee(out int id) => int.TryParse(User.FindFirst("EmployeeId")?.Value, out id);
    private bool IsAdmin => User.HasClaim("IsAdmin", "1");

    [HttpGet]
    public async Task<IActionResult> Dashboard(int? periodId)
    {
        if (!TryEmployee(out var actor) || !await performance.IsEvaluatorAsync(actor)) return Forbid();
        var scoped = await performance.ScopeAsync(actor);
        var periods = await performance.PeriodsAsync();
        var active = await performance.ActivePeriodAsync();
        var historicalIds = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.OriginalEvaluatorId == actor || x.LastEvaluatorId == actor)
            .Select(x => x.PeriodId).Distinct().ToListAsync();
        var chosenPeriods = periods.Where(x => (active?.Id == x.Id) || historicalIds.Contains(x.Id) ||
            (x.StartAt <= MonthlyPerformanceService.NowIran() && x.EndAt < MonthlyPerformanceService.NowIran()
             && db.MonthlyPerformanceEvaluations.Any(e => e.PeriodId == x.Id &&
                scoped.Select(s => s.EmployeeId).Contains(e.EmployeeId)))).ToList();
        var period = chosenPeriods.FirstOrDefault(x => x.Id == periodId) ??
            chosenPeriods.FirstOrDefault(x => x.Id == active?.Id) ?? chosenPeriods.FirstOrDefault();
        var model = new MonthlyPerformanceDashboardVm
        {
            IsEvaluator = true, Periods = chosenPeriods, SelectedPeriod = period, PeriodId = period?.Id,
            CanEdit = period is not null && MonthlyPerformanceService.IsOpen(period, MonthlyPerformanceService.NowIran()),
            Message = active is null ? "دوره فعال برای ارزیابی وجود ندارد. فقط سوابق دوره‌های پیشین قابل مشاهده هستند." : ""
        };
        if (period is null) return View(model);
        var scopedIds = scoped.Select(x => x.EmployeeId).ToHashSet();
        var evals = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == period.Id &&
                (scopedIds.Contains(x.EmployeeId) ||
                 x.OriginalEvaluatorId == actor || x.LastEvaluatorId == actor ||
                 db.MonthlyPerformanceRevisions.Any(r => r.EvaluationId == x.Id && r.ChangedByEmployeeId == actor)))
            .Include(x => x.Employee).Include(x => x.Scores).ToListAsync();
        var shownIds = model.CanEdit ? scopedIds : evals.Select(x => x.EmployeeId).ToHashSet();
        var employees = await db.Employees.AsNoTracking().Where(e => shownIds.Contains(e.Id))
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync();
        var evaluatorIds = evals.Select(e => e.LastEvaluatorId).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(x => evaluatorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName);
        var upperIds = new HashSet<int>();
        foreach (var other in evaluatorIds.Where(id => id != actor))
            if (await performance.IsAboveAsync(actor, other)) upperIds.Add(other);

        model.People = employees.Select(employee =>
        {
            var ev = evals.FirstOrDefault(x => x.EmployeeId == employee.Id);
            return new MonthlyPerformancePersonRow
            {
                EmployeeId = employee.Id, EvaluationId = ev?.Id,
                FullName = employee.FirstName + " " + employee.LastName,
                PersonnelNumber = employee.PersonnelNumber,
                Rank = ev?.RankTitleSnapshot ?? scoped.FirstOrDefault(s => s.EmployeeId == employee.Id)?.RankTitle ?? "—",
                Unit = employee.PositionTitle ?? "—",
                Score = ev?.TotalScore ?? 0, MaxScore = ev?.MaxScore ?? 0,
                UpdatedAt = ev?.UpdatedAt,
                EvaluatorName = ev is null ? "ثبت نشده" : names.GetValueOrDefault(ev.LastEvaluatorId, "—"),
                CanEdit = model.CanEdit && scopedIds.Contains(employee.Id) &&
                    (ev is null || ev.LastEvaluatorId == actor || upperIds.Contains(ev.LastEvaluatorId))
            };
        }).ToList();
        model.TotalCapacity = evals.Sum(x => x.MaxScore);
        model.UsedScore = evals.Sum(x => x.TotalScore);
        model.QuestionAverages = evals.SelectMany(x => x.Scores)
            .GroupBy(x => new { x.QuestionId, x.QuestionTitleSnapshot, x.DomainTitleSnapshot })
            .Select(g => new MonthlyPerformanceQuestionAverage
            {
                Title = g.Key.QuestionTitleSnapshot, Domain = g.Key.DomainTitleSnapshot,
                AveragePercent = g.Count(x => x.MaxScore > 0) == 0 ? 0 :
                    Math.Round(g.Where(x => x.MaxScore > 0).Average(x => x.Score / x.MaxScore * 100m), 1)
            }).OrderBy(x => x.AveragePercent).ToList();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Evaluate(int employeeId, int periodId)
    {
        if (!TryEmployee(out var actor) || !await performance.IsEvaluatorAsync(actor)) return Forbid();
        var period = await db.MonthlyPerformancePeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId);
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId);
        if (period is null || employee is null) return NotFound();
        var scope = await performance.ScopeAsync(actor);
        var ev = await db.MonthlyPerformanceEvaluations.AsNoTracking().Include(x => x.Scores)
            .FirstOrDefaultAsync(x => x.PeriodId == periodId && x.EmployeeId == employeeId);
        var isScoped = scope.Any(x => x.EmployeeId == employeeId);
        var canHistory = ev != null && (ev.OriginalEvaluatorId == actor || ev.LastEvaluatorId == actor ||
            await db.MonthlyPerformanceRevisions.AnyAsync(r => r.EvaluationId == ev.Id && r.ChangedByEmployeeId == actor));
        if (!isScoped && !canHistory) return Forbid();
        var canEdit = isScoped && MonthlyPerformanceService.IsOpen(period, MonthlyPerformanceService.NowIran())
            && (ev is null || ev.LastEvaluatorId == actor || await performance.IsAboveAsync(actor, ev.LastEvaluatorId));
        var rank = ev?.RankTitleSnapshot ?? scope.FirstOrDefault(x => x.EmployeeId == employeeId)?.RankTitle ?? "";
        var model = new MonthlyPerformanceEvaluationVm
        {
            Period = period, Employee = employee, CanEdit = canEdit, EvaluationId = ev?.Id,
            RankTitle = rank, OriginalEvaluatorId = ev?.OriginalEvaluatorId, LastEvaluatorId = ev?.LastEvaluatorId
        };
        if (ev is null)
        {
            if (!canEdit) return NotFound();
            var mappings = await db.MonthlyPerformanceRankQuestions.AsNoTracking()
                .Include(x => x.Rank).Include(x => x.Question).ThenInclude(x => x.Domain)
                .Where(x => x.IsActive && x.Question.IsActive && x.Question.Domain.IsActive && x.Rank.Title == rank)
                .OrderBy(x => x.Question.SortOrder).ToListAsync();
            model.Scores = mappings.Select(x => new MonthlyPerformanceScoreVm
            {
                QuestionId = x.QuestionId, Code = x.Question.Code, Title = x.Question.Title,
                Domain = x.Question.Domain.Title, MaxScore = x.MaxScore, Score = x.MaxScore
            }).ToList();
        }
        else
        {
            var questionIds = ev.Scores.Select(x => x.QuestionId).ToList();
            var codes = await db.MonthlyPerformanceQuestions.AsNoTracking()
                .Where(x => questionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code);
            model.Scores = ev.Scores.OrderBy(x => x.QuestionId).Select(x => new MonthlyPerformanceScoreVm
            {
                QuestionId = x.QuestionId, Code = codes.GetValueOrDefault(x.QuestionId, ""),
                Title = x.QuestionTitleSnapshot, Domain = x.DomainTitleSnapshot,
                MaxScore = x.MaxScore, Score = x.Score, Note = x.Note
            }).ToList();
            var history = await db.MonthlyPerformanceRevisions.AsNoTracking()
                .Where(x => x.EvaluationId == ev.Id).OrderByDescending(x => x.ChangedAt).ToListAsync();
            var actors = history.Select(x => x.ChangedByEmployeeId).Distinct().ToList();
            var people = await db.Employees.AsNoTracking().Where(x => actors.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName);
            model.History = history.Select(x => new MonthlyPerformanceRevisionVm
            {
                Question = model.Scores.FirstOrDefault(s => s.QuestionId == x.QuestionId)?.Title ?? "—",
                Actor = people.GetValueOrDefault(x.ChangedByEmployeeId, "—"), Before = x.OldScore,
                After = x.NewScore, Reason = x.Reason, At = x.ChangedAt
            }).ToList();
        }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int employeeId, int periodId, Dictionary<int, decimal> scores,
        Dictionary<int, string?>? notes, string? reason)
    {
        if (!TryEmployee(out var actor) || !await performance.IsEvaluatorAsync(actor)) return Forbid();
        try
        {
            await performance.SaveAsync(actor, employeeId, periodId, scores, notes, reason);
            TempData["Success"] = "امتیازها ثبت و جمع نهایی محاسبه شد. تغییرات در تاریخچه قابل مشاهده‌اند.";
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Evaluate), new { employeeId, periodId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAll(int periodId)
    {
        if (!TryEmployee(out var actor) || !await performance.IsEvaluatorAsync(actor)) return Forbid();
        try
        {
            var (created, skipped, errors) = await performance.QuickAllAsync(actor, periodId);
            TempData["Success"] = $"امتیاز کامل برای {created} نفر ثبت شد؛ {skipped} ارزیابی قبلی بدون تغییر ماند و {errors} مورد فاقد سؤال معتبر یا دارای خطا بود.";
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Dashboard), new { periodId });
    }

    [HttpGet]
    public async Task<IActionResult> Mine()
    {
        if (!TryEmployee(out var actor)) return Forbid();
        var rows = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.EmployeeId == actor)
            .Include(x => x.Period).Include(x => x.Scores)
            .OrderByDescending(x => x.Period.Year).ThenByDescending(x => x.Period.Month).ToListAsync();
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> MyDetails(int periodId)
    {
        if (!TryEmployee(out var actor)) return Forbid();
        var eval = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Include(x => x.Period).Include(x => x.Scores)
            .FirstOrDefaultAsync(x => x.EmployeeId == actor && x.PeriodId == periodId);
        return eval is null ? NotFound() : View(eval);
    }

    [HttpGet]
    public async Task<IActionResult> Export(int periodId)
    {
        if (!TryEmployee(out var actor) || !await performance.IsEvaluatorAsync(actor)) return Forbid();
        var scope = (await performance.ScopeAsync(actor)).Select(x => x.EmployeeId).ToHashSet();
        var rows = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(e => e.PeriodId == periodId && (scope.Contains(e.EmployeeId) ||
                 e.OriginalEvaluatorId == actor || e.LastEvaluatorId == actor ||
                 db.MonthlyPerformanceRevisions.Any(r => r.EvaluationId == e.Id && r.ChangedByEmployeeId == actor)))
            .Include(x => x.Employee).OrderBy(x => x.Employee.LastName).ToListAsync();
        using var wb = new XLWorkbook();
        var sheet = wb.AddWorksheet("ارزیابی ماهانه");
        var header = new[] { "کد پرسنلی", "نام و نام خانوادگی", "رده", "امتیاز نهایی", "سقف امتیاز", "کسر امتیاز", "درصد" };
        for (var i = 0; i < header.Length; i++) sheet.Cell(1, i + 1).Value = header[i];
        for (var i = 0; i < rows.Count; i++)
        {
            var e = rows[i]; var r = i + 2;
            sheet.Cell(r, 1).Value = e.Employee.PersonnelNumber;
            sheet.Cell(r, 2).Value = e.Employee.FirstName + " " + e.Employee.LastName;
            sheet.Cell(r, 3).Value = e.RankTitleSnapshot;
            sheet.Cell(r, 4).Value = e.TotalScore; sheet.Cell(r, 5).Value = e.MaxScore;
            sheet.Cell(r, 6).Value = e.MaxScore - e.TotalScore;
            sheet.Cell(r, 7).Value = e.MaxScore == 0 ? 0 : e.TotalScore / e.MaxScore * 100;
        }
        sheet.Row(1).Style.Font.Bold = true; sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream(); wb.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"performance-{periodId}.xlsx");
    }
}
