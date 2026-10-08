namespace HRPortal.Models;

public class WorkflowStep
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
    public string AssignmentType { get; set; } = "Position";
    public int? OrganizationNodeId { get; set; }
    public string AssignmentMode { get; set; } = "Any";
    public bool AllowApprove { get; set; } = true;
    public bool AllowReject { get; set; } = true;
    public bool AllowReturn { get; set; } = true;
    public bool RequireCommentOnReject { get; set; } = true;
    public bool RequireCommentOnReturn { get; set; } = false;
    public bool IsFinalStep { get; set; }
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public OrganizationNode? OrganizationNode { get; set; }
    public ICollection<WorkflowTransition> OutgoingTransitions { get; set; } = new List<WorkflowTransition>();
}
