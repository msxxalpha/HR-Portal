using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "AdminOnly")]
public class EmployeeContractsController(
    HRPortalDbContext db,
    AuditService audit) : Controller
{
    public IActionResult Index() => View();

    public async Task<IActionResult> BasicInformation(int? editId)
    {
        EmployeePerformanceEvaluationQuestion? editing = null;
        if (editId.HasValue)
            editing = await db.EmployeePerformanceEvaluationQuestions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == editId.Value);

        return View(await BuildBasicInfoViewModelAsync(editing));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveQuestion(EmployeePerformanceEvaluationQuestion input)
    {
        input.QuestionText = input.QuestionText?.Trim() ?? "";
        input.ScoringGuidance = input.ScoringGuidance?.Trim() ?? "";

        EmployeePerformanceEvaluationQuestion? existing = null;
        if (input.Id > 0)
        {
            existing = await db.EmployeePerformanceEvaluationQuestions
                .FirstOrDefaultAsync(x => x.Id == input.Id);
            if (existing is null)
                return NotFound();
        }

        if (input.MaxScore <= 0 || input.MaxScore > 100)
            ModelState.AddModelError(nameof(input.MaxScore), "سقف امتیاز باید بیشتر از صفر و حداکثر ۱۰۰ باشد.");
        if (input.MaxScore != decimal.Round(input.MaxScore, 2))
            ModelState.AddModelError(nameof(input.MaxScore), "سقف امتیاز حداکثر می‌تواند دو رقم اعشار داشته باشد.");

        var activeTotalWithoutCurrent = await db.EmployeePerformanceEvaluationQuestions
            .Where(x => x.IsActive && x.Id != input.Id)
            .SumAsync(x => (decimal?)x.MaxScore) ?? 0m;

        if (input.IsActive && activeTotalWithoutCurrent + input.MaxScore > 100m)
            ModelState.AddModelError(nameof(input.MaxScore),
                $"جمع سقف امتیاز پرسش‌های فعال پس از این تغییر {activeTotalWithoutCurrent + input.MaxScore:0.##} می‌شود و نباید از ۱۰۰ بیشتر باشد.");

        if (!ModelState.IsValid)
            return View("BasicInformation", await BuildBasicInfoViewModelAsync(input));

        if (existing is null)
        {
            input.CreatedAt = DateTime.UtcNow;
            input.UpdatedAt = DateTime.UtcNow;
            db.EmployeePerformanceEvaluationQuestions.Add(input);
        }
        else
        {
            existing.QuestionText = input.QuestionText;
            existing.MaxScore = input.MaxScore;
            existing.ScoringGuidance = input.ScoringGuidance;
            existing.SortOrder = input.SortOrder;
            existing.IsActive = input.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        var saved = existing ?? input;
        await audit.WriteAsync(
            existing is null ? "ایجاد پرسش ارزشیابی عملکرد" : "ویرایش پرسش ارزشیابی عملکرد",
            nameof(EmployeePerformanceEvaluationQuestion),
            saved.Id.ToString(),
            saved.QuestionText);

        TempData["Success"] = existing is null ? "پرسش ارزشیابی ایجاد شد." : "پرسش ارزشیابی به‌روزرسانی شد.";
        return RedirectToAction(nameof(BasicInformation));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleQuestion(int id)
    {
        var question = await db.EmployeePerformanceEvaluationQuestions
            .FirstOrDefaultAsync(x => x.Id == id);
        if (question is null)
            return NotFound();

        if (!question.IsActive)
        {
            var activeTotal = await db.EmployeePerformanceEvaluationQuestions
                .Where(x => x.IsActive && x.Id != id)
                .SumAsync(x => (decimal?)x.MaxScore) ?? 0m;

            if (activeTotal + question.MaxScore > 100m)
            {
                TempData["Error"] = $"فعال‌سازی این پرسش باعث می‌شود مجموع سقف امتیاز از ۱۰۰ بیشتر شود (مجموع جدید: {activeTotal + question.MaxScore:0.##}). ابتدا سقف سایر پرسش‌های فعال را اصلاح کنید.";
                return RedirectToAction(nameof(BasicInformation));
            }
        }

        question.IsActive = !question.IsActive;
        question.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync(
            question.IsActive ? "فعال‌سازی پرسش ارزشیابی عملکرد" : "غیرفعال‌سازی پرسش ارزشیابی عملکرد",
            nameof(EmployeePerformanceEvaluationQuestion),
            question.Id.ToString(),
            question.QuestionText);

        TempData["Success"] = question.IsActive ? "پرسش فعال شد." : "پرسش غیرفعال شد.";
        return RedirectToAction(nameof(BasicInformation));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id)
    {
        var question = await db.EmployeePerformanceEvaluationQuestions
            .FirstOrDefaultAsync(x => x.Id == id);
        if (question is null)
            return NotFound();

        if (await db.EmployeePerformanceEvaluationScores.AnyAsync(x => x.QuestionId == id))
        {
            TempData["Error"] = "این پرسش در یک ارزشیابی استفاده شده است و حذف آن برای حفظ سوابق مجاز نیست. می‌توانید آن را غیرفعال کنید.";
            return RedirectToAction(nameof(BasicInformation));
        }

        db.EmployeePerformanceEvaluationQuestions.Remove(question);
        await db.SaveChangesAsync();
        await audit.WriteAsync(
            "حذف پرسش ارزشیابی عملکرد",
            nameof(EmployeePerformanceEvaluationQuestion),
            id.ToString(),
            question.QuestionText);

        TempData["Success"] = "پرسش حذف شد.";
        return RedirectToAction(nameof(BasicInformation));
    }

    public IActionResult Renewal() => View();

    private async Task<ContractManagementBasicInfoViewModel> BuildBasicInfoViewModelAsync(
        EmployeePerformanceEvaluationQuestion? editingQuestion = null)
    {
        var questions = await db.EmployeePerformanceEvaluationQuestions
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();

        return new ContractManagementBasicInfoViewModel
        {
            Questions = questions,
            EditingQuestion = editingQuestion ?? new EmployeePerformanceEvaluationQuestion
            {
                IsActive = true,
                SortOrder = questions.Count == 0 ? 10 : questions.Max(x => x.SortOrder) + 10
            },
            ActiveQuestionCount = questions.Count(x => x.IsActive),
            ActiveMaxScoreTotal = questions.Where(x => x.IsActive).Sum(x => x.MaxScore)
        };
    }
}
