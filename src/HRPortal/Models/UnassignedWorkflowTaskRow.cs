namespace HRPortal.Models;

public class UnassignedWorkflowTaskRow
{
    public int TaskId { get; set; }
    public int WorkflowInstanceId { get; set; }
    public int WorkflowId { get; set; }
    public string WorkflowTitle { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string RequesterName { get; set; } = "";
    public string StepTitle { get; set; } = "";
    public string PositionCode { get; set; } = "";
    public string PositionTitle { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string ReasonCode { get; set; } = "";
    public string Reason { get; set; } = "";
}
