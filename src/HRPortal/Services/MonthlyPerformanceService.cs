using System.Globalization;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class MonthlyPerformanceService(HRPortalDbContext db)
{
    private static readonly TimeZoneInfo IranZone = ResolveIranZone();
    private static TimeZoneInfo ResolveIranZone()
    {
        foreach (var id in new[] { "Iran Standard Time", "Asia/Tehran" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new InvalidOperationException("منطقه زمانی ایران در سرور قابل شناسایی نیست.");
    }
    public static DateTime NowIran() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IranZone);
    public static DateTime ToIranTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), IranZone);

    public static bool IsOpen(MonthlyPerformancePeriod p, DateTime now) =>
        !p.IsDeleted && p.IsEnabled && p.StartAt <= now && now < p.EndAt;

    public static string PeriodState(MonthlyPerformancePeriod p, DateTime now) =>
        p.IsDeleted ? "حذف‌شده / بایگانی" : !p.IsEnabled ? "غیرفعال" : now >= p.EndAt ? "پایان‌یافته" :
        now < p.StartAt ? "در انتظار شروع" : "فعال";

    public static (DateTime start, DateTime endExclusive) ParseWindow(string from, string to)
    {
        if (!PersianDateService.TryParse(from, out var start) ||
            !PersianDateService.TryParse(to, out var end) || end < start)
            throw new InvalidOperationException("بازه تاریخ شمسی دوره معتبر نیست.");
        return (start, end.AddDays(1));
    }

    public async Task<List<MonthlyPerformancePeriod>> PeriodsAsync() =>
        await db.MonthlyPerformancePeriods.AsNoTracking()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync();

    public async Task<MonthlyPerformancePeriod?> ActivePeriodAsync()
    {
        var now = NowIran();
        return await db.MonthlyPerformancePeriods.AsNoTracking()
            .Where(p => !p.IsDeleted && p.IsEnabled && p.StartAt <= now && now < p.EndAt)
            .OrderByDescending(p => p.StartAt).FirstOrDefaultAsync();
    }

    private sealed record Node(int Id, int? Parent, string Rank, int? Holder, string? Csv);
    private sealed record Person(int Id, string Status, int? Position, int? Section, int? Department, int? Unit);
    public sealed record ScopeEntry(int EmployeeId, string RankTitle, int NodeId, int Depth);

    public async Task<List<ScopeEntry>> ScopeAsync(int evaluatorId)
    {
        var employees = await db.Employees.AsNoTracking()
            .Select(e => new Person(e.Id, e.Status, e.OrganizationPositionId,
                e.OrganizationSectionId, e.OrganizationDepartmentId, e.OrganizationUnitId))
            .ToListAsync();
        if (employees.All(x => x.Id != evaluatorId || x.Status != "فعال")) return [];

        var today = NowIran().Date;
        var revisionId = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync();
        if (!revisionId.HasValue) return [];
        var nodes = (await db.OrganizationNodes.AsNoTracking()
            .Where(n => n.IsActive && n.OrganizationStructureRevisionId == revisionId.Value)
            .Select(n => new Node(n.Id, n.ParentId, n.RankType, n.PositionHolderEmployeeId, n.AssignedEmployeeIdsCsv))
            .ToListAsync()).ToDictionary(n => n.Id);
        var byPerson = new Dictionary<int, List<int>>();
        foreach (var person in employees.Where(x => x.Status == "فعال"))
        {
            var choices = new HashSet<int>();
            foreach (var n in nodes.Values)
            {
                if (n.Holder == person.Id ||
                    (n.Csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Any(s => int.TryParse(s, out var id) && id == person.Id))
                    choices.Add(n.Id);
            }
            foreach (var assigned in new[] { person.Position, person.Section, person.Department, person.Unit })
                if (assigned.HasValue && nodes.ContainsKey(assigned.Value)) choices.Add(assigned.Value);
            byPerson[person.Id] = choices.ToList();
        }

        // Take the deepest assigned organization node as the employee's effective placement.
        int Depth(int id)
        {
            var depth = 0;
            var visited = new HashSet<int>();
            while (nodes.TryGetValue(id, out var n) && visited.Add(id) && n.Parent.HasValue)
            { depth++; id = n.Parent.Value; }
            return visited.Count > nodes.Count ? 0 : depth;
        }
        var placements = byPerson.ToDictionary(pair => pair.Key, pair =>
            pair.Value.OrderByDescending(Depth).FirstOrDefault());
        if (!placements.TryGetValue(evaluatorId, out var evaluatorNodeId) || evaluatorNodeId == 0)
            return [];
        var evaluatorNode = nodes[evaluatorNodeId];
        bool IsDescendantOrPeer(int id)
        {
            if (id == evaluatorNodeId) return true; // colleagues in the same node
            if (!nodes.TryGetValue(id, out var node)) return false;
            // Same-level peers are limited to siblings under the same direct parent.
            if (evaluatorNode.Parent.HasValue && node.Parent == evaluatorNode.Parent && node.Rank == evaluatorNode.Rank)
                return true;
            var visited = new HashSet<int>();
            while (nodes.TryGetValue(id, out var step) && visited.Add(id) && step.Parent.HasValue)
            {
                if (step.Parent.Value == evaluatorNodeId) return true;
                id = step.Parent.Value;
            }
            return false;
        }
        return placements.Where(x => x.Key != evaluatorId && x.Value != 0 && IsDescendantOrPeer(x.Value))
            .Select(x => new ScopeEntry(x.Key, nodes[x.Value].Rank, x.Value, Depth(x.Value))).ToList();
    }

    public async Task<bool> IsEvaluatorAsync(int actor) =>
        await db.Employees.AsNoTracking().AnyAsync(e => e.Id == actor && e.Status == "فعال" && e.IsPerformanceEvaluator);

    public async Task<bool> CanEvaluateAsync(int actor, int target) =>
        await IsEvaluatorAsync(actor) && (await ScopeAsync(actor)).Any(x => x.EmployeeId == target);

    public async Task<bool> IsAboveAsync(int actor, int otherEvaluator)
    {
        // An upper evaluator can also assess the evaluator immediately below them.
        var subordinates = await ScopeAsync(actor);
        if (!subordinates.Any(x => x.EmployeeId == otherEvaluator)) return false;
        var reverse = await ScopeAsync(otherEvaluator);
        return !reverse.Any(x => x.EmployeeId == actor);
    }

    public async Task<MonthlyPerformanceEvaluation?> GetEvaluationAsync(int periodId, int employeeId) =>
        await db.MonthlyPerformanceEvaluations.Include(x => x.Scores)
            .FirstOrDefaultAsync(x => x.PeriodId == periodId && x.EmployeeId == employeeId);

    public async Task<MonthlyPerformanceEvaluation> SaveAsync(
        int actorId, int employeeId, int periodId, IDictionary<int, decimal>? enteredScores,
        IDictionary<int, string?>? notes, string? reason, bool onlyWhenMissing = false)
    {
        if (!await CanEvaluateAsync(actorId, employeeId))
            throw new UnauthorizedAccessException("اجازه ارزیابی این کارمند را ندارید.");
        var period = await db.MonthlyPerformancePeriods.FirstOrDefaultAsync(x => x.Id == periodId)
            ?? throw new InvalidOperationException("دوره موردنظر یافت نشد.");
        if (!IsOpen(period, DateTime.Now))
            throw new InvalidOperationException("دوره فعال نیست یا بازه زمانی ثبت ارزیابی پایان یافته است.");

        var scope = await ScopeAsync(actorId);
        var placement = scope.Single(x => x.EmployeeId == employeeId);
        var evaluation = await GetEvaluationAsync(periodId, employeeId);
        if (evaluation is not null)
        {
            if (onlyWhenMissing) return evaluation;
            if (evaluation.LastEvaluatorId != actorId &&
                !await IsAboveAsync(actorId, evaluation.LastEvaluatorId))
                throw new UnauthorizedAccessException("ویرایش امتیاز ارزیاب دیگر فقط برای ارزیاب بالادست مجاز است.");
        }

        var mappings = await db.MonthlyPerformanceRankQuestions.AsNoTracking()
            .Where(x => x.IsActive && x.Rank.IsActive && x.Rank.Title == placement.RankTitle && x.Question.IsActive && x.Question.Domain.IsActive)
            .Include(x => x.Question).ThenInclude(x => x.Domain)
            .OrderBy(x => x.Question.SortOrder).ThenBy(x => x.QuestionId).ToListAsync();
        if (mappings.Count == 0 && evaluation is null)
            throw new InvalidOperationException($"برای رده «{placement.RankTitle}» سؤال و سقف امتیاز فعال تعریف نشده است.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        var isNew = evaluation is null;
        if (evaluation is null)
        {
            evaluation = new MonthlyPerformanceEvaluation
            {
                PeriodId = periodId, EmployeeId = employeeId, OriginalEvaluatorId = actorId,
                LastEvaluatorId = actorId, RankTitleSnapshot = placement.RankTitle,
                MaxScore = mappings.Sum(x => x.MaxScore), TotalScore = mappings.Sum(x => x.MaxScore)
            };
            foreach (var mapping in mappings)
                evaluation.Scores.Add(new MonthlyPerformanceScore
                {
                    QuestionId = mapping.QuestionId, QuestionTitleSnapshot = mapping.Question.Title,
                    DomainTitleSnapshot = mapping.Question.Domain.Title,
                    MaxScore = mapping.MaxScore, Score = mapping.MaxScore
                });
            db.MonthlyPerformanceEvaluations.Add(evaluation);
            await db.SaveChangesAsync();
        }

        var previousEvaluatorId = evaluation.LastEvaluatorId;
        var updates = new List<MonthlyPerformanceRevision>();
        if (isNew)
            updates.AddRange(evaluation.Scores.Select(s => new MonthlyPerformanceRevision
            {
                EvaluationId = evaluation.Id, QuestionId = s.QuestionId,
                ChangedByEmployeeId = actorId, PreviousEvaluatorId = actorId,
                OldScore = null, NewScore = s.Score, NewNote = s.Note,
                Reason = string.IsNullOrWhiteSpace(reason) ? "ثبت اولیه امتیاز کامل" : reason.Trim()[..Math.Min(500, reason.Trim().Length)]
            }));
        foreach (var score in evaluation.Scores)
        {
            var next = enteredScores != null && enteredScores.TryGetValue(score.QuestionId, out var chosen)
                ? chosen : score.Score;
            if (next < 0 || next > score.MaxScore)
                throw new InvalidOperationException($"امتیاز «{score.QuestionTitleSnapshot}» باید بین صفر و {score.MaxScore} باشد.");
            var note = notes != null && notes.TryGetValue(score.QuestionId, out var value)
                ? value?.Trim() : score.Note;
            if (note?.Length > 1000) throw new InvalidOperationException("توضیحات سؤال بیش از حد مجاز است.");
            if (score.Score == next && score.Note == note) continue;
            updates.Add(new MonthlyPerformanceRevision
            {
                EvaluationId = evaluation.Id, QuestionId = score.QuestionId,
                ChangedByEmployeeId = actorId, PreviousEvaluatorId = previousEvaluatorId,
                OldScore = score.Score, NewScore = next, OldNote = score.Note, NewNote = note,
                Reason = string.IsNullOrWhiteSpace(reason) ? "اصلاح امتیاز" : reason.Trim()[..Math.Min(500, reason.Trim().Length)]
            });
            score.Score = next; score.Note = note;
        }
        if (!isNew && previousEvaluatorId != actorId && updates.Count == 0 &&
            evaluation.Scores.Count > 0)
        {
            var firstScore = evaluation.Scores.OrderBy(x => x.QuestionId).First();
            updates.Add(new MonthlyPerformanceRevision
            {
                EvaluationId = evaluation.Id, QuestionId = firstScore.QuestionId,
                ChangedByEmployeeId = actorId, PreviousEvaluatorId = previousEvaluatorId,
                OldScore = firstScore.Score, NewScore = firstScore.Score,
                OldNote = firstScore.Note, NewNote = firstScore.Note,
                Reason = "بازبینی ارزیابی توسط مقام بالادست، بدون تغییر امتیاز"
            });
        }
        if (updates.Count > 0) await db.MonthlyPerformanceRevisions.AddRangeAsync(updates);
        evaluation.TotalScore = evaluation.Scores.Sum(x => x.Score);
        evaluation.MaxScore = evaluation.Scores.Sum(x => x.MaxScore);
        evaluation.LastEvaluatorId = actorId;
        evaluation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return evaluation;
    }

    public async Task<(int created, int skipped, int errors)> QuickAllAsync(int actorId, int periodId)
    {
        if (!await IsEvaluatorAsync(actorId)) throw new UnauthorizedAccessException("فقط ارزیاب مجاز است.");
        var period = await db.MonthlyPerformancePeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodId)
            ?? throw new InvalidOperationException("دوره یافت نشد.");
        if (!IsOpen(period, DateTime.Now)) throw new InvalidOperationException("دوره فعال نیست.");
        var scope = await ScopeAsync(actorId);
        var existing = (await db.MonthlyPerformanceEvaluations.AsNoTracking()
            .Where(x => x.PeriodId == periodId).Select(x => x.EmployeeId).ToListAsync()).ToHashSet();
        var created = 0; var skipped = 0; var errors = 0;
        foreach (var employee in scope)
        {
            if (existing.Contains(employee.EmployeeId)) { skipped++; continue; }
            try { await SaveAsync(actorId, employee.EmployeeId, periodId, null, null, "ثبت گروهی امتیاز کامل", true); created++; }
            catch (InvalidOperationException) { errors++; }
            catch (DbUpdateException) { errors++; db.ChangeTracker.Clear(); }
        }
        return (created, skipped, errors);
    }
}
