namespace HRPortal.Models;

public class WorkflowInstance
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public int RequesterEmployeeId { get; set; }
    public int? CurrentStepId { get; set; }
    public string Status { get; set; } = "InProgress";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public Employee RequesterEmployee { get; set; } = null!;
    public WorkflowStep? CurrentStep { get; set; }
    public ICollection<WorkflowTask> Tasks { get; set; } = new List<WorkflowTask>();
    public ICollection<WorkflowHistory> History { get; set; } = new List<WorkflowHistory>();
}
