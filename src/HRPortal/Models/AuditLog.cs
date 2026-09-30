namespace HRPortal.Models;

public class AuditLog
{
    public long Id { get; set; }
    public int? EmployeeId { get; set; }
    public string Action { get; set; } = ""; public string EntityName { get; set; } = ""; public string? EntityId { get; set; }
    public string? Description { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}