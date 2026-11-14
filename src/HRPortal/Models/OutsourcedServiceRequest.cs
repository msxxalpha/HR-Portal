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
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int CompletedCount { get; set; }
    public int RejectedCount { get; set; }
    public int UrgentCount { get; set; }
    public List<OutsourcedServiceBucket> PriorityBuckets { get; set; } = [];
    public List<OutsourcedServiceBucket> StatusBuckets { get; set; } = [];
    public List<OutsourcedServiceBucket> MonthlyBuckets { get; set; } = [];
    public List<OutsourcedServiceBucket> UnitBuckets { get; set; } = [];
    public List<OutsourcedServiceReportRow> Rows { get; set; } = [];
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