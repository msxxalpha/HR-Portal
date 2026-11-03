using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "UsersRoles.Manage")]
public class UsersController(
    HRPortalDbContext db,
    RoleService roles,
    AuditService audit,
    ILogger<UsersController>? logger = null) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? roleId)
    {
        var canUsers = User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", "Users.Manage");
        var canRoles = User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", "Roles.Manage");

        var model = new UserRoleManagementViewModel
        {
            Users = canUsers ? await BuildUserRowsAsync() : [],
            Permissions = canRoles ? await roles.GetPermissionsAsync() : [],
            Roles = canRoles ? await BuildRoleModelsAsync() : [],
            OrganizationRankTypes = canRoles ? await roles.GetOrganizationRankTypesAsync() : [],
            RoleRankTypes = canRoles ? await roles.GetRoleRankAssignmentsAsync() : []
        };
        ViewBag.CanUsers = canUsers;
        ViewBag.CanRoles = canRoles;
        ViewBag.SelectedRoleId = roleId;
        return View(model);
    }


    private async Task<List<EmployeeRoleRow>> BuildUserRowsAsync()
    {
        var rows = await roles.GetUserRowsAsync();
        foreach (var row in rows)
        {
            row.PersonalPermissionIds = await roles.GetEmployeePersonalPermissionIdsAsync(row.Employee.Id);
            row.IsPasswordLocked = PasswordLoginLockoutPolicy.IsLocked(row.Employee.PasswordLoginLockedUntilUtc, DateTime.UtcNow);
            row.PasswordLoginLockedUntilUtc = row.Employee.PasswordLoginLockedUntilUtc;
        }
        return rows;
    }

    [HttpPost, Authorize(Policy = "Users.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePersonalPermissions(int employeeId, int[]? permissionIds)
    {
        var employee = await db.Employees.FindAsync(employeeId);
        if (employee is null) return NotFound();
        await roles.SaveEmployeePersonalPermissionsAsync(employeeId, permissionIds ?? []);
        await audit.WriteAsync("تغییر دسترسی‌های شخصی کاربر", "Employee", employeeId.ToString(), employee.PersonnelNumber, employeeId);
        TempData["Success"] = "دسترسی‌های شخصی کاربر ذخیره شد؛ دسترسی مؤثر، اجتماع نقش‌ها و مجوزهای شخصی است.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = "Users.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockPassword(int employeeId)
    {
        var employee = await db.Employees.FindAsync(employeeId);
        if (employee is null) return NotFound();
        employee.PasswordLoginFailedAttempts = 0;
        employee.PasswordLoginLockedUntilUtc = null;
        employee.PasswordLoginLastFailedAtUtc = null;
        employee.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync("رفع قفل ورود با رمز توسط مدیر", "Employee", employeeId.ToString(), employee.PersonnelNumber, employeeId);
        TempData["Success"] = $"محدودیت ورود با رمز برای {employee.PersonnelNumber} برداشته شد.";
        return RedirectToAction(nameof(Index));
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
        employee.PasswordLoginFailedAttempts = 0;
        employee.PasswordLoginLockedUntilUtc = null;
        employee.PasswordLoginLastFailedAtUtc = null;
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

    [HttpGet, Authorize(Policy = "Roles.Manage")]
    public async Task<IActionResult> EditRole(int? id)
    {
        var model = await roles.GetRoleEditAsync(id);
        if (model is null) return NotFound();

        ViewBag.Permissions = await roles.GetPermissionsAsync();
        ViewBag.OrganizationRankTypes = await roles.GetOrganizationRankTypesAsync();
        var assignments = await roles.GetRoleRankAssignmentsAsync();
        ViewBag.AssignedRankTypes = id.HasValue && assignments.TryGetValue(id.Value, out var assigned)
            ? assigned
            : new List<string>();
        ViewBag.IsBuiltInRole = model.Id > 0 && RoleService.IsBuiltInRoleCode(model.Code);
        return View("EditRole", model);
    }

    [HttpPost, Authorize(Policy = "Roles.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRole(RoleEditModel model)
    {
        try
        {
            var roleId = await roles.SaveRoleAsync(model);
            await audit.WriteAsync(
                model.Id == 0 ? "ایجاد نقش جدید" : "ویرایش نقش",
                "Role", roleId.ToString(), model.Code);
            TempData["Success"] = "نقش و دسترسی‌های آن با موفقیت ذخیره شد.";
            return RedirectToAction(nameof(EditRole), new { id = roleId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateException ex)
        {
            logger?.LogError(ex, "Database error while saving role {RoleCode} (RoleId: {RoleId}).", model.Code, model.Id);
            TempData["Error"] = "ذخیره نقش انجام نشد. کد نقش یا اطلاعات واردشده را بررسی کنید و دوباره تلاش کنید.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Unexpected error while saving role {RoleCode} (RoleId: {RoleId}).", model.Code, model.Id);
            TempData["Error"] = "خطای غیرمنتظره‌ای هنگام ذخیره نقش رخ داد. جزئیات خطا برای مدیر سامانه ثبت شد.";
        }

        return model.Id > 0
            ? RedirectToAction(nameof(EditRole), new { id = model.Id })
            : RedirectToAction(nameof(EditRole));
    }

    [HttpPost, Authorize(Policy = "Roles.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRoleStatus(int roleId)
    {
        var role = await db.Roles.FindAsync(roleId);
        if (role is null) return NotFound();

        role.IsActive = !role.IsActive;
        await db.SaveChangesAsync();
        await audit.WriteAsync(
            role.IsActive ? "فعال‌سازی نقش" : "غیرفعال‌سازی نقش",
            "Role", role.Id.ToString(), role.Code);
        TempData["Success"] = role.IsActive
            ? $"نقش «{role.Title}» فعال شد."
            : $"نقش «{role.Title}» غیرفعال شد؛ دسترسی‌های وابسته به آن دیگر اعمال نمی‌شود.";
        return Redirect("/Users#roles");
    }

    [HttpPost, Authorize(Policy = "Roles.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRole(int roleId)
    {
        var role = await db.Roles.FindAsync(roleId);
        if (role is null) return NotFound();

        if (RoleService.IsBuiltInRoleCode(role.Code))
        {
            TempData["Error"] = "نقش‌های سیستمی قابل حذف نیستند؛ در صورت نیاز آن‌ها را غیرفعال کنید.";
            return Redirect("/Users#roles");
        }

        var assignedUsers = await db.EmployeeRoles.CountAsync(x => x.RoleId == roleId);
        var assignedRanks = await db.RoleOrganizationRanks.CountAsync(x => x.RoleId == roleId);
        if (assignedUsers > 0 || assignedRanks > 0)
        {
            TempData["Error"] = $"حذف نقش «{role.Title}» ممکن نیست؛ ابتدا تخصیص مستقیم آن به {assignedUsers} کاربر و تخصیص سازمانی آن به {assignedRanks} رده را از سامانه بردارید.";
            return Redirect("/Users#roles");
        }

        db.RolePermissions.RemoveRange(db.RolePermissions.Where(x => x.RoleId == roleId));
        db.Roles.Remove(role);
        await db.SaveChangesAsync();
        await audit.WriteAsync("حذف نقش", "Role", role.Id.ToString(), role.Code);
        TempData["Success"] = $"نقش «{role.Title}» حذف شد.";
        return Redirect("/Users#roles");
    }

    [HttpPost, Authorize(Policy = "Roles.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRoleRanks(int roleId, string[]? rankTypes)
    {
        try
        {
            await roles.SaveRoleRankTypesAsync(roleId, rankTypes ?? []);
            TempData["Success"] = "تخصیص رده‌های سازمانی به نقش ذخیره شد؛ دسترسی‌های مؤثر کاربران در درخواست بعدی بر اساس ساختار نهایی و تمام نقش‌های فعال محاسبه می‌شود.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(EditRole), new { id = roleId });
    }

    private async Task<List<RoleEditModel>> BuildRoleModelsAsync()
    {
        var all = await db.Roles.AsNoTracking().OrderBy(x => x.Title).ToListAsync();
        var roleIds = all.Select(x => x.Id).ToArray();
        if (roleIds.Length == 0) return [];

        var assignments = await db.RolePermissions.AsNoTracking()
            .Where(x => roleIds.Contains(x.RoleId) && x.Permission.Code != "ContractRenewal.Manage")
            .Select(x => new { x.RoleId, x.PermissionId, x.Permission.Module })
            .ToListAsync();
        var userAssignments = await db.EmployeeRoles.AsNoTracking()
            .Where(x => roleIds.Contains(x.RoleId))
            .Select(x => new { x.RoleId, x.EmployeeId })
            .ToListAsync();
        var rankAssignments = await db.RoleOrganizationRanks.AsNoTracking()
            .Where(x => roleIds.Contains(x.RoleId))
            .Select(x => new { x.RoleId, x.RankType })
            .ToListAsync();

        // Count unique active system users with either a direct role assignment
        // or an inherited assignment through the currently effective final org
        // structure. This makes the count meaningful for both assignment modes.
        var activeSystemUsers = await db.Employees.AsNoTracking()
            .Where(x => x.IsSystemUser && x.Status == "فعال")
            .Select(x => new { x.Id, x.OrganizationPositionId })
            .ToListAsync();
        var activeUserIds = activeSystemUsers.Select(x => x.Id).ToHashSet();
        var userCounts = roleIds.ToDictionary(x => x, _ => new HashSet<int>());
        foreach (var assignment in userAssignments)
            if (activeUserIds.Contains(assignment.EmployeeId))
                userCounts[assignment.RoleId].Add(assignment.EmployeeId);

        var rankTypesByRole = rankAssignments
            .GroupBy(x => x.RoleId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.RankType)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));

        if (rankTypesByRole.Count > 0 && activeSystemUsers.Count > 0)
        {
            var revisionId = await db.OrganizationStructureRevisions.AsNoTracking()
                .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
                .OrderByDescending(x => x.EffectiveDate)
                .ThenByDescending(x => x.FinalizedAt)
                .ThenByDescending(x => x.Id)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync();

            if (revisionId.HasValue)
            {
                var nodes = await db.OrganizationNodes.AsNoTracking()
                    .Where(x => x.OrganizationStructureRevisionId == revisionId.Value && x.IsActive)
                    .Select(x => new RoleService.OrganizationRankNode(
                        x.Id, x.Code, x.RankType, x.PositionHolderEmployeeId, x.AssignedEmployeeIdsCsv))
                    .ToListAsync();

                var positionIds = activeSystemUsers
                    .Where(x => x.OrganizationPositionId.HasValue)
                    .Select(x => x.OrganizationPositionId!.Value).Distinct().ToArray();
                var positionCodes = positionIds.Length == 0
                    ? new Dictionary<int, string>()
                    : await db.OrganizationNodes.AsNoTracking()
                        .Where(x => positionIds.Contains(x.Id))
                        .ToDictionaryAsync(x => x.Id, x => x.Code);

                foreach (var employee in activeSystemUsers)
                {
                    var positionCode = employee.OrganizationPositionId.HasValue &&
                                       positionCodes.TryGetValue(employee.OrganizationPositionId.Value, out var code)
                        ? code
                        : null;
                    var employeeRanks = RoleService.ResolveRankTypesForEmployee(
                        employee.Id, employee.OrganizationPositionId, positionCode, nodes);

                    if (employeeRanks.Count == 0) continue;
                    foreach (var roleRank in rankTypesByRole)
                        if (employeeRanks.Overlaps(roleRank.Value))
                            userCounts[roleRank.Key].Add(employee.Id);
                }
            }
        }

        return all.Select(role =>
        {
            var rolePermissions = assignments.Where(x => x.RoleId == role.Id).ToList();
            var rankTypes = rankAssignments.Where(x => x.RoleId == role.Id)
                .Select(x => x.RankType).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x).ToList();
            return new RoleEditModel
            {
                Id = role.Id,
                Code = role.Code,
                Title = role.Title,
                Description = role.Description ?? string.Empty,
                IsActive = role.IsActive,
                IsBuiltIn = RoleService.IsBuiltInRoleCode(role.Code),
                PermissionIds = rolePermissions.Select(x => x.PermissionId).Distinct().ToList(),
                PermissionCount = rolePermissions.Select(x => x.PermissionId).Distinct().Count(),
                PermissionModules = rolePermissions.Select(x => x.Module).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x).ToList(),
                UserCount = userCounts[role.Id].Count,
                AssignedRankTypes = rankTypes,
                OrganizationRankCount = rankTypes.Count
            };
        }).ToList();
    }
}
