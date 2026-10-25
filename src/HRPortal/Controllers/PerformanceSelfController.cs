using HRPortal.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

/// <summary>Employees can view only their own evaluation and historical scores.</summary>
[Authorize]
[Route("PerformanceSelf")]
public sealed class PerformanceSelfController(HRPortalDbContext db) : Controller
{
    public sealed record HistoryItem(string PeriodTitle, DateTime StartAt, DateTime EndAt,
        decimal Score, decimal MaxScore, decimal Percent, int EvaluationId);
    public sealed record QuestionItem(string Domain, string Title, decimal Score, decimal MaxScore,
        decimal Percent, string? Comment);
    public sealed record SelfVm(string EmployeeName, List<HistoryItem> History,
        HistoryItem? Selected, List<QuestionItem> Questions, decimal TotalScore,
        decimal TotalMax, decimal TotalDeducted);

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(int? evaluationId)
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return Forbid();
        var employee = await db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new { x.FirstName, x.LastName }).SingleOrDefaultAsync();
        if (employee == null) return Forbid();

        // Periods marked as deleted are excluded; evaluations are never exposed cross-employee.
        var historyRaw = await (
            from e in db.PerformanceEvaluations.AsNoTracking()
            join p in db.PerformancePeriods.AsNoTracking() on e.PeriodId equals p.Id
            where e.EmployeeId == employeeId && !p.IsDeleted
            orderby p.StartAt descending
            select new { e.Id, Title = p.Title, p.StartAt, p.EndAt, Score = e.FinalScore, Max = e.FinalMaxScore }
        ).ToListAsync();

        var history = historyRaw.Select(x => new HistoryItem(
            x.Title, x.StartAt, x.EndAt, x.Score, x.Max,
            x.Max > 0 ? Math.Round(x.Score * 100m / x.Max, 2) : 0m, x.Id)).ToList();
        var selected = history.FirstOrDefault(x => x.EvaluationId == evaluationId) ?? history.FirstOrDefault();

        var scores = selected == null ? [] : await db.PerformanceScores.AsNoTracking()
            .Where(x => x.EvaluationId == selected.EvaluationId)
            .OrderBy(x => x.DomainTitleSnapshot).ThenBy(x => x.QuestionTitleSnapshot)
            .ToListAsync();
        var questions = scores.Select(x => new QuestionItem(
            x.DomainTitleSnapshot, x.QuestionTitleSnapshot, x.Score, x.MaxScore,
            x.MaxScore > 0 ? Math.Round(x.Score * 100m / x.MaxScore, 2) : 0m, x.Comment)).ToList();
        return View(new SelfVm($"{employee.FirstName} {employee.LastName}", history, selected, questions,
            history.Sum(x => x.Score), history.Sum(x => x.MaxScore),
            history.Sum(x => Math.Max(0, x.MaxScore - x.Score))));
    }
}