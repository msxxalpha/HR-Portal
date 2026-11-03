namespace HRPortal.Models;
public class EmployeePermission
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int PermissionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Permission Permission { get; set; } = null!;
}