using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Modules.Performance;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = PerformanceModule.ManagePermission)]
[Route("PerformanceAdmin")]
public sealed class PerformanceAdminController(HRPortalDbContext db, OrganizationService organization) : Controller
{
    public sealed record AssignmentInfo(int Id, string Period, string Employee, string Evaluator);
    public sealed record AdminVm(
        List<PerformancePeriod> Periods, List<PerformanceDomain> Domains,
        List<PerformancePosition> Positions, List<PerformanceQuestion> Questions,
        List<PerformancePositionQuestion> Mappings, List<Employee> Employees,
        List<PerformanceEmployeePosition> EmployeePositions, List<AssignmentInfo> Assignments,
        string RevisionTitle, string DomainCode, string QuestionCode, string PositionCode);

    private static string NextCode(IEnumerable<string> codes, string prefix)
    {
        var used = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < int.MaxValue; i++)
        {
            var candidate = $"{prefix}-{i:000}";
            if (!used.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("امکان تولید کد جدید وجود ندارد.");
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var rev = await organization.CurrentAsync();
        var periods = await db.PerformancePeriods.AsNoTracking().OrderByDescending(x => x.StartAt).ToListAsync();
        var domains = await db.PerformanceDomains.AsNoTracking().OrderBy(x => x.Title).ToListAsync();
        var positions = await db.PerformancePositions.AsNoTracking().OrderBy(x => x.Title).ToListAsync();
        var questions = await db.PerformanceQuestions.AsNoTracking().Include(x => x.Domain).OrderBy(x => x.Title).ToListAsync();
        var employees = await db.Employees.AsNoTracking().Where(x => x.Status == "فعال")
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync();
        var mappings = await db.PerformancePositionQuestions.AsNoTracking().Include(x => x.Position).Include(x => x.Question).ToListAsync();
        var employeePositions = await db.PerformanceEmployeePositions.AsNoTracking().Include(x => x.Position).ToListAsync();
        var assignmentQuery = await (
            from a in db.PerformanceAssignments.AsNoTracking()
            join p in db.PerformancePeriods.AsNoTracking() on a.PeriodId equals p.Id
            join emp in db.Employees.AsNoTracking() on a.EmployeeId equals emp.Id
            join evaluator in db.Employees.AsNoTracking() on a.EvaluatorEmployeeId equals evaluator.Id
            where !p.IsDeleted
            select new { a.Id, Period = p.Title, EmpFirst = emp.FirstName, EmpLast = emp.LastName,
                EvalFirst = evaluator.FirstName, EvalLast = evaluator.LastName }
        ).OrderByDescending(x => x.Id).Take(100).ToListAsync();

        return View(new AdminVm(periods, domains, positions, questions, mappings, employees,
            employeePositions, assignmentQuery.Select(x => new AssignmentInfo(x.Id, x.Period,
                $"{x.EmpFirst} {x.EmpLast}", $"{x.EvalFirst} {x.EvalLast}")).ToList(),
            rev?.Title ?? "نسخه نهایی ساختار یافت نشد",
            NextCode(domains.Select(x => x.Code), "DOM"),
            NextCode(questions.Select(x => x.Code), "Q"),
            NextCode(positions.Select(x => x.Code), "POS")));
    }

    [HttpPost("AddDomain"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDomain(string? code, string title)
    {
        code = code?.Trim() ?? "";
        title = title?.Trim() ?? "";
        if (code == "") code = NextCode(await db.PerformanceDomains.Select(x => x.Code).ToListAsync(), "DOM");

        if (title == "" || code.Length > 50 || title.Length > 200 ||
            await db.PerformanceDomains.AnyAsync(x => x.Code == code))
            TempData["Error"] = "عنوان یا کد حوزه نامعتبر یا تکراری است.";
        else
        {
            db.PerformanceDomains.Add(new PerformanceDomain { Code = code, Title = title });
            await db.SaveChangesAsync();
            TempData["Result"] = "حوزه ارزیابی ایجاد شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("AddPosition"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPosition(string? code, string title)
    {
        code = code?.Trim() ?? "";
        title = title?.Trim() ?? "";
        if (code == "") code = NextCode(await db.PerformancePositions.Select(x => x.Code).ToListAsync(), "POS");

        if (title == "" || code.Length > 50 || title.Length > 200 ||
            await db.PerformancePositions.AnyAsync(x => x.Code == code))
            TempData["Error"] = "عنوان یا کد رده نامعتبر یا تکراری است.";
        else
        {
            db.PerformancePositions.Add(new PerformancePosition { Code = code, Title = title });
            await db.SaveChangesAsync();
            TempData["Result"] = "رده ارزیابی ایجاد شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("AddQuestion"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuestion(string? code, string title, string text, int domainId)
    {
        code = code?.Trim() ?? "";
        title = title?.Trim() ?? "";
        text = text?.Trim() ?? "";
        if (code == "") code = NextCode(await db.PerformanceQuestions.Select(x => x.Code).ToListAsync(), "Q");

        if (title == "" || text == "" || code.Length > 50 || title.Length > 250 || text.Length > 2000 ||
            !await db.PerformanceDomains.AnyAsync(x => x.Id == domainId && x.IsActive) ||
            await db.PerformanceQuestions.AnyAsync(x => x.Code == code))
            TempData["Error"] = "اطلاعات سؤال یا حوزه ارزیابی معتبر نیست.";
        else
        {
            db.PerformanceQuestions.Add(new PerformanceQuestion
                { Code = code, Title = title, Text = text, DomainId = domainId });
            await db.SaveChangesAsync();
            TempData["Result"] = "سؤال ایجاد شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("MapQuestion"), ValidateAntiForgeryToken]
    public async Task<IActionResult> MapQuestion(int positionId, int questionId, decimal maxScore, int sortOrder = 1)
    {
        if (maxScore <= 0 || !await db.PerformancePositions.AnyAsync(x => x.Id == positionId && x.IsActive) ||
            !await db.PerformanceQuestions.AnyAsync(x => x.Id == questionId && x.IsActive))
            TempData["Error"] = "رده، سؤال یا سقف امتیاز معتبر نیست.";
        else
        {
            var item = await db.PerformancePositionQuestions.SingleOrDefaultAsync(x =>
                x.PositionId == positionId && x.QuestionId == questionId);
            if (item == null)
                db.PerformancePositionQuestions.Add(new PerformancePositionQuestion
                    { PositionId = positionId, QuestionId = questionId, MaxScore = maxScore, SortOrder = Math.Max(1, sortOrder) });
            else
            {
                item.MaxScore = maxScore;
                item.SortOrder = Math.Max(1, sortOrder);
                item.IsActive = true;
            }
            await db.SaveChangesAsync();
            TempData["Result"] = "سؤال به رده ارزیابی متصل شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("MapEmployeePosition"), ValidateAntiForgeryToken]
    public async Task<IActionResult> MapEmployeePosition(int employeeId, int positionId)
    {
        if (!await db.Employees.AnyAsync(x => x.Id == employeeId && x.Status == "فعال") ||
            !await db.PerformancePositions.AnyAsync(x => x.Id == positionId && x.IsActive))
            TempData["Error"] = "کارمند یا رده ارزیابی معتبر نیست.";
        else
        {
            var map = await db.PerformanceEmployeePositions.FindAsync(employeeId);
            if (map == null)
                db.PerformanceEmployeePositions.Add(new PerformanceEmployeePosition
                    { EmployeeId = employeeId, PositionId = positionId });
            else
                map.PositionId = positionId;
            await db.SaveChangesAsync();
            TempData["Result"] = "رده ارزیابی کارمند تنظیم شد؛ اطلاعات پرسنلی HRPortal تغییر نکرد.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("CreatePeriod"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePeriod(string title, DateTime startAt, DateTime endAt)
    {
        startAt = startAt.Date;
        endAt = endAt.Date.AddDays(1).AddTicks(-1);
        var revision = await organization.CurrentAsync();
        if (revision == null || string.IsNullOrWhiteSpace(title) || startAt >= endAt ||
            await db.PerformancePeriods.AnyAsync(p => !p.IsDeleted && p.IsOpen &&
                p.StartAt <= endAt && p.EndAt >= startAt))
        {
            TempData["Error"] = "برای دوره، عنوان، بازه بدون هم‌پوشانی و نسخه نهایی ساختار سازمانی الزامی است.";
        }
        else
        {
            db.PerformancePeriods.Add(new PerformancePeriod
            {
                Title = title.Trim(), StartAt = startAt, EndAt = endAt,
                OrganizationStructureRevisionId = revision.Id, IsOpen = true
            });
            await db.SaveChangesAsync();
            TempData["Result"] = "دوره ارزیابی با ثبت نسخه مبنای ساختار ایجاد شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("AssignEvaluator"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignEvaluator(int periodId, int employeeId, int evaluatorEmployeeId)
    {
        var period = await db.PerformancePeriods.SingleOrDefaultAsync(x => x.Id == periodId);
        if (period == null || !PerformanceService.IsEditable(period, DateTime.Now) ||
            employeeId == evaluatorEmployeeId ||
            !await db.Employees.AnyAsync(x => x.Id == employeeId && x.Status == "فعال") ||
            !await db.Employees.AnyAsync(x => x.Id == evaluatorEmployeeId && x.Status == "فعال") ||
            !await db.PerformanceEmployeePositions.AnyAsync(x => x.EmployeeId == employeeId))
        {
            TempData["Error"] = "تخصیص نامعتبر است؛ دوره باید باز باشد و کارمند رده ارزیابی داشته باشد.";
        }
        else if (await db.PerformanceEvaluations.AnyAsync(x => x.PeriodId == periodId && x.EmployeeId == employeeId))
        {
            TempData["Error"] = "این فرد قبلاً ارزیابی شده است؛ تغییر ارزیاب نیازمند فرآیند بازنگری و ثبت تاریخچه است.";
        }
        else
        {
            var assignment = await db.PerformanceAssignments.SingleOrDefaultAsync(x =>
                x.PeriodId == periodId && x.EmployeeId == employeeId);
            if (assignment == null)
                db.PerformanceAssignments.Add(new PerformanceAssignment
                    { PeriodId = periodId, EmployeeId = employeeId, EvaluatorEmployeeId = evaluatorEmployeeId });
            else
                assignment.EvaluatorEmployeeId = evaluatorEmployeeId;
            await db.SaveChangesAsync();
            TempData["Result"] = "تخصیص ارزیاب ذخیره شد.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("ClosePeriod"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ClosePeriod(int periodId)
    {
        var period = await db.PerformancePeriods.SingleOrDefaultAsync(x => x.Id == periodId && !x.IsDeleted);
        if (period == null) return NotFound();
        period.IsOpen = false;
        await db.SaveChangesAsync();
        TempData["Result"] = "دوره بسته شد؛ سوابق فقط خواندنی هستند.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("DeletePeriod"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePeriod(int periodId)
    {
        var period = await db.PerformancePeriods.SingleOrDefaultAsync(x => x.Id == periodId && !x.IsDeleted);
        if (period == null) return NotFound();
        // Archive: retain scores and their history for audit; exclude from every active report.
        period.IsOpen = false;
        period.IsDeleted = true;
        period.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Result"] = "دوره به بایگانی حذف‌شده‌ها منتقل شد و از محاسبات جاری کنار گذاشته شد.";
        return RedirectToAction(nameof(Index));
    }
}
