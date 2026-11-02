namespace HRPortal.Models;

public class WorkflowTask
{
    public int Id { get; set; }
    public int WorkflowInstanceId { get; set; }
    public int WorkflowStepId { get; set; }
    public int? AssignedPositionId { get; set; }
    public int? AssignedEmployeeId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Action { get; set; }
    public string? Comment { get; set; }
    public WorkflowInstance WorkflowInstance { get; set; } = null!;
    public WorkflowStep WorkflowStep { get; set; } = null!;
    public OrganizationNode? AssignedPosition { get; set; }
    public Employee? AssignedEmployee { get; set; }
}
