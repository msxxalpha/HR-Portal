namespace HRPortal.Models;

public class WorkflowInboxRow
{
    public int TaskId { get; set; }
    public int WorkflowInstanceId { get; set; }
    public int WorkflowStepId { get; set; }
    public int StepSortOrder { get; set; }
    public bool IsUpcomingPreview { get; set; }
    public bool CanAct { get; set; }
    public string WorkflowTitle { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string RequesterName { get; set; } = "";
    public string RequesterPersonnelNumber { get; set; } = "";
    public string StepTitle { get; set; } = "";
    public string PositionTitle { get; set; } = "";
    public string CurrentStepTitle { get; set; } = "";
    public string CurrentAssigneeName { get; set; } = "";
    public string LatestWorkflowStatus { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? Comment { get; set; }
    public string Action { get; set; } = "";
    public bool CanDelete { get; set; }
}
