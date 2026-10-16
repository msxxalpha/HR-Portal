namespace HRPortal.Models;

public class WorkflowStepField
{
    public int Id { get; set; }
    public int WorkflowStepId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string FieldType { get; set; } = "Text";
    public string? Options { get; set; }
    public string? HelpText { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public int? MaxLength { get; set; }
    public WorkflowStep WorkflowStep { get; set; } = null!;
}
