namespace HRPortal.Models;

public class ContractRenewalWorkflowSettings
{
    public int Id { get; set; } = 1;
    public int? RequestApprovalPositionId { get; set; }
    public int? SecurityPositionId { get; set; }
    public int? HumanResourcesPositionId { get; set; }
    public int? CeoEmployeeId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public OrganizationNode? RequestApprovalPosition { get; set; }
    public OrganizationNode? SecurityPosition { get; set; }
    public OrganizationNode? HumanResourcesPosition { get; set; }
    public Employee? CeoEmployee { get; set; }
}
