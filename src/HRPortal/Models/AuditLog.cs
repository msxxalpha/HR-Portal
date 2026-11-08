namespace HRPortal.Models;

public class AuditLog
{
    public long Id { get; set; }
    public int? EmployeeId { get; set; }
    public int? AdminUserId { get; set; }
    public string? UserName { get; set; }
    public string? UserType { get; set; }
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Description { get; set; }
    public string? IpAddress { get; set; }
    public string? HttpMethod { get; set; }
    public string? RequestPath { get; set; }
    public string? Controller { get; set; }
    public string? ActionName { get; set; }
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    public string? Category { get; set; }
    public string? CorrelationId { get; set; }
    public string? UserAgent { get; set; }
    public bool? IsSuccess { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
