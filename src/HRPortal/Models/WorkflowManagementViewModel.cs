namespace HRPortal.Models;

public class WorkflowManagementViewModel
{
    public List<WorkflowDefinition> Definitions { get; set; } = [];
    public WorkflowDefinition? SelectedDefinition { get; set; }
    public List<OrganizationNode> Positions { get; set; } = [];
}
