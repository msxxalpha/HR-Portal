namespace HRPortal.Models;

public class WorkflowDefinitionEditModel
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
