using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class MonthlyPerformanceImprovementService(HRPortalDbContext db)
{
    public async Task<MonthlyPerformanceImprovementAction> CreateAsync(
        int ownerEmployeeId, int scoreId, string? title, string? description, DateTime dueDate)
    {
        var score = await db.MonthlyPerformanceScores
            .Include(x => x.Evaluation).ThenInclude(x => x.Period)
            .FirstOrDefaultAsync(x => x.Id == scoreId)
            ?? throw new InvalidOperationException("شاخص موردنظر در ارزیابی پیدا نشد.");

        if (score.Evaluation.EmployeeId != ownerEmployeeId)
            throw new UnauthorizedAccessException("فقط برای ارزیابی شخصی خود می‌توانید برنامه اقدام ثبت کنید.");
        if (score.Evaluation.Period.IsDeleted)
            throw new InvalidOperationException("برای دوره بایگانی‌شده امکان ثبت اقدام جدید وجود ندارد.");
        if (score.MaxScore <= 0 || score.Score >= score.MaxScore)
            throw new InvalidOperationException("برای شاخصی که امتیاز کامل گرفته است، اقدام بهبود جدید لازم نیست.");

        var normalizedTitle = (title ?? string.Empty).Trim();
        var normalizedDescription = (description ?? string.Empty).Trim();
        if (normalizedTitle.Length is < 3 or > 200)
            throw new InvalidOperationException("عنوان اقدام باید بین ۳ تا ۲۰۰ نویسه باشد.");
        if (normalizedDescription.Length > 2000)
            throw new InvalidOperationException("شرح اقدام نمی‌تواند بیش از ۲۰۰۰ نویسه باشد.");
        if (dueDate.Date < MonthlyPerformanceService.NowIran().Date)
            throw new InvalidOperationException("تاریخ پایان اقدام نمی‌تواند در گذشته باشد.");

        var now = DateTime.UtcNow;
        var action = new MonthlyPerformanceImprovementAction
        {
            ScoreId = scoreId,
            Score = score,
            OwnerEmployeeId = ownerEmployeeId,
            Title = normalizedTitle,
            Description = string.IsNullOrWhiteSpace(normalizedDescription) ? null : normalizedDescription,
            DueDate = dueDate.Date,
            Status = MonthlyPerformanceImprovementStatuses.InProgress,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.MonthlyPerformanceImprovementActions.Add(action);
        await db.SaveChangesAsync();
        return action;
    }

    public async Task<MonthlyPerformanceImprovementAction> UpdateAsync(
        int ownerEmployeeId, int actionId, string? title, string? description,
        DateTime dueDate, string? status)
    {
        var action = await db.MonthlyPerformanceImprovementActions
            .Include(x => x.Score).ThenInclude(x => x.Evaluation).ThenInclude(x => x.Period)
            .FirstOrDefaultAsync(x => x.Id == actionId)
            ?? throw new InvalidOperationException("اقدام بهبود موردنظر پیدا نشد.");

        if (action.OwnerEmployeeId != ownerEmployeeId ||
            action.Score.Evaluation.EmployeeId != ownerEmployeeId)
            throw new UnauthorizedAccessException("فقط مالک برنامه اقدام می‌تواند آن را ویرایش کند.");
        if (action.Score.Evaluation.Period.IsDeleted)
            throw new InvalidOperationException("دوره بایگانی‌شده است و برنامه اقدام آن دیگر قابل تغییر نیست.");

        var normalizedTitle = (title ?? string.Empty).Trim();
        var normalizedDescription = (description ?? string.Empty).Trim();
        var normalizedStatus = (status ?? string.Empty).Trim();

        if (normalizedTitle.Length is < 3 or > 200)
            throw new InvalidOperationException("عنوان اقدام باید بین ۳ تا ۲۰۰ نویسه باشد.");
        if (normalizedDescription.Length > 2000)
            throw new InvalidOperationException("شرح اقدام نمی‌تواند بیش از ۲۰۰۰ نویسه باشد.");
        if (!MonthlyPerformanceImprovementStatuses.IsValid(normalizedStatus))
            throw new InvalidOperationException("وضعیت انتخاب‌شده برای اقدام معتبر نیست.");
        // An overdue action may still be closed or cancelled. A new or
        // continued InProgress action must always carry a non-past deadline.
        if (normalizedStatus == MonthlyPerformanceImprovementStatuses.InProgress &&
            dueDate.Date < MonthlyPerformanceService.NowIran().Date)
            throw new InvalidOperationException("برای ادامه اقدام در وضعیت «در حال انجام»، تاریخ پایان باید امروز یا یکی از روزهای آینده باشد.");

        action.Title = normalizedTitle;
        action.Description = string.IsNullOrWhiteSpace(normalizedDescription) ? null : normalizedDescription;
        action.DueDate = dueDate.Date;
        action.Status = normalizedStatus;
        action.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return action;
    }

    public async Task<List<MonthlyPerformanceImprovementActionVm>> ListMineAsync(int ownerEmployeeId)
    {
        var actions = await db.MonthlyPerformanceImprovementActions.AsNoTracking()
            .Where(x => x.OwnerEmployeeId == ownerEmployeeId && !x.Score.Evaluation.Period.IsDeleted)
            .Include(x => x.Score).ThenInclude(x => x.Evaluation).ThenInclude(x => x.Period)
            .Include(x => x.OwnerEmployee)
            .OrderBy(x => x.DueDate).ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
        return actions.Select(ToViewModel).ToList();
    }

    public async Task<List<MonthlyPerformanceImprovementActionVm>> ListForScopeAsync(
        IReadOnlyCollection<int> employeeIds, int periodId)
    {
        if (employeeIds.Count == 0) return [];

        var actions = await db.MonthlyPerformanceImprovementActions.AsNoTracking()
            .Where(x => employeeIds.Contains(x.OwnerEmployeeId) &&
                        x.Score.Evaluation.PeriodId == periodId && !x.Score.Evaluation.Period.IsDeleted)
            .Include(x => x.Score).ThenInclude(x => x.Evaluation).ThenInclude(x => x.Period)
            .Include(x => x.OwnerEmployee)
            .OrderBy(x => x.DueDate).ThenBy(x => x.OwnerEmployee.LastName)
            .ToListAsync();
        return actions.Select(ToViewModel).ToList();
    }

    public static MonthlyPerformanceImprovementActionVm ToViewModel(
        MonthlyPerformanceImprovementAction action) => new()
    {
        Id = action.Id,
        ScoreId = action.ScoreId,
        PeriodId = action.Score.Evaluation.PeriodId,
        PeriodTitle = action.Score.Evaluation.Period.Title,
        EmployeeId = action.OwnerEmployeeId,
        EmployeeName = action.OwnerEmployee.FirstName + " " + action.OwnerEmployee.LastName,
        QuestionTitle = action.Score.QuestionTitleSnapshot,
        DomainTitle = action.Score.DomainTitleSnapshot,
        Title = action.Title,
        Description = action.Description,
        DueDate = action.DueDate,
        Status = action.Status,
        CreatedAtUtc = action.CreatedAtUtc,
        UpdatedAtUtc = action.UpdatedAtUtc
    };
}
