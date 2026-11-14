using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public sealed class OutsourcedServiceRequest
{
    public int Id { get; set; }
    [MaxLength(30)] public string RequestNumber { get; set; } = "";
    [Required, MaxLength(3000)] public string ServiceDescription { get; set; } = "";
    [Required, MaxLength(40)] public string UnitOfMeasure { get; set; } = "";
    public decimal Quantity { get; set; }
    public DateTime NeedByDate { get; set; }
    [Required, MaxLength(20)] public string Priority { get; set; } = "Normal";
    [Required, MaxLength(30)] public string Status { get; set; } = "Submitted";
    public int RequesterEmployeeId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedByEmployeeId { get; set; }
}

public sealed class OutsourcedServiceRequestRevision
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    [MaxLength(30)] public string Action { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public int? ActorEmployeeId { get; set; }
    [MaxLength(200)] public string? ActorUserName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public OutsourcedServiceRequest Request { get; set; } = null!;
}

public sealed class OutsourcedServiceRequestFormVm
{
    public string ServiceDescription { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal Quantity { get; set; }
    public string NeedByDateJalali { get; set; } = "";
    public string Priority { get; set; } = "Normal";
}

public sealed class OutsourcedServiceDashboardViewModel
{
    public string? FromJalali { get; set; }
    public string? ToJalali { get; set; }
    public string Priority { get; set; } = "all";
    public string Status { get; set; } = "all";
    public string UnitOfMeasure { get; set; } = "all";
    public string OrganizationalUnit { get; set; } = "all";
    public List<string> OrganizationalUnitOptions { get; set; } = [];
    public List<OutsourcedServiceOrganizationBucket> OrganizationalUnitBuckets { get; set; } = [];
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int CompletedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ReturnedCount { get; set; }
    public int UrgentCount { get; set; }
    public List<OutsourcedServiceBucket> PriorityBuckets { get; set; } = [];
    public List<OutsourcedServiceBucket> StatusBuckets { get; set; } = [];
    public List<OutsourcedServiceBucket> MonthlyBuckets { get; set; } = [];
    public List<OutsourcedServiceBucket> UnitBuckets { get; set; } = [];
    public List<OutsourcedServiceReportRow> Rows { get; set; } = [];
}

public sealed class OutsourcedServiceOrganizationBucket
{
    public string Label { get; set; } = "";
    public int InProgressCount { get; set; }
    public int CompletedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ReturnedCount { get; set; }
    public int NotStartedCount { get; set; }
    public int TotalCount => InProgressCount + CompletedCount + RejectedCount + ReturnedCount + NotStartedCount;
}

public sealed class OutsourcedServiceBucket
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public sealed class OutsourcedServiceReportRow
{
    public int Id { get; set; }
    public string RequestNumber { get; set; } = "";
    public string ServiceDescription { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal Quantity { get; set; }
    public DateTime NeedByDate { get; set; }
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public string RequesterName { get; set; } = "";
    public string PersonnelNumber { get; set; } = "";
    public string Organization { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string CurrentStepTitle { get; set; } = "";
}

public sealed class OutsourcedServiceRequestListItem
{
    public OutsourcedServiceRequest Request { get; set; } = new();
    public string WorkflowStatus { get; set; } = "Submitted";
    public string CurrentStepTitle { get; set; } = "";
}
