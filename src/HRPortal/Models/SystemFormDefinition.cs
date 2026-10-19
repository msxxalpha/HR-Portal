namespace HRPortal.Models;

public class SystemFormDefinition
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    // Custom: independently implemented process; GenericWorkflow: compatible with the shared engine;
    // Standalone: a simple independent form without the shared workflow engine.
    public string ImplementationType { get; set; } = "Standalone";
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
