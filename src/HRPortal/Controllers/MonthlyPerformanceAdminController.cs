using ClosedXML.Excel;
using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "AdminOnly")]
public class MonthlyPerformanceAdminController(HRPortalDbContext db) : Controller
{
    private static string Code(string? supplied, string prefix, IEnumerable<string> existing)
    {
        if (!string.IsNullOrWhiteSpace(supplied)) return supplied.Trim().ToUpperInvariant();
        var codes = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = 1; string result;
        do { result = $"{prefix}-{n++:000}"; } while (codes.Contains(result));
        return result;
    }

    [HttpGet]
    public async Task<IActionResult> Basic(string tab = "periods", int? editPeriodId = null)
    {
        var model = new MonthlyPerformanceBasicVm
        {
            Tab = tab, EditPeriodId = editPeriodId,
            Periods = await db.MonthlyPerformancePeriods.AsNoTracking()
                .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync(),
            Ranks = await db.MonthlyPerformanceRanks.AsNoTracking().OrderBy(x => x.Title).ToListAsync(),
            Domains = await db.MonthlyPerformanceDomains.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(),
            Questions = await db.MonthlyPerformanceQuestions.AsNoTracking()
                .Include(x => x.Domain).OrderBy(x => x.SortOrder).ToListAsync(),
            Mappings = await db.MonthlyPerformanceRankQuestions.AsNoTracking()
                .Include(x => x.Rank).Include(x => x.Question).OrderBy(x => x.Rank.Title).ToListAsync(),
            Units = await db.OrganizationNodes.AsNoTracking()
                .Where(x => x.OrganizationStructureRevisionId ==
                    db.OrganizationStructureRevisions.Where(r => r.IsFinalized && r.EffectiveDate <= DateTime.Today)
                    .OrderByDescending(r => r.EffectiveDate).ThenByDescending(r => r.Id)
                    .Select(r => r.Id).FirstOrDefault())
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToListAsync(),
            Evaluators = await db.Employees.AsNoTracking().Where(x => x.Status == "فعال")
                .OrderBy(x => x.LastName).Select(x => new MonthlyPerformanceEmployeeVm
                {
                    Id = x.Id, Name = x.FirstName + " " + x.LastName,
                    Number = x.PersonnelNumber, IsEvaluator = x.IsPerformanceEvaluator
                }).ToListAsync()
        };
        model.EditPeriod = editPeriodId.HasValue ? model.Periods.FirstOrDefault(x => x.Id == editPeriodId.Value) : null;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> PeriodDetails(int id)
    {
        var period = await db.MonthlyPerformancePeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (period is null) return NotFound();
        var evals = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == id).Include(x => x.Employee)
            .OrderByDescending(x => x.TotalScore).ToListAsync();
        ViewBag.Evaluations = evals;
        return View(period);
    }

