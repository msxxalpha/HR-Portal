namespace HRPortal.Models;

public class UserRoleManagementViewModel
{
    public List<EmployeeRoleRow> Users { get; set; } = [];
    public List<RoleEditModel> Roles { get; set; } = [];
    public List<Permission> Permissions { get; set; } = [];
}

public class EmployeeRoleRow
{
    public Employee Employee { get; set; } = null!;
    public List<int> RoleIds { get; set; } = [];
}

public class RoleEditModel
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<int> PermissionIds { get; set; } = [];
}
