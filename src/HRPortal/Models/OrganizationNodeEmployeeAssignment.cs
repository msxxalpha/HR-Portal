namespace HRPortal.Models;

public class OrganizationNodeEmployeeAssignment
{
    public int Id { get; set; }
    public int OrganizationNodeId { get; set; }
    public int EmployeeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public OrganizationNode OrganizationNode { get; set; } = null!;
    public Employee Employee { get; set; } = null!;
}
