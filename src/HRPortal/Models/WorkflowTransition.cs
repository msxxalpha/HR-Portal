namespace HRPortal.Models;

public class WorkflowTransition
{
    public int Id { get; set; }
    public int WorkflowStepId { get; set; }
    public int? ToStepId { get; set; }
    public string Action { get; set; } = "Approve";
    public string? Title { get; set; }
    public string? ConditionFieldCode { get; set; }
    public string ConditionOperator { get; set; } = "Equals";
    public string? ConditionValue { get; set; }
    public WorkflowStep WorkflowStep { get; set; } = null!;
    public WorkflowStep? ToStep { get; set; }
}
