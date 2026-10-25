namespace HRPortal.Models;

public class MonthlyPerformanceDashboardVm
{
    public bool IsEvaluator { get; set; }
    public bool CanEdit { get; set; }
    public int? PeriodId { get; set; }
    public string Message { get; set; } = "";
    public List<MonthlyPerformancePeriod> Periods { get; set; } = [];
    public MonthlyPerformancePeriod? SelectedPeriod { get; set; }
    public List<MonthlyPerformancePersonRow> People { get; set; } = [];
    public List<MonthlyPerformanceQuestionAverage> QuestionAverages { get; set; } = [];
    public decimal TotalCapacity { get; set; }
    public decimal UsedScore { get; set; }
    public decimal DeductedScore => TotalCapacity - UsedScore;
    public decimal AveragePercent => People.Count(x => x.MaxScore > 0) == 0 ? 0 :
        Math.Round(People.Where(x => x.MaxScore > 0).Average(x => x.Score / x.MaxScore * 100m), 1);
}
public class MonthlyPerformancePersonRow
{
    public int EmployeeId { get; set; }
    public int? EvaluationId { get; set; }
    public string PersonnelNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Rank { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public decimal Deducted => MaxScore - Score;
    public decimal Percent => MaxScore <= 0 ? 0 : Math.Round(Score * 100m / MaxScore, 1);
    public string EvaluatorName { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public bool CanEdit { get; set; }
}
public class MonthlyPerformanceQuestionAverage
{
    public string Title { get; set; } = "";
    public string Domain { get; set; } = "";
    public decimal AveragePercent { get; set; }
}
public class MonthlyPerformanceEvaluationVm
{
    public MonthlyPerformancePeriod Period { get; set; } = null!;
    public Employee Employee { get; set; } = null!;
    public string RankTitle { get; set; } = "";
    public bool CanEdit { get; set; }
    public int? EvaluationId { get; set; }
    public string? EvaluationVersion { get; set; }
    public int? OriginalEvaluatorId { get; set; }
    public int? LastEvaluatorId { get; set; }
    public List<MonthlyPerformanceScoreVm> Scores { get; set; } = [];
    public List<MonthlyPerformanceRevisionVm> History { get; set; } = [];
    public decimal MaxScore => Scores.Sum(x => x.MaxScore);
    public decimal TotalScore => Scores.Sum(x => x.Score);
}
public class MonthlyPerformanceScoreVm
{
    public int QuestionId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Domain { get; set; } = "";
    public decimal MaxScore { get; set; }
    public decimal Score { get; set; }
    public string? Note { get; set; }
}
public class MonthlyPerformanceRevisionVm
{
    public string Question { get; set; } = "";
    public string Actor { get; set; } = "";
    public decimal? Before { get; set; }
    public decimal After { get; set; }
    public string? OldNote { get; set; }
    public string? NewNote { get; set; }
    public string? Reason { get; set; }
    public DateTime At { get; set; }
}
public class MonthlyPerformanceBasicVm
{
    public List<MonthlyPerformancePeriod> Periods { get; set; } = [];
    public List<MonthlyPerformanceRank> Ranks { get; set; } = [];
    public List<MonthlyPerformanceDomain> Domains { get; set; } = [];
    public List<MonthlyPerformanceQuestion> Questions { get; set; } = [];
    public List<MonthlyPerformanceRankQuestion> Mappings { get; set; } = [];
    public List<MonthlyPerformanceEmployeeVm> Evaluators { get; set; } = [];
    public List<OrganizationNode> Units { get; set; } = [];
    public string Tab { get; set; } = "domains";
    public int? EditPeriodId { get; set; }
    public MonthlyPerformancePeriod? EditPeriod { get; set; }
}
public class MonthlyPerformanceEmployeeVm
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsEvaluator { get; set; }
}
