namespace HRPortal.Models;

public class WorkflowTaskFieldGroup
{
    public int WorkflowStepId { get; set; }
    public string StepTitle { get; set; } = "";
    public bool IsCurrentStep { get; set; }
    public List<WorkflowTaskFieldItem> Fields { get; set; } = [];
}

public class WorkflowTaskFieldItem
{
    public int FieldDefinitionId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string FieldType { get; set; } = "Text";
    public string? Options { get; set; }
    public string? HelpText { get; set; }
    public int? MaxLength { get; set; }
    public bool IsRequired { get; set; }
    public string? Value { get; set; }
}
