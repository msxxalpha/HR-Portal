using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class RoleService(HRPortalDbContext db)
{
    public static readonly IReadOnlyList<(string Code, string Title, string Module)> DefaultPermissions =
    [
        ("Employees.View", "مشاهده کارکنان", "کارکنان"),
        ("Employees.Create", "ایجاد کارمند", "کارکنان"),
        ("Employees.Edit", "ویرایش کارمند", "کارکنان"),
        ("Employees.Deactivate", "غیرفعال‌سازی کارمند", "کارکنان"),
        ("Employees.Delete", "حذف کارمند", "کارکنان"),
        ("Employees.ImportExport", "ورود و خروج Excel", "کارکنان"),
        ("Organization.View", "مشاهده ساختار سازمانی", "ساختار سازمانی"),
        ("Organization.Create", "ایجاد گره سازمانی", "ساختار سازمانی"),
        ("Organization.Edit", "ویرایش گره سازمانی", "ساختار سازمانی"),
        ("Organization.Delete", "حذف گره سازمانی", "ساختار سازمانی"),
        ("Organization.Move", "جابجایی گره سازمانی", "ساختار سازمانی"),
        ("Organization.Finalize", "نهایی‌سازی ساختار", "ساختار سازمانی"),
        ("Settings.View", "مشاهده تنظیمات سامانه", "مدیریت سامانه"),
        ("Settings.Edit", "ویرایش تنظیمات سامانه", "مدیریت سامانه"),
        ("Payroll.View", "مشاهده فیش حقوقی", "خدمات کارکنان"),
        ("PersonnelOrder.View", "مشاهده حکم کارگزینی", "خدمات کارکنان"),
        ("InputQueries.View", "مشاهده کوئری‌های ورودی", "مدیریت سامانه"),
        ("InputQueries.Manage", "مدیریت کوئری‌های ورودی", "مدیریت سامانه"),
        ("Users.Manage", "مدیریت کاربران", "مدیریت کاربران"),
        ("Roles.Manage", "مدیریت نقش‌ها", "مدیریت کاربران"),
        ("Announcements.Manage", "مدیریت اطلاعیه‌ها", "مدیریت سامانه"),
        ("Workflow.Inbox", "کارتابل گردش کار", "گردش کار"),
        ("Workflow.Manage", "مدیریت گردش کار", "گردش کار")
    ];

    public async Task SeedAsync()
    {
        foreach (var item in DefaultPermissions)
        {
            if (!await db.Permissions.AnyAsync(x => x.Code == item.Code))
                db.Permissions.Add(new Permission { Code = item.Code, Title = item.Title, Module = item.Module });
        }
        await db.SaveChangesAsync();

        await EnsureRoleAsync("HR_MANAGER", "مدیریت کارکنان", "دسترسی عملیاتی به اطلاعات کارکنان",
            ["Employees.View", "Employees.Create", "Employees.Edit", "Employees.Deactivate", "Employees.Delete", "Employees.ImportExport"]);
        await EnsureRoleAsync("ORG_MANAGER", "مدیریت ساختار سازمانی", "مدیریت نسخه‌ها و ساختار درختی",
            ["Organization.View", "Organization.Create", "Organization.Edit", "Organization.Delete", "Organization.Move", "Organization.Finalize"]);
        await EnsureRoleAsync("SETTINGS_MANAGER", "مدیریت سامانه", "مدیریت تنظیمات سامانه",
            ["Settings.View", "Settings.Edit", "InputQueries.View", "InputQueries.Manage", "Announcements.Manage"]);
        await EnsureRoleAsync("PAYROLL_USER", "گزارش فیش حقوقی", "دسترسی به فیش حقوقی",
            ["Payroll.View", "PersonnelOrder.View"]);
        await EnsureRoleAsync("EMPLOYEE", "کاربر کارکنان", "نقش پیش‌فرض کارکنان برای استفاده از خدمات پرسنلی",
            ["Payroll.View", "PersonnelOrder.View"]);
        var defaultRoleId = await db.Roles.Where(x => x.Code == "EMPLOYEE").Select(x => x.Id).SingleAsync();
        var assignedEmployees = await db.EmployeeRoles.Select(x => x.EmployeeId).Distinct().ToListAsync();
        var employeesWithoutRole = await db.Employees.Where(x => x.IsSystemUser && !assignedEmployees.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        foreach (var employeeId in employeesWithoutRole)
            db.EmployeeRoles.Add(new EmployeeRole { EmployeeId = employeeId, RoleId = defaultRoleId });
        if (employeesWithoutRole.Count > 0)
            await db.SaveChangesAsync();

        await EnsureRoleAsync("USER_MANAGER", "مدیریت کاربران", "مدیریت وضعیت کاربر و تخصیص نقش به کارکنان",
            ["Users.Manage"]);
        await EnsureRoleAsync("ROLE_MANAGER", "مدیریت نقش‌ها", "ایجاد و ویرایش نقش‌ها و سطح دسترسی",
            ["Roles.Manage"]);
        await EnsureRoleAsync("WORKFLOW_MANAGER", "مدیریت گردش کار", "تعریف گردش کار و تخصیص مراحل به جایگاه‌های سازمانی",
            ["Workflow.Inbox", "Workflow.Manage"]);
    }

    private async Task<Role> EnsureRoleAsync(string code, string title, string description, IEnumerable<string> permissionCodes)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Code == code);
        if (role is null)
        {
            role = new Role { Code = code, Title = title, Description = description, IsActive = true };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var permissionIds = await db.Permissions
            .Where(x => permissionCodes.Contains(x.Code))
            .Select(x => x.Id)
            .ToListAsync();

        var existing = await db.RolePermissions.Where(x => x.RoleId == role.Id).Select(x => x.PermissionId).ToListAsync();
        foreach (var id in permissionIds.Except(existing))
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = id });
        await db.SaveChangesAsync();
        return role;
    }

    public async Task<List<Role>> GetRolesAsync() =>
        await db.Roles
            .OrderBy(r => r.Title)
            .ToListAsync();

    public async Task<List<Permission>> GetPermissionsAsync() =>
        await db.Permissions.OrderBy(x => x.Module).ThenBy(x => x.Title).ToListAsync();

    public async Task<List<EmployeeRoleRow>> GetUserRowsAsync()
    {
        var employees = await db.Employees
            .Include(x => x.OrganizationUnit)
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .ToListAsync();

        var pairs = await db.EmployeeRoles.AsNoTracking().ToListAsync();
        return employees.Select(e => new EmployeeRoleRow
        {
            Employee = e,
            RoleIds = pairs.Where(p => p.EmployeeId == e.Id).Select(p => p.RoleId).ToList()
        }).ToList();
    }

    public async Task<HashSet<string>> GetEmployeePermissionsAsync(int employeeId)
    {
        var codes = await db.EmployeeRoles
            .Where(er => er.EmployeeId == employeeId && er.Role.IsActive)
            .SelectMany(er => db.RolePermissions.Where(rp => rp.RoleId == er.RoleId).Select(rp => rp.Permission.Code))
            .ToListAsync();

        return codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveEmployeeRolesAsync(int employeeId, IEnumerable<int> roleIds)
    {
        var valid = await db.Roles.Where(x => x.IsActive && roleIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        var existing = await db.EmployeeRoles.Where(x => x.EmployeeId == employeeId).ToListAsync();
        db.EmployeeRoles.RemoveRange(existing);
        foreach (var roleId in valid)
            db.EmployeeRoles.Add(new EmployeeRole { EmployeeId = employeeId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    public async Task<RoleEditModel?> GetRoleEditAsync(int? id)
    {
        var role = id.HasValue ? await db.Roles.FindAsync(id.Value) : null;
        if (role is null && id.HasValue) return null;
        return new RoleEditModel
        {
            Id = role?.Id ?? 0,
            Code = role?.Code ?? "",
            Title = role?.Title ?? "",
            Description = role?.Description ?? "",
            IsActive = role?.IsActive ?? true,
            PermissionIds = role is null
                ? []
                : await db.RolePermissions.Where(x => x.RoleId == role.Id).Select(x => x.PermissionId).ToListAsync()
        };
    }

    public async Task SaveRoleAsync(RoleEditModel model)
    {
        Role role;
        if (model.Id == 0)
        {
            role = new Role { Code = model.Code.Trim(), Title = model.Title.Trim(), Description = model.Description.Trim(), IsActive = model.IsActive };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        else
        {
            role = await db.Roles.FindAsync(model.Id) ?? throw new InvalidOperationException("نقش یافت نشد.");
            role.Code = model.Code.Trim();
            role.Title = model.Title.Trim();
            role.Description = model.Description.Trim();
            role.IsActive = model.IsActive;
            await db.SaveChangesAsync();
            db.RolePermissions.RemoveRange(db.RolePermissions.Where(x => x.RoleId == role.Id));
        }

        foreach (var permissionId in await db.Permissions.Where(x => model.PermissionIds.Contains(x.Id)).Select(x => x.Id).ToListAsync())
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });

        await db.SaveChangesAsync();
    }
}
