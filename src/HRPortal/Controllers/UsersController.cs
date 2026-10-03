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
        var canUsers = User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", "Users.Manage");
        var canRoles = User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", "Roles.Manage");

        var model = new UserRoleManagementViewModel
        {
            Users = canUsers ? await roles.GetUserRowsAsync() : [],
            Permissions = canRoles ? await roles.GetPermissionsAsync() : [],
            Roles = canRoles ? await BuildRoleModelsAsync() : []
        };
        ViewBag.CanUsers = canUsers;
        ViewBag.CanRoles = canRoles;
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

    [HttpPost, Authorize(Policy = "Users.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPersonalCredential(int employeeId, string? newValue, string? confirmValue)
    {
        var password = newValue ?? string.Empty;
        var confirmation = confirmValue ?? string.Empty;

        // The personal-password policy is centralized: the raw string must
        // contain at least six characters. No character composition, trimming,
        // or normalization is applied.
        if (!PasswordHasher.IsValidPersonalPassword(password))
        {
            TempData["Error"] = "رمز جدید باید حداقل ۶ کاراکتر داشته باشد.";
            return RedirectToAction(nameof(Index));
        }
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            TempData["Error"] = "تکرار رمز جدید با مقدار جدید یکسان نیست.";
            return RedirectToAction(nameof(Index));
        }
        var employee = await db.Employees.FindAsync(employeeId);
        if (employee is null)
        {
            TempData["Error"] = "کارمند موردنظر یافت نشد.";
            return RedirectToAction(nameof(Index));
        }
        if (!employee.IsSystemUser || employee.Status != "فعال")
        {
            TempData["Error"] = "فقط برای کاربران فعال سامانه می‌توان رمز شخصی تعیین یا تغییر داد.";
            return RedirectToAction(nameof(Index));
        }
        var hadValue = !string.IsNullOrWhiteSpace(employee.PersonalPasswordHash);
        employee.PersonalPasswordHash = PasswordHasher.Hash(password);
        employee.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync(
            hadValue ? "تغییر رمز شخصی توسط مدیر" : "تعیین رمز شخصی توسط مدیر",
            "Employee",
            employee.Id.ToString(),
            employee.PersonnelNumber,
            employee.Id);
        TempData["Success"] = hadValue
            ? $"رمز شخصی کاربر {employee.PersonnelNumber} تغییر کرد."
            : $"رمز شخصی برای کاربر {employee.PersonnelNumber} تعیین شد.";
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
