namespace HRPortal.Models;

public class WorkflowHistory
{
    public int Id { get; set; }
    public int WorkflowInstanceId { get; set; }
    public int? WorkflowStepId { get; set; }
    public string FromStatus { get; set; } = "";
    public string ToStatus { get; set; } = "";
    public string Action { get; set; } = "";
    public int? ActorEmployeeId { get; set; }
    public int? ActorPositionId { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public WorkflowInstance WorkflowInstance { get; set; } = null!;
    public WorkflowStep? WorkflowStep { get; set; }
    public Employee? ActorEmployee { get; set; }
    public OrganizationNode? ActorPosition { get; set; }
}
