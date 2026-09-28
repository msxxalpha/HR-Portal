using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "UsersRoles.Manage")]
public class UsersController(HRPortalDbContext db, RoleService roles, AuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? roleId)
    {
        var model = new UserRoleManagementViewModel
        {
            Users = await roles.GetUserRowsAsync(),
            Permissions = await roles.GetPermissionsAsync(),
            Roles = await BuildRoleModelsAsync()
        };
        ViewBag.SelectedRoleId = roleId;
        return View(model);
    }

    [HttpPost, Authorize(Policy = "Users.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUser(int employeeId, bool isSystemUser, int[]? roleIds)
    {
        var employee = await db.Employees.FindAsync(employeeId);
        if (employee is null)
            return NotFound();

        employee.IsSystemUser = isSystemUser;
        if (!isSystemUser)
            employee.Status = "غیرفعال";
        else if (employee.Status == "غیرفعال")
            employee.Status = "فعال";
        employee.UpdatedAt = DateTime.UtcNow;

        await roles.SaveEmployeeRolesAsync(employeeId, roleIds ?? []);
        await db.SaveChangesAsync();

        await audit.WriteAsync("تغییر کاربر و نقش", "Employee", employeeId.ToString(), employee.PersonnelNumber);
        TempData["Success"] = "تنظیمات کاربر ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = "Roles.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRole(RoleEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Title))
        {
            TempData["Error"] = "کد و عنوان نقش الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await roles.SaveRoleAsync(model);
            TempData["Success"] = "نقش و سطح دسترسی آن ذخیره شد.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "کد نقش تکراری است یا ذخیره نقش امکان‌پذیر نیست.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<List<RoleEditModel>> BuildRoleModelsAsync()
    {
        var all = await db.Roles.OrderBy(x => x.Title).ToListAsync();
        var assignments = await db.RolePermissions.AsNoTracking().ToListAsync();

        return all.Select(r => new RoleEditModel
        {
            Id = r.Id,
            Code = r.Code,
            Title = r.Title,
            Description = r.Description ?? "",
            IsActive = r.IsActive,
            PermissionIds = assignments.Where(x => x.RoleId == r.Id).Select(x => x.PermissionId).ToList()
        }).ToList();
    }
}
