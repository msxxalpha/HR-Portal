using HRPortal.Models;

namespace HRPortal.Models;

public class ActivityPerformanceReportViewModel
{
    public List<AuditLog> Items { get; set; } = [];
    public string? FromJalali { get; set; }
    public string? ToJalali { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? UserName { get; set; }
    public string? Search { get; set; }
    public string? Category { get; set; }
    public string? Method { get; set; }
    public bool? Success { get; set; }
    public string ScopeTitle { get; set; } = "";
    public int ScopedEmployeeCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int UniqueUserCount { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
