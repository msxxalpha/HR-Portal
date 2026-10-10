namespace HRPortal.Models;

/// <summary>Latest value for a field within a particular workflow instance and step.</summary>
public class WorkflowFieldValue
{
    public int Id { get; set; }
    public int WorkflowInstanceId { get; set; }
    public int WorkflowStepId { get; set; }
    public int WorkflowStepFieldId { get; set; }
    // Snapshots keep historical labels/types stable if a definition is later edited.
    public string FieldCode { get; set; } = "";
    public string FieldTitle { get; set; } = "";
    public string FieldType { get; set; } = "Text";
    public string? Value { get; set; }
    public int? UpdatedByEmployeeId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public WorkflowInstance WorkflowInstance { get; set; } = null!;
    public WorkflowStep WorkflowStep { get; set; } = null!;
    public WorkflowStepField WorkflowStepField { get; set; } = null!;
    public Employee? UpdatedByEmployee { get; set; }
}
