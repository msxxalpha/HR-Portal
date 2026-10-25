using System.ComponentModel.DataAnnotations;

namespace HRPortal.Modules.Performance;

public sealed class PerformanceDomain
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = "";
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(1000)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
public sealed class PerformanceQuestion
{
    public int Id { get; set; }
    public int DomainId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = "";
    [Required, MaxLength(250)] public string Title { get; set; } = "";
    [Required, MaxLength(2000)] public string Text { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public PerformanceDomain? Domain { get; set; }
}
public sealed class PerformancePosition
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = "";
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public sealed class PerformancePositionQuestion
{
    public int Id { get; set; }
    public int PositionId { get; set; }
    public int QuestionId { get; set; }
    public decimal MaxScore { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 1;
    public PerformancePosition? Position { get; set; }
    public PerformanceQuestion? Question { get; set; }
}
/// <summary>Position catalog is separate; employee identity remains HRPortal.Employee.Id.</summary>
public sealed class PerformanceEmployeePosition
{
    [System.ComponentModel.DataAnnotations.Key] public int EmployeeId { get; set; }
    public int PositionId { get; set; }
    public PerformancePosition? Position { get; set; }
}
public sealed class PerformancePeriod
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public int OrganizationStructureRevisionId { get; set; }
    public bool IsOpen { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
/// <summary>Explicit evaluator assignment; never infer reporting line solely from the org unit.</summary>
public sealed class PerformanceAssignment
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int EmployeeId { get; set; }
    public int EvaluatorEmployeeId { get; set; }
    public PerformancePeriod? Period { get; set; }
}
/// <summary>
/// Period-specific reviewer hierarchy configured by HR administrator.
/// Must not be inferred from shared department membership.
/// </summary>
public sealed class PerformanceEvaluatorChain
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int SubordinateEvaluatorEmployeeId { get; set; }
    public int SuperiorEvaluatorEmployeeId { get; set; }
}
public sealed class PerformanceEvaluation
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int EmployeeId { get; set; }
    public int EvaluatorEmployeeId { get; set; }
    public int? LastReviewedByEmployeeId { get; set; }
    public decimal FinalScore { get; set; }
    public decimal FinalMaxScore { get; set; }
    public string Status { get; set; } = "Submitted";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public PerformancePeriod? Period { get; set; }
    public List<PerformanceScore> Scores { get; set; } = [];
}
public sealed class PerformanceScore
{
    public int Id { get; set; }
    public int EvaluationId { get; set; }
    public int QuestionId { get; set; }
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    [Required, MaxLength(250)] public string QuestionTitleSnapshot { get; set; } = "";
    [Required, MaxLength(200)] public string DomainTitleSnapshot { get; set; } = "";
    [MaxLength(2000)] public string? Comment { get; set; }
    public PerformanceEvaluation? Evaluation { get; set; }
}
public sealed class PerformanceScoreChange
{
    public long Id { get; set; }
    public int EvaluationId { get; set; }
    public int QuestionId { get; set; }
    public int ChangedByEmployeeId { get; set; }
    public decimal OldScore { get; set; }
    public decimal NewScore { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = "";
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
