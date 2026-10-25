using HRPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Modules.Performance;

/// <summary>Evaluation rules are isolated from the host's employee, identity and organizational repositories.</summary>
public sealed class PerformanceService(HRPortalDbContext db)
{
    public sealed record AssignmentRow(int EmployeeId, string PersonnelNumber, string EmployeeName,
        string Position, int? EvaluationId, decimal Score, decimal MaxScore);
    public sealed record ScoreInput(int QuestionId, decimal Score, string? Comment);
    public sealed record ScoreDisplay(int QuestionId, string Title, string Domain, decimal Score, decimal MaxScore, string? Comment);

    public Task<List<PerformancePeriod>> PeriodsAsync() =>
        db.PerformancePeriods.AsNoTracking().Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.StartAt).ToListAsync();

    public static bool IsEditable(PerformancePeriod period, DateTime now) =>
        !period.IsDeleted && period.IsOpen && period.StartAt <= now && period.EndAt >= now;


    /// <summary>
    /// Evaluator + all downstream evaluators in the period-specific, explicitly approved chain.
    /// This does not infer supervisory rights from a shared department.
    /// </summary>
    public async Task<HashSet<int>> EligibleEvaluatorIdsAsync(int periodId, int actorId)
    {
        var links = await db.PerformanceEvaluatorChains.AsNoTracking()
            .Where(x => x.PeriodId == periodId)
            .Select(x => new { x.SubordinateEvaluatorEmployeeId, x.SuperiorEvaluatorEmployeeId })
            .ToListAsync();
        var visible = new HashSet<int> { actorId };
        var frontier = new Queue<int>();
        frontier.Enqueue(actorId);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var link in links.Where(x => x.SuperiorEvaluatorEmployeeId == current))
            {
                if (visible.Add(link.SubordinateEvaluatorEmployeeId))
                    frontier.Enqueue(link.SubordinateEvaluatorEmployeeId);
            }
        }
        return visible;
    }

    public async Task<bool> CanViewAsync(int periodId, int employeeId, int actorId)
    {
        var assignedTo = await db.PerformanceAssignments.AsNoTracking()
            .Where(x => x.PeriodId == periodId && x.EmployeeId == employeeId)
            .Select(x => (int?)x.EvaluatorEmployeeId).SingleOrDefaultAsync();
        if (assignedTo == null) return false;
        var evaluators = await EligibleEvaluatorIdsAsync(periodId, actorId);
        return evaluators.Contains(assignedTo.Value);
    }

    public async Task<bool> CanEditAsync(int periodId, int employeeId, int actorId)
    {
        if (!await CanViewAsync(periodId, employeeId, actorId)) return false;
        var previous = await db.PerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == periodId && x.EmployeeId == employeeId)
            .Select(x => new { x.EvaluatorEmployeeId, x.LastReviewedByEmployeeId })
            .SingleOrDefaultAsync();
        if (previous == null) return true;
        var lastResponsible = previous.LastReviewedByEmployeeId ?? previous.EvaluatorEmployeeId;
        var descendants = await EligibleEvaluatorIdsAsync(periodId, actorId);
        return descendants.Contains(lastResponsible);
    }

    public async Task<List<AssignmentRow>> AssignmentRowsAsync(int periodId, int evaluatorId)
    {
        var subordinateIds = (await EligibleEvaluatorIdsAsync(periodId, evaluatorId)).ToList();
        var raw = await (
            from a in db.PerformanceAssignments.AsNoTracking()
            join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id
            join mapping in db.PerformanceEmployeePositions.AsNoTracking() on e.Id equals mapping.EmployeeId into leftMaps
            from mapping in leftMaps.DefaultIfEmpty()
            join p in db.PerformancePositions.AsNoTracking() on mapping.PositionId equals p.Id into leftPositions
            from p in leftPositions.DefaultIfEmpty()
            where a.PeriodId == periodId && subordinateIds.Contains(a.EvaluatorEmployeeId)
            select new { e.Id, e.PersonnelNumber, e.FirstName, e.LastName, Position = p == null ? e.PositionTitle : p.Title }
        ).OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync();

        var ids = raw.Select(x => x.Id).ToList();
        var evaluations = await db.PerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == periodId && ids.Contains(x.EmployeeId))
            .ToDictionaryAsync(x => x.EmployeeId);

        return raw.Select(e =>
        {
            evaluations.TryGetValue(e.Id, out var ev);
            return new AssignmentRow(e.Id, e.PersonnelNumber, $"{e.FirstName} {e.LastName}",
                e.Position ?? "بدون رده", ev?.Id, ev?.FinalScore ?? 0, ev?.FinalMaxScore ?? 0);
        }).ToList();
    }

    public async Task<bool> HasAssignmentAsync(int periodId, int employeeId, int evaluatorId) =>
        await db.PerformanceAssignments.AnyAsync(x => x.PeriodId == periodId &&
            x.EmployeeId == employeeId && x.EvaluatorEmployeeId == evaluatorId);

    private async Task<List<PerformancePositionQuestion>> GetQuestionsAsync(int employeeId)
    {
        var positionId = await db.PerformanceEmployeePositions.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).Select(x => (int?)x.PositionId).SingleOrDefaultAsync();
        if (positionId == null) return [];

        return await db.PerformancePositionQuestions.AsNoTracking()
            .Include(x => x.Question!).ThenInclude(x => x.Domain)
            .Where(x => x.PositionId == positionId && x.IsActive &&
                x.Question != null && x.Question.IsActive && x.Question.Domain != null && x.Question.Domain.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.QuestionId).ToListAsync();
    }

    public async Task<List<ScoreDisplay>> ScoreRowsAsync(int periodId, int employeeId, int evaluatorId)
    {
        if (!await CanViewAsync(periodId, employeeId, evaluatorId))
            throw new UnauthorizedAccessException("این کارمند در فهرست ارزیابی مجاز شما نیست.");

        var existing = await db.PerformanceEvaluations.AsNoTracking().Include(x => x.Scores)
            .SingleOrDefaultAsync(x => x.PeriodId == periodId && x.EmployeeId == employeeId);
        if (existing != null)
            return existing.Scores.OrderBy(x => x.Id)
                .Select(x => new ScoreDisplay(x.QuestionId, x.QuestionTitleSnapshot, x.DomainTitleSnapshot,
                    x.Score, x.MaxScore, x.Comment)).ToList();

        var qs = await GetQuestionsAsync(employeeId);
        return qs.Select(x => new ScoreDisplay(x.QuestionId, x.Question!.Title,
            x.Question.Domain!.Title, x.MaxScore, x.MaxScore, null)).ToList();
    }

    /// <summary>Quick action only creates missing evaluations. Existing deductions and evaluator identity are preserved.</summary>
    public async Task<(int Created, int AlreadyEvaluated, int WithoutQuestions)> QuickEvaluateAsync(int periodId, int evaluatorId)
    {
        var period = await db.PerformancePeriods.SingleOrDefaultAsync(x => x.Id == periodId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (!IsEditable(period, DateTime.Now))
            throw new InvalidOperationException("دوره برای ثبت ارزیابی باز نیست.");

        var subordinateIds = (await EligibleEvaluatorIdsAsync(periodId, evaluatorId)).ToList();
        var assignments = await db.PerformanceAssignments.Where(x =>
            x.PeriodId == periodId && subordinateIds.Contains(x.EvaluatorEmployeeId)).ToListAsync();
        var existing = (await db.PerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == periodId).Select(x => x.EmployeeId).ToListAsync()).ToHashSet();

        var created = 0;
        var already = 0;
        var skipped = 0;
        foreach (var assignment in assignments)
        {
            if (existing.Contains(assignment.EmployeeId))
            {
                already++;
                continue;
            }

            var questions = await GetQuestionsAsync(assignment.EmployeeId);
            if (questions.Count == 0) { skipped++; continue; }

            var evaluation = new PerformanceEvaluation
            {
                PeriodId = periodId,
                EmployeeId = assignment.EmployeeId,
                EvaluatorEmployeeId = evaluatorId,
                Status = "Submitted",
                UpdatedAt = DateTime.UtcNow
            };
            foreach (var q in questions)
            {
                evaluation.Scores.Add(new PerformanceScore
                {
                    QuestionId = q.QuestionId,
                    MaxScore = q.MaxScore,
                    Score = q.MaxScore,
                    QuestionTitleSnapshot = q.Question!.Title,
                    DomainTitleSnapshot = q.Question.Domain!.Title
                });
            }
            evaluation.FinalMaxScore = evaluation.Scores.Sum(x => x.MaxScore);
            evaluation.FinalScore = evaluation.Scores.Sum(x => x.Score);
            db.PerformanceEvaluations.Add(evaluation);
            created++;
        }
        await db.SaveChangesAsync();
        return (created, already, skipped);
    }

    public async Task SaveAsync(int periodId, int employeeId, int evaluatorId,
        IReadOnlyCollection<ScoreInput> values, string? reason)
    {
        var period = await db.PerformancePeriods.SingleOrDefaultAsync(x => x.Id == periodId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (!IsEditable(period, DateTime.Now))
            throw new InvalidOperationException("دوره بسته است و امکان ویرایش وجود ندارد.");
        if (!await CanEditAsync(periodId, employeeId, evaluatorId))
            throw new UnauthorizedAccessException("این ارزیابی توسط مدیر بالادست ثبت یا بازنگری شده و شما مجاز به ویرایش آن نیستید.");

        var existing = await db.PerformanceEvaluations.Include(x => x.Scores)
            .SingleOrDefaultAsync(x => x.PeriodId == periodId && x.EmployeeId == employeeId);
        var normalized = values.ToList();
        if (normalized.Select(x => x.QuestionId).Distinct().Count() != normalized.Count)
            throw new InvalidOperationException("شناسه سؤال تکراری است.");
        if (normalized.Count == 0)
            throw new InvalidOperationException("سؤالی برای ارزیابی وجود ندارد.");

        if (existing == null)
        {
            var questions = await GetQuestionsAsync(employeeId);
            if (questions.Count == 0 || normalized.Count != questions.Count ||
                !questions.All(x => normalized.Any(v => v.QuestionId == x.QuestionId)))
                throw new InvalidOperationException("فهرست سؤال‌های ارسالی با رده پستی کارمند یکسان نیست.");

            existing = new PerformanceEvaluation
            {
                PeriodId = periodId, EmployeeId = employeeId, EvaluatorEmployeeId = evaluatorId,
                Status = "Submitted", UpdatedAt = DateTime.UtcNow
            };
            foreach (var q in questions)
            {
                var value = normalized.Single(v => v.QuestionId == q.QuestionId);
                if (value.Score < 0 || value.Score > q.MaxScore)
                    throw new InvalidOperationException($"امتیاز «{q.Question!.Title}» خارج از بازه مجاز است.");
                existing.Scores.Add(new PerformanceScore
                {
                    QuestionId = q.QuestionId, Score = value.Score, MaxScore = q.MaxScore,
                    QuestionTitleSnapshot = q.Question!.Title,
                    DomainTitleSnapshot = q.Question.Domain!.Title,
                    Comment = value.Comment?.Trim()
                });
            }
            db.PerformanceEvaluations.Add(existing);
        }
        else
        {
            if (normalized.Count != existing.Scores.Count ||
                !existing.Scores.All(s => normalized.Any(v => v.QuestionId == s.QuestionId)))
                throw new InvalidOperationException("فهرست سؤال‌های ثبت‌شده قابل تغییر نیست.");

            var changed = existing.Scores.Where(s =>
                normalized.Single(x => x.QuestionId == s.QuestionId).Score != s.Score).ToList();
            if (changed.Count > 0 && string.IsNullOrWhiteSpace(reason))
                throw new InvalidOperationException("علت تغییر امتیاز الزامی است.");

            foreach (var score in existing.Scores)
            {
                var value = normalized.Single(x => x.QuestionId == score.QuestionId);
                if (value.Score < 0 || value.Score > score.MaxScore)
                    throw new InvalidOperationException($"امتیاز «{score.QuestionTitleSnapshot}» خارج از بازه مجاز است.");
                if (value.Score != score.Score)
                {
                    db.PerformanceScoreChanges.Add(new PerformanceScoreChange
                    {
                        EvaluationId = existing.Id, QuestionId = score.QuestionId,
                        ChangedByEmployeeId = evaluatorId,
                        OldScore = score.Score, NewScore = value.Score,
                        Reason = reason!.Trim(), ChangedAt = DateTime.UtcNow
                    });
                }
                score.Score = value.Score;
                score.Comment = value.Comment?.Trim();
            }
        }

        if (existing.EvaluatorEmployeeId != evaluatorId)
        {
            existing.LastReviewedByEmployeeId = evaluatorId;
            existing.Status = "Reviewed";
        }
        existing.FinalScore = existing.Scores.Sum(x => x.Score);
        existing.FinalMaxScore = existing.Scores.Sum(x => x.MaxScore);
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
