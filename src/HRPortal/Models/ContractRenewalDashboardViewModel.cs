namespace HRPortal.Models;

public class ContractRenewalDashboardViewModel
{
    public DateTime Today { get; set; } = DateTime.Today;
    public int ExpiringEmployeeCount { get; set; }
    public int TotalRequests { get; set; }
    public int InProgressRequests { get; set; }
    public int CompletedRequests { get; set; }
    public int StoppedRequests { get; set; }
    public int NeedsAttentionRequests { get; set; }
    public decimal? AverageCompletedScore { get; set; }
    public List<ContractRenewalExpiryRow> ExpiringEmployees { get; set; } = [];
    public List<ContractRenewalDashboardBucket> StatusBuckets { get; set; } = [];
    public List<ContractRenewalDashboardBucket> MonthlyBuckets { get; set; } = [];
}

public class ContractRenewalExpiryRow
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string PersonnelNumber { get; set; } = "";
    public string OrganizationUnit { get; set; } = "";
    public string PositionTitle { get; set; } = "";
    public DateTime ContractEndDate { get; set; }
    public int DaysRemaining { get; set; }
    public ContractRenewalRequest? LatestRequest { get; set; }
    public bool CanStartRequest { get; set; }
}

public class ContractRenewalDashboardBucket
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}
