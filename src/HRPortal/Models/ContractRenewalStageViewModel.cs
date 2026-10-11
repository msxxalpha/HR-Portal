namespace HRPortal.Models;

public class ContractRenewalStageViewModel
{
    public WorkflowTask Task { get; set; } = null!;
    public ContractRenewalRequest Request { get; set; } = null!;
    public Employee SubjectEmployee { get; set; } = null!;
    public string StageKind { get; set; } = "";
    public List<EmployeePerformanceEvaluationScore> CurrentScores { get; set; } = [];
    public List<EmployeePerformanceEvaluationScore> PreviousStageScores { get; set; } = [];
    public List<ContractRenewalScoreStageGroup> AllScoreStages { get; set; } = [];
    public decimal CurrentQuestionCeilingTotal { get; set; }
    public decimal? PreviousStageScoreTotal { get; set; }
    public int? SuggestedContractMonths { get; set; }
}
public class ContractRenewalScoreStageGroup
{
    public string StageTitle { get; set; } = "";
    public string PositionTitle { get; set; } = "";
    public string? Comment { get; set; }
    public decimal ScoreTotal { get; set; }
    public List<EmployeePerformanceEvaluationScore> Scores { get; set; } = [];
}