    [HttpGet]
    public async Task<IActionResult> EvaluationDetails(int id)
    {
        var evaluation = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Include(x => x.Period).Include(x => x.Employee).Include(x => x.Scores)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (evaluation is null) return NotFound();
        ViewBag.History = await db.MonthlyPerformanceRevisions.AsNoTracking()
            .Where(x => x.EvaluationId == id).OrderByDescending(x => x.ChangedAt).ToListAsync();
        return View(evaluation);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePeriod(int id, string? code, string title, int year, int month,
        string startJalali, string endJalali, bool isEnabled, string? description)
    {
        try
        {
            if (year is < 1300 or > 1600 || month is < 1 or > 12)
                throw new InvalidOperationException("سال یا ماه دوره معتبر نیست.");
            if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("عنوان دوره الزامی است.");
            var dates = MonthlyPerformanceService.ParseWindow(startJalali, endJalali);
            var pc = new System.Globalization.PersianCalendar();
            if (pc.GetYear(dates.start) != year || pc.GetMonth(dates.start) != month ||
                pc.GetYear(dates.endExclusive.AddDays(-1)) != year ||
                pc.GetMonth(dates.endExclusive.AddDays(-1)) != month)
                throw new InvalidOperationException("بازه شروع و پایان باید داخل ماه و سال انتخاب‌شده باشد.");
            if (await db.MonthlyPerformancePeriods.AnyAsync(x => x.Id != id && x.Year == year && x.Month == month))
                throw new InvalidOperationException("برای این ماه قبلاً دوره تعریف شده است.");
            if (isEnabled && await db.MonthlyPerformancePeriods.AnyAsync(x => x.Id != id && x.IsEnabled &&
                x.StartAt < dates.endExclusive && dates.start < x.EndAt))
                throw new InvalidOperationException("این بازه با دوره فعال دیگری همپوشانی دارد.");
            var entity = id == 0 ? new MonthlyPerformancePeriod() :
                await db.MonthlyPerformancePeriods.FirstOrDefaultAsync(x => x.Id == id) ??
                throw new InvalidOperationException("دوره یافت نشد.");
            if (id != 0 && entity.StartAt <= MonthlyPerformanceService.NowIran() &&
                await db.MonthlyPerformanceEvaluations.AnyAsync(x => x.PeriodId == id) &&
                (entity.StartAt != dates.start || entity.EndAt != dates.endExclusive))
                throw new InvalidOperationException("تغییر محدوده زمانی دوره شروع‌شده و دارای نتایج، مجاز نیست.");
            var newCode = Code(code, "PER", await db.MonthlyPerformancePeriods.Select(x => x.Code).ToListAsync());
            if (await db.MonthlyPerformancePeriods.AnyAsync(x => x.Id != id && x.Code == newCode))
                throw new InvalidOperationException("کد دوره تکراری است.");
            entity.Title = title.Trim(); entity.Code = newCode; entity.Year = year; entity.Month = month;
            entity.StartAt = dates.start; entity.EndAt = dates.endExclusive;
            entity.Description = description?.Trim(); entity.IsEnabled = isEnabled;
            if (id == 0) db.MonthlyPerformancePeriods.Add(entity);
            await db.SaveChangesAsync();
            TempData["Success"] = "دوره ارزیابی با موفقیت ذخیره شد.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Basic), new { tab = "periods" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRank(int id, string? code, string title, bool isActive)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("عنوان رده الزامی است.");
            var rank = id == 0 ? new MonthlyPerformanceRank() :
                await db.MonthlyPerformanceRanks.FindAsync(id) ?? throw new InvalidOperationException("رده یافت نشد.");
            rank.Code = Code(code, "R", await db.MonthlyPerformanceRanks.Select(x => x.Code).ToListAsync());
            rank.Title = title.Trim(); rank.IsActive = isActive;
            if (id == 0) db.MonthlyPerformanceRanks.Add(rank);
            await db.SaveChangesAsync(); TempData["Success"] = "رده ثبت شد.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Basic), new { tab = "ranks" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDomain(int id, string? code, string title, int sortOrder, bool isActive)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("عنوان حوزه الزامی است.");
            var entity = id == 0 ? new MonthlyPerformanceDomain() :
                await db.MonthlyPerformanceDomains.FindAsync(id) ?? throw new InvalidOperationException("حوزه یافت نشد.");
            entity.Code = Code(code, "D", await db.MonthlyPerformanceDomains.Select(x => x.Code).ToListAsync());
            entity.Title = title.Trim(); entity.SortOrder = sortOrder; entity.IsActive = isActive;
            if (id == 0) db.MonthlyPerformanceDomains.Add(entity);
            await db.SaveChangesAsync(); TempData["Success"] = "حوزه ثبت شد.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Basic), new { tab = "domains" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveQuestion(int id, string? code, string title, int domainId, int sortOrder, bool isActive)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("متن سؤال الزامی است.");
            if (!await db.MonthlyPerformanceDomains.AnyAsync(x => x.Id == domainId))
                throw new InvalidOperationException("حوزه نامعتبر است.");
            var entity = id == 0 ? new MonthlyPerformanceQuestion() :
                await db.MonthlyPerformanceQuestions.FindAsync(id) ?? throw new InvalidOperationException("سؤال یافت نشد.");
            entity.Code = Code(code, "Q", await db.MonthlyPerformanceQuestions.Select(x => x.Code).ToListAsync());
            entity.Title = title.Trim(); entity.DomainId = domainId; entity.SortOrder = sortOrder;
            entity.IsActive = isActive;
            if (id == 0) db.MonthlyPerformanceQuestions.Add(entity);
            await db.SaveChangesAsync(); TempData["Success"] = "سؤال ثبت شد.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Basic), new { tab = "questions" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMapping(int rankId, int questionId, decimal maxScore, bool isActive)
    {
        try
        {
            if (maxScore <= 0 || maxScore > 100000) throw new InvalidOperationException("سقف امتیاز باید مثبت باشد.");
            if (!await db.MonthlyPerformanceRanks.AnyAsync(x => x.Id == rankId) ||
                !await db.MonthlyPerformanceQuestions.AnyAsync(x => x.Id == questionId))
                throw new InvalidOperationException("سؤال یا رده نامعتبر است.");
            var mapping = await db.MonthlyPerformanceRankQuestions
                .FirstOrDefaultAsync(x => x.RankId == rankId && x.QuestionId == questionId);
            if (mapping is null)
            {
                mapping = new MonthlyPerformanceRankQuestion { RankId = rankId, QuestionId = questionId };
                db.MonthlyPerformanceRankQuestions.Add(mapping);
            }
            mapping.MaxScore = maxScore; mapping.IsActive = isActive;
            await db.SaveChangesAsync(); TempData["Success"] = "سقف امتیاز برای رده ثبت شد.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Basic), new { tab = "mappings" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEvaluator(int employeeId, bool isEvaluator)
    {
        var employee = await db.Employees.FindAsync(employeeId);
        if (employee is null) return NotFound();
        employee.IsPerformanceEvaluator = isEvaluator;
        await db.SaveChangesAsync();
        TempData["Success"] = "ویژگی ارزیاب کارمند ذخیره شد.";
        return RedirectToAction(nameof(Basic), new { tab = "evaluators" });
    }

    [HttpGet]
    public async Task<IActionResult> ExportPeriod(int periodId)
    {
        var evals = await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == periodId).Include(x => x.Employee).Include(x => x.Scores).ToListAsync();
        using var wb = new XLWorkbook();
        var summary = wb.AddWorksheet("نتایج دوره");
        var cols = new[] { "شماره پرسنلی", "نام", "رده", "امتیاز", "سقف", "کسر", "درصد" };
        for (int i = 0; i < cols.Length; i++) summary.Cell(1, i + 1).Value = cols[i];
        for (int i = 0; i < evals.Count; i++)
        {
            var e = evals[i]; var r = i + 2;
            summary.Cell(r, 1).Value = e.Employee.PersonnelNumber;
            summary.Cell(r, 2).Value = e.Employee.FirstName + " " + e.Employee.LastName;
            summary.Cell(r, 3).Value = e.RankTitleSnapshot;
            summary.Cell(r, 4).Value = e.TotalScore; summary.Cell(r, 5).Value = e.MaxScore;
            summary.Cell(r, 6).Value = e.MaxScore - e.TotalScore;
            summary.Cell(r, 7).Value = e.MaxScore == 0 ? 0 : e.TotalScore / e.MaxScore * 100m;
        }
        var details = wb.AddWorksheet("ریز امتیازها");
        var dh = new[] { "شماره پرسنلی", "نام", "حوزه", "سؤال", "سقف", "امتیاز", "کسر", "توضیحات" };
        for (int i = 0; i < dh.Length; i++) details.Cell(1, i + 1).Value = dh[i];
        int row = 2;
        foreach (var e in evals)
            foreach (var s in e.Scores)
            {
                details.Cell(row, 1).Value = e.Employee.PersonnelNumber;
                details.Cell(row, 2).Value = e.Employee.FirstName + " " + e.Employee.LastName;
                details.Cell(row, 3).Value = s.DomainTitleSnapshot;
                details.Cell(row, 4).Value = s.QuestionTitleSnapshot;
                details.Cell(row, 5).Value = s.MaxScore; details.Cell(row, 6).Value = s.Score;
                details.Cell(row, 7).Value = s.MaxScore - s.Score;
                details.Cell(row, 8).Value = s.Note ?? ""; row++;
            }
        var history = wb.AddWorksheet("تاریخچه تغییرات");
        var hh = new[] { "شناسه ارزیابی", "شناسه ارزیاب", "شناسه سؤال", "امتیاز قبلی", "امتیاز جدید", "علت", "تاریخ UTC" };
        for (int i = 0; i < hh.Length; i++) history.Cell(1, i + 1).Value = hh[i];
        var evalIds = evals.Select(x => x.Id).ToArray();
        var changes = await db.MonthlyPerformanceRevisions.AsNoTracking()
            .Where(x => evalIds.Contains(x.EvaluationId)).OrderBy(x => x.ChangedAt).ToListAsync();
        for (int i = 0; i < changes.Count; i++)
        {
            var x = changes[i]; var r = i + 2;
            history.Cell(r, 1).Value = x.EvaluationId; history.Cell(r, 2).Value = x.ChangedByEmployeeId;
            history.Cell(r, 3).Value = x.QuestionId; history.Cell(r, 4).Value = x.OldScore?.ToString() ?? "اولیه";
            history.Cell(r, 5).Value = x.NewScore; history.Cell(r, 6).Value = x.Reason;
            history.Cell(r, 7).Value = x.ChangedAt.ToString("yyyy-MM-dd HH:mm");
        }
        foreach (var sheet in wb.Worksheets) { sheet.Row(1).Style.Font.Bold = true; sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents(); }
        using var stream = new MemoryStream(); wb.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"monthly-performance-period-{periodId}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> Export(string tab = "periods")
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("اطلاعات پایه");
        var table = new List<string[]>();
        switch (tab)
        {
            case "domains":
                table.Add(["کد حوزه", "عنوان", "فعال"]);
                table.AddRange(await db.MonthlyPerformanceDomains.AsNoTracking()
                    .Select(x => new string[] { x.Code, x.Title, x.IsActive ? "بله" : "خیر" }).ToListAsync());
                break;
            case "ranks":
                table.Add(["کد رده", "عنوان", "فعال"]);
                table.AddRange(await db.MonthlyPerformanceRanks.AsNoTracking()
                    .Select(x => new string[] { x.Code, x.Title, x.IsActive ? "بله" : "خیر" }).ToListAsync());
                break;
            case "questions":
                table.Add(["کد سؤال", "متن", "حوزه", "فعال"]);
                table.AddRange(await db.MonthlyPerformanceQuestions.AsNoTracking()
                    .Select(x => new string[] { x.Code, x.Title, x.Domain.Title, x.IsActive ? "بله" : "خیر" }).ToListAsync());
                break;
            case "mappings":
                table.Add(["رده", "سؤال", "سقف", "فعال"]);
                var mapping = await db.MonthlyPerformanceRankQuestions.AsNoTracking().Include(x => x.Rank)
                    .Include(x => x.Question).ToListAsync();
                table.AddRange(mapping.Select(x => new[] { x.Rank.Title, x.Question.Title, x.MaxScore.ToString(), x.IsActive ? "بله" : "خیر" }));
                break;
            case "units":
                table.Add(["کد واحد", "عنوان", "رده", "فعال"]);
                var revId = await db.OrganizationStructureRevisions.AsNoTracking()
                    .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
                    .OrderByDescending(x => x.EffectiveDate).Select(x => x.Id).FirstOrDefaultAsync();
                var units = await db.OrganizationNodes.AsNoTracking().Where(x => x.OrganizationStructureRevisionId == revId).ToListAsync();
                table.AddRange(units.Select(x => new[] { x.Code, x.Title, x.RankType, x.IsActive ? "بله" : "خیر" }));
                break;
            case "evaluators":
                table.Add(["شماره پرسنلی", "نام", "ارزیاب"]);
                table.AddRange(await db.Employees.AsNoTracking().Select(x =>
                    new string[] { x.PersonnelNumber, x.FirstName + " " + x.LastName, x.IsPerformanceEvaluator ? "بله" : "خیر" }).ToListAsync());
                break;
            default:
                table.Add(["کد دوره", "عنوان", "سال", "ماه", "شروع", "پایان", "فعال"]);
                var periods = await db.MonthlyPerformancePeriods.AsNoTracking().ToListAsync();
                table.AddRange(periods.Select(x => new[] { x.Code, x.Title, x.Year.ToString(), x.Month.ToString(),
                    PersianDateService.Format(x.StartAt), PersianDateService.Format(x.EndAt.AddDays(-1)),
                    x.IsEnabled ? "بله" : "خیر" }));
                break;
        }
        for (int r = 0; r < table.Count; r++)
            for (int col = 0; col < table[r].Length; col++) ws.Cell(r + 1, col + 1).Value = table[r][col];
        ws.Row(1).Style.Font.Bold = true; ws.SheetView.FreezeRows(1); ws.Columns().AdjustToContents();
        using var mem = new MemoryStream(); wb.SaveAs(mem);
        return File(mem.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"performance-base-{tab}.xlsx");
    }
}
