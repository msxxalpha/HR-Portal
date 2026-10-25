using HRPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Modules.Performance;

/// <summary>Analytics are computed from stored score snapshots, never from mutable question mappings.</summary>
public sealed class PerformanceAnalyticsService(HRPortalDbContext db)
{
    public sealed record Metric(string Title, int Count, decimal Earned, decimal Maximum, decimal Percentage);
    public sealed record Distribution(string Title, int Count);
    public sealed record Analytics(
        List<Metric> Domains, List<Metric> Questions, List<Metric> Units,
        List<Distribution> Distribution, List<Metric> Highest, List<Metric> Lowest)
    {
        public static Analytics Empty => new([], [], [], [], [], []);
    }

    private static decimal Percent(decimal earned, decimal maximum) =>
        maximum > 0 ? Math.Round(earned * 100m / maximum, 2) : 0m;

    public async Task<Analytics> BuildAsync(int periodId)
    {
        var people = await (
            from e in db.PerformanceEvaluations.AsNoTracking()
            join emp in db.Employees.AsNoTracking() on e.EmployeeId equals emp.Id
            where e.PeriodId == periodId
            select new
            {
                e.Id, e.FinalScore, e.FinalMaxScore,
                emp.FirstName, emp.LastName, emp.OrganizationUnitId,
                emp.OrganizationDepartmentId, emp.OrganizationSectionId
            }
        ).ToListAsync();

        if (people.Count == 0) return Analytics.Empty;

        var evalIds = people.Select(x => x.Id).ToList();
        var scores = await db.PerformanceScores.AsNoTracking()
            .Where(x => evalIds.Contains(x.EvaluationId))
            .Select(x => new { x.QuestionTitleSnapshot, x.DomainTitleSnapshot, x.Score, x.MaxScore })
            .ToListAsync();

        static List<Metric> Group(IEnumerable<(string Label, decimal Score, decimal Max)> values) =>
            values.GroupBy(x => x.Label)
                .Select(g => new Metric(g.Key, g.Count(), g.Sum(x => x.Score), g.Sum(x => x.Max),
                    Percent(g.Sum(x => x.Score), g.Sum(x => x.Max))))
                .OrderByDescending(x => x.Count).ThenBy(x => x.Title).ToList();

        var domains = Group(scores.Select(x => (x.DomainTitleSnapshot, x.Score, x.MaxScore)));
        var questions = Group(scores.Select(x => (x.QuestionTitleSnapshot, x.Score, x.MaxScore)));

        var unitIds = people
            .Select(x => x.OrganizationSectionId ?? x.OrganizationDepartmentId ?? x.OrganizationUnitId)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var unitMap = await db.OrganizationNodes.AsNoTracking()
            .Where(x => unitIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title);

        var units = Group(people.Select(x =>
        {
            var id = x.OrganizationSectionId ?? x.OrganizationDepartmentId ?? x.OrganizationUnitId;
            var label = id.HasValue && unitMap.TryGetValue(id.Value, out var name)
                ? name : "واحد نامشخص";
            return (label, x.FinalScore, x.FinalMaxScore);
        }));

        var distribution = new List<Distribution>
        {
            new("ضعیف (کمتر از ۵۰٪)", people.Count(x => Percent(x.FinalScore, x.FinalMaxScore) < 50)),
            new("نیازمند بهبود (۵۰ تا ۷۰٪)", people.Count(x => Percent(x.FinalScore, x.FinalMaxScore) >= 50 && Percent(x.FinalScore, x.FinalMaxScore) < 70)),
            new("مطلوب (۷۰ تا ۸۵٪)", people.Count(x => Percent(x.FinalScore, x.FinalMaxScore) >= 70 && Percent(x.FinalScore, x.FinalMaxScore) < 85)),
            new("عالی (۸۵٪ و بیشتر)", people.Count(x => Percent(x.FinalScore, x.FinalMaxScore) >= 85))
        };

        var peopleMetrics = people.Select(x => new Metric($"{x.FirstName} {x.LastName}",
            1, x.FinalScore, x.FinalMaxScore, Percent(x.FinalScore, x.FinalMaxScore))).ToList();

        return new Analytics(domains, questions, units, distribution,
            peopleMetrics.OrderByDescending(x => x.Percentage).ThenBy(x => x.Title).Take(10).ToList(),
            peopleMetrics.OrderBy(x => x.Percentage).ThenBy(x => x.Title).Take(10).ToList());
    }
}
