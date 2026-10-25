using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

// Kept separate from the contract-renewal evaluation entities.
public class MonthlyPerformancePeriod
{
    public int Id { get; set; }
    [MaxLength(100)] public string Code { get; set; } = "";
    [MaxLength(150)] public string Title { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }  // exclusive upper bound (day after the chosen final Jalali day)
    public bool IsEnabled { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByAdminId { get; set; }
    [MaxLength(500)] public string? DeletionReason { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class MonthlyPerformanceDomain
{
    public int Id { get; set; }
    [MaxLength(50)] public string Code { get; set; } = "";
    [MaxLength(200)] public string Title { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
public class MonthlyPerformanceRank
{
    public int Id { get; set; }
    [MaxLength(50)] public string Code { get; set; } = "";
    [MaxLength(100)] public string Title { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public class MonthlyPerformanceQuestion
{
    public int Id { get; set; }
    [MaxLength(50)] public string Code { get; set; } = "";
    [MaxLength(500)] public string Title { get; set; } = "";
    [MaxLength(1000)] public string? Text { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public int DomainId { get; set; }
    public MonthlyPerformanceDomain Domain { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
public class MonthlyPerformanceRankQuestion
{
    public int Id { get; set; }
    public int RankId { get; set; }
    public int QuestionId { get; set; }
    public decimal MaxScore { get; set; }
    public int SortOrder { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public MonthlyPerformanceRank Rank { get; set; } = null!;
    public MonthlyPerformanceQuestion Question { get; set; } = null!;
}
public class MonthlyPerformanceEvaluation
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public int EmployeeId { get; set; }
    public int OriginalEvaluatorId { get; set; }
    public int LastEvaluatorId { get; set; }
    [MaxLength(100)] public string RankTitleSnapshot { get; set; } = "";
    public decimal MaxScore { get; set; }
    public decimal TotalScore { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public MonthlyPerformancePeriod Period { get; set; } = null!;
    public Employee Employee { get; set; } = null!;
    public List<MonthlyPerformanceScore> Scores { get; set; } = [];
}
public class MonthlyPerformanceScore
{
    public int Id { get; set; }
    public int EvaluationId { get; set; }
    public int QuestionId { get; set; }
    [MaxLength(500)] public string QuestionTitleSnapshot { get; set; } = "";
    [MaxLength(200)] public string DomainTitleSnapshot { get; set; } = "";
    public decimal MaxScore { get; set; }
    public decimal Score { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    public MonthlyPerformanceEvaluation Evaluation { get; set; } = null!;
    public MonthlyPerformanceQuestion Question { get; set; } = null!;
}
public class MonthlyPerformanceRevision
{
    public long Id { get; set; }
    public int EvaluationId { get; set; }
    public int QuestionId { get; set; }
    public int ChangedByEmployeeId { get; set; }
    public int PreviousEvaluatorId { get; set; }
    public decimal? OldScore { get; set; } // null: initial creation of score
    public decimal NewScore { get; set; }
    [MaxLength(1000)] public string? OldNote { get; set; }
    [MaxLength(1000)] public string? NewNote { get; set; }
    [MaxLength(500)] public string Reason { get; set; } = "";
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
