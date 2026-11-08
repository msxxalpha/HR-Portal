namespace HRPortal.Models;

public class AuditLogReportViewModel
{
    public List<AuditLog> Items { get; set; } = new();
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public string? Search { get; set; }
    public string? UserName { get; set; }
    public string? Category { get; set; }
    public string? Method { get; set; }
    public bool? Success { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public int UniqueUserCount { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
