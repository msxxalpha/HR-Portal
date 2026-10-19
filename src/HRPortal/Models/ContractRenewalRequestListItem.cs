namespace HRPortal.Models;

public class ContractRenewalRequestListItem
{
    public ContractRenewalRequest Request { get; set; } = null!;
    public string LastStatus { get; set; } = "";
    public string CurrentStageTitle { get; set; } = "";
    public bool HasAnyEvaluation { get; set; }
    public bool CanEditOrDelete { get; set; }
    public bool CanStop { get; set; }
}
