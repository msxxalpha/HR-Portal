namespace HRPortal.Models;

/// <summary>
/// Evaluation question usage and response snapshot. A null score means the question is reserved for a workflow step but has not been scored yet. The evaluation UI/workflow will be implemented separately.
/// Snapshots preserve the text, guidance and score ceiling used at evaluation time.
/// </summary>
public class EmployeePerformanceEvaluationScore
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public int WorkflowInstanceId { get; set; }
    public int WorkflowStepId { get; set; }
    public int? EvaluatorEmployeeId { get; set; }
    public decimal? Score { get; set; }
    public decimal MaxScoreSnapshot { get; set; }
    public string QuestionTextSnapshot { get; set; } = "";
    public string ScoringGuidanceSnapshot { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public EmployeePerformanceEvaluationQuestion Question { get; set; } = null!;
    public WorkflowInstance WorkflowInstance { get; set; } = null!;
    public WorkflowStep WorkflowStep { get; set; } = null!;
    public Employee? EvaluatorEmployee { get; set; }
}
