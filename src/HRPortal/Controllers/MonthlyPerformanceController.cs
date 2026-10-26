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
        var scopedIds = scoped.Select(s => s.EmployeeId).ToArray();
        var periods = (await performance.PeriodsAsync()).Where(p => !p.IsDeleted).ToList();
        var active = await performance.ActivePeriodAsync();
        var historyPeriodIds = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.OriginalEvaluatorId == actor || x.LastEvaluatorId == actor ||
                scopedIds.Contains(x.EmployeeId) ||
                db.MonthlyPerformanceRevisions.Any(r => r.EvaluationId == x.Id && r.ChangedByEmployeeId == actor))
            .Select(x => x.PeriodId).Distinct().ToListAsync();
        var chosenPeriods = periods.Where(x => (active?.Id == x.Id) ||
            (x.StartAt <= MonthlyPerformanceService.NowIran() && historyPeriodIds.Contains(x.Id))).ToList();
        var period = chosenPeriods.FirstOrDefault(x => x.Id == periodId) ??
            chosenPeriods.FirstOrDefault(x => x.Id == active?.Id) ?? chosenPeriods.FirstOrDefault();
        var model = new MonthlyPerformanceDashboardVm
        {
            IsEvaluator = true, Periods = chosenPeriods, SelectedPeriod = period, PeriodId = period?.Id,
            CanEdit = period is not null && MonthlyPerformanceService.IsOpen(period, MonthlyPerformanceService.NowIran()),
            Message = active is null ? "دوره فعال برای ارزیابی وجود ندارد. فقط سوابق دوره‌های پیشین قابل مشاهده هستند." : ""
        };
        if (period is null) return View(model);
        var scopedSet = scopedIds.ToHashSet();
        // During an open period, every metric and row must be limited to the
        // *current* subordinate roster. Historical participation is considered
        // only for closed-period audit views.
        var evals = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == period.Id &&
                (model.CanEdit
                    ? scopedSet.Contains(x.EmployeeId)
                    : scopedSet.Contains(x.EmployeeId) ||
                      x.OriginalEvaluatorId == actor || x.LastEvaluatorId == actor ||
                      db.MonthlyPerformanceRevisions.Any(r =>
                          r.EvaluationId == x.Id && r.ChangedByEmployeeId == actor)))
            .Include(x => x.Employee).Include(x => x.Scores).ToListAsync();
        var shownIds = model.CanEdit ? scopedSet : evals.Select(x => x.EmployeeId).ToHashSet();
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
                CanEdit = model.CanEdit && scopedSet.Contains(employee.Id) &&
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
        var period = await db.MonthlyPerformancePeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId && !x.IsDeleted);
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId);
        if (period is null || employee is null) return NotFound();
        var scope = await performance.ScopeAsync(actor);
        var ev = await db.MonthlyPerformanceEvaluations.AsNoTracking().Include(x => x.Scores)
            .FirstOrDefaultAsync(x => x.PeriodId == periodId && x.EmployeeId == employeeId);
        var isScoped = scope.Any(x => x.EmployeeId == employeeId);
        var canHistory = !MonthlyPerformanceService.IsOpen(period, MonthlyPerformanceService.NowIran()) &&
            ev != null && (ev.OriginalEvaluatorId == actor || ev.LastEvaluatorId == actor ||
            await db.MonthlyPerformanceRevisions.AnyAsync(r => r.EvaluationId == ev.Id && r.ChangedByEmployeeId == actor));
        if (!isScoped && !canHistory) return Forbid();
        var canEdit = isScoped && MonthlyPerformanceService.IsOpen(period, MonthlyPerformanceService.NowIran())
            && (ev is null || ev.LastEvaluatorId == actor || await performance.IsAboveAsync(actor, ev.LastEvaluatorId));
        var rank = ev?.RankTitleSnapshot ?? scope.FirstOrDefault(x => x.EmployeeId == employeeId)?.RankTitle ?? "";
        var model = new MonthlyPerformanceEvaluationVm
        {
            Period = period, Employee = employee, CanEdit = canEdit, EvaluationId = ev?.Id,
            RankTitle = rank, EvaluationVersion = ev is null ? null : Convert.ToBase64String(ev.RowVersion),
            OriginalEvaluatorId = ev?.OriginalEvaluatorId, LastEvaluatorId = ev?.LastEvaluatorId
        };
        if (ev is null)
        {
            if (!canEdit) return NotFound();
            var mappings = await db.MonthlyPerformanceRankQuestions.AsNoTracking()
                .Include(x => x.Rank).Include(x => x.Question).ThenInclude(x => x.Domain)
                .Where(x => x.IsActive && x.Question.IsActive && x.Question.Domain.IsActive && x.Rank.Title == rank)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Question.SortOrder).ToListAsync();
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
            var activeOrder = await db.MonthlyPerformanceRankQuestions.AsNoTracking()
                .Where(x => x.Rank.Title == rank && questionIds.Contains(x.QuestionId))
                .ToDictionaryAsync(x => x.QuestionId, x => x.SortOrder);
            model.Scores = ev.Scores.OrderBy(x => activeOrder.GetValueOrDefault(x.QuestionId, int.MaxValue))
                .ThenBy(x => x.QuestionId).Select(x => new MonthlyPerformanceScoreVm
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
                After = x.NewScore, OldNote = x.OldNote, NewNote = x.NewNote,
                Reason = x.Reason, At = x.ChangedAt
            }).ToList();
        }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int employeeId, int periodId, Dictionary<int, decimal> scores,
        Dictionary<int, string?>? notes, string? reason, string? expectedVersion)
    {
        if (!TryEmployee(out var actor) || !await performance.IsEvaluatorAsync(actor)) return Forbid();
        try
        {
            await performance.SaveAsync(actor, employeeId, periodId, scores, notes, reason, expectedVersion: expectedVersion);
            TempData["Success"] = "امتیازها ثبت و جمع نهایی محاسبه شد. تغییرات در تاریخچه قابل مشاهده‌اند.";
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = "ارزیابی هم‌زمان توسط فرد دیگری تغییر کرده است؛ جزئیات را دوباره باز کنید و سپس امتیازها را ثبت کنید.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "نتیجه هم‌زمان ثبت یا تغییر کرده است. برای جلوگیری از حذف تاریخچه، صفحه را بازخوانی کنید.";
        }
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
            .Where(x => x.EmployeeId == actor && !x.Period.IsDeleted)
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
            .FirstOrDefaultAsync(x => x.EmployeeId == actor && x.PeriodId == periodId && !x.Period.IsDeleted);
        return eval is null ? NotFound() : View(eval);
    }

    [HttpGet]
    public async Task<IActionResult> ExportMine()
    {
        if (!TryEmployee(out var employeeId)) return Forbid();
        var evals = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && !x.Period.IsDeleted)
            .Include(x => x.Period).Include(x => x.Scores)
            .OrderByDescending(x => x.Period.Year).ThenByDescending(x => x.Period.Month).ToListAsync();
        using var wb = new XLWorkbook();
        var periods = wb.AddWorksheet("خلاصه دوره‌ها");
        var headers = new[] { "دوره", "سال", "ماه", "امتیاز", "سقف", "کسر امتیاز", "درصد" };
        for (var i = 0; i < headers.Length; i++) periods.Cell(1, i + 1).Value = headers[i];
        for (int i = 0; i < evals.Count; i++)
        {
            var e = evals[i]; int r = i + 2;
            periods.Cell(r, 1).Value = e.Period.Title; periods.Cell(r, 2).Value = e.Period.Year;
            periods.Cell(r, 3).Value = e.Period.Month; periods.Cell(r, 4).Value = e.TotalScore;
            periods.Cell(r, 5).Value = e.MaxScore; periods.Cell(r, 6).Value = e.MaxScore - e.TotalScore;
            periods.Cell(r, 7).Value = e.MaxScore == 0 ? 0 : e.TotalScore / e.MaxScore * 100;
        }
        var questions = wb.AddWorksheet("امتیاز سؤالات");
        var qHeaders = new[] { "دوره", "حوزه", "سؤال", "امتیاز", "سقف", "کسر", "توضیح" };
        for (int i = 0; i < qHeaders.Length; i++) questions.Cell(1, i + 1).Value = qHeaders[i];
        var row = 2;
        foreach (var e in evals)
            foreach (var s in e.Scores)
            {
                questions.Cell(row, 1).Value = e.Period.Title;
                questions.Cell(row, 2).Value = s.DomainTitleSnapshot;
                questions.Cell(row, 3).Value = s.QuestionTitleSnapshot;
                questions.Cell(row, 4).Value = s.Score;
                questions.Cell(row, 5).Value = s.MaxScore;
                questions.Cell(row, 6).Value = s.MaxScore - s.Score;
                questions.Cell(row, 7).Value = s.Note ?? "";
                row++;
            }
        foreach (var sheet in wb.Worksheets) { sheet.Row(1).Style.Font.Bold = true; sheet.Columns().AdjustToContents(); sheet.SheetView.FreezeRows(1); }
        using var stream = new MemoryStream(); wb.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "my-monthly-performance.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportEvaluation(int employeeId, int periodId)
    {
        if (!TryEmployee(out var actor)) return Forbid();
        var ev = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.PeriodId == periodId && !x.Period.IsDeleted).Include(x => x.Scores).Include(x => x.Period)
            .FirstOrDefaultAsync();
        if (ev is null) return NotFound();
        var allowed = actor == employeeId ||
            (await performance.IsEvaluatorAsync(actor) &&
                ((await performance.ScopeAsync(actor)).Any(x => x.EmployeeId == employeeId) ||
                 ev.OriginalEvaluatorId == actor || ev.LastEvaluatorId == actor ||
                 await db.MonthlyPerformanceRevisions.AnyAsync(r => r.EvaluationId == ev.Id && r.ChangedByEmployeeId == actor)));
        if (!allowed) return Forbid();
        using var wb = new XLWorkbook();
        var sheet = wb.AddWorksheet("جزییات ارزیابی");
        var headers = new[] { "دوره", "حوزه", "سؤال", "سقف", "امتیاز", "کسر امتیاز", "توضیح" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        var row = 2;
        foreach (var score in ev.Scores.OrderBy(x => x.QuestionId))
        {
            sheet.Cell(row, 1).Value = ev.Period.Title; sheet.Cell(row, 2).Value = score.DomainTitleSnapshot;
            sheet.Cell(row, 3).Value = score.QuestionTitleSnapshot; sheet.Cell(row, 4).Value = score.MaxScore;
            sheet.Cell(row, 5).Value = score.Score; sheet.Cell(row, 6).Value = score.MaxScore - score.Score;
            sheet.Cell(row, 7).Value = score.Note ?? "";
            row++;
        }
        sheet.Row(1).Style.Font.Bold = true; sheet.Columns().AdjustToContents(); sheet.SheetView.FreezeRows(1);
        using var stream = new MemoryStream(); wb.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"performance-{periodId}-{employeeId}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> Export(int periodId)
    {
        if (!TryEmployee(out var actor) || !await performance.IsEvaluatorAsync(actor)) return Forbid();
        var scope = (await performance.ScopeAsync(actor)).Select(x => x.EmployeeId).ToHashSet();
        var selectedPeriod = await db.MonthlyPerformancePeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId && !p.IsDeleted);
        if (selectedPeriod is null) return NotFound();
        var currentOpen = MonthlyPerformanceService.IsOpen(
            selectedPeriod, MonthlyPerformanceService.NowIran());
        var rows = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(e => e.PeriodId == periodId &&
                (currentOpen
                    ? scope.Contains(e.EmployeeId)
                    : scope.Contains(e.EmployeeId) ||
                      e.OriginalEvaluatorId == actor || e.LastEvaluatorId == actor ||
                      db.MonthlyPerformanceRevisions.Any(r =>
                          r.EvaluationId == e.Id && r.ChangedByEmployeeId == actor)))
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
