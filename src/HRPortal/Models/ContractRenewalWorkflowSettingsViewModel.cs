namespace HRPortal.Models;

public class ContractRenewalWorkflowSettingsViewModel
{
    public ContractRenewalWorkflowSettings Settings { get; set; } = new();
    public List<OrganizationNode> Positions { get; set; } = [];
    public List<Employee> ActiveEmployees { get; set; } = [];
    public string? ErrorMessage { get; set; }
}
