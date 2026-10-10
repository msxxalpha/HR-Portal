namespace HRPortal.Models;

public class WorkflowStepEditModel
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
    public int? OrganizationNodeId { get; set; }
    public string AssignmentType { get; set; } = "Position";
    public string HierarchyStopRankType { get; set; } = "معاونت";
    public string AssignmentMode { get; set; } = "Any";
    public bool AllowApprove { get; set; } = true;
    public bool AllowReject { get; set; } = true;
    public bool AllowReturn { get; set; } = true;
    public bool RequireCommentOnReject { get; set; } = true;
    public bool RequireCommentOnReturn { get; set; }
    public bool IsFinalStep { get; set; }
}
