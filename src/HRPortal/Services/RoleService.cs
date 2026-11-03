using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace HRPortal.Services;

public class RoleService(HRPortalDbContext db)
{
    private static readonly HashSet<string> BuiltInRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "HR_MANAGER", "ORG_MANAGER", "SETTINGS_MANAGER", "PAYROLL_USER", "EMPLOYEE",
        "USER_MANAGER", "ROLE_MANAGER", "WORKFLOW_MANAGER", "SUGGESTION_MANAGER",
        "PERFORMANCE_MANAGER", "PERFORMANCE_EVALUATOR"
    };

    public static bool IsBuiltInRoleCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && BuiltInRoleCodes.Contains(code.Trim());

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
        ("Reports.View", "مشاهده گزارش‌ها", "گزارش‌ها"),
        ("InputQueries.View", "مشاهده کوئری‌های ورودی", "مدیریت سامانه"),
        ("InputQueries.Manage", "مدیریت کوئری‌های ورودی", "مدیریت سامانه"),
        ("Users.Manage", "مدیریت کاربران", "مدیریت کاربران"),
        ("Roles.Manage", "مدیریت نقش‌ها", "مدیریت کاربران"),
        ("Announcements.Manage", "مدیریت اطلاعیه‌ها", "مدیریت سامانه"),
        ("Suggestions.Submit", "ثبت و پیگیری پیشنهاد", "نظام پیشنهادات"),
        ("Suggestions.ViewAll", "مشاهده همه پیشنهادها", "نظام پیشنهادات"),
        ("Suggestions.Manage", "مدیریت پیشنهادها و تعیین پیشنهاددهنده", "نظام پیشنهادات"),
        ("Suggestions.Evaluate", "ارزیابی تخصصی پیشنهادها", "نظام پیشنهادات"),
        ("Suggestions.Reports", "مشاهده داشبورد و گزارش‌های نظام پیشنهادات", "نظام پیشنهادات"),
        ("Suggestions.Export", "دریافت خروجی گزارش‌های نظام پیشنهادات", "نظام پیشنهادات"),
        ("Workflow.Inbox", "کارتابل گردش کار", "گردش کار"),
        ("Workflow.Manage", "مدیریت گردش کار", "گردش کار"),
        ("ContractRenewal.View", "مشاهده جزئیات و سوابق درخواست تمدید", "تمدید قرارداد"),
        ("ContractRenewal.List", "مشاهده فهرست و سوابق درخواست‌ها", "تمدید قرارداد"),
        ("ContractRenewal.Reports", "مشاهده داشبورد و گزارش‌های تمدید قرارداد", "تمدید قرارداد"),
        ("ContractRenewal.Evaluation.Manage", "مدیریت پرسش‌ها و ماژول ارزشیابی تمدید قرارداد", "تمدید قرارداد"),
        ("ContractRenewal.Create", "ایجاد و ثبت درخواست تمدید", "تمدید قرارداد"),
        ("ContractRenewal.Edit", "ویرایش درخواست پیش از ارزیابی", "تمدید قرارداد"),
        ("ContractRenewal.Delete", "حذف نرم درخواست پیش از ارزیابی", "تمدید قرارداد"),
        ("ContractRenewal.Settings", "مدیریت اطلاعات پایه و تنظیمات گردش", "تمدید قرارداد"),
        ("ContractRenewal.Workflow", "مدیریت و توقف گردش درخواست‌ها", "تمدید قرارداد"),
        ("ContractRenewal.Manage", "دسترسی کامل و سازگاری با نقش‌های قبلی", "تمدید قرارداد"),
        ("MonthlyPerformance.View", "مشاهده ارزیابی عملکرد شخصی", "ماژول ارزیابی عملکرد کارکنان"),
        ("MonthlyPerformance.Evaluate", "ارزیابی کارکنان زیرمجموعه", "ماژول ارزیابی عملکرد کارکنان"),
        ("MonthlyPerformance.Manage", "مدیریت اطلاعات پایه و دوره‌های ارزیابی", "ماژول ارزیابی عملکرد کارکنان"),
        ("MonthlyPerformance.Reports", "مشاهده گزارش‌های مدیریتی عملکرد", "ماژول ارزیابی عملکرد کارکنان")
    ];

    public sealed record OrganizationRankNode(
        int Id, string Code, string RankType, int? PositionHolderEmployeeId, string? AssignedEmployeeIdsCsv);

    public static bool IsMonthlyPerformanceManagerRole(string? roleCode, string? roleTitle)
    {
        var code = (roleCode ?? string.Empty).Trim().Replace("-", "_").ToUpperInvariant();
        var title = (roleTitle ?? string.Empty).Trim()
            .Replace("ي", "ی", StringComparison.Ordinal)
            .Replace("ك", "ک", StringComparison.Ordinal)
            .Replace("‌", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return code is "PERFORMANCE_MANAGER" or "MONTHLY_PERFORMANCE_MANAGER" or "PERFORMANCE_ADMIN" ||
            title.StartsWith("مدیریتارزیابیعملکرد", StringComparison.Ordinal) ||
            title.StartsWith("مدیریتماژولارزیابیعملکرد", StringComparison.Ordinal) ||
            title.StartsWith("مدیرارزیابیعملکرد", StringComparison.Ordinal) ||
            title.StartsWith("مدیرماژولارزیابیعملکرد", StringComparison.Ordinal);
    }

    public static HashSet<string> AggregatePermissionCodes(params IEnumerable<string>[] permissionSets)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permissionSet in permissionSets)
            foreach (var code in permissionSet)
                if (!string.IsNullOrWhiteSpace(code))
                    result.Add(code.Trim());
        return result;
    }

    public static HashSet<string> ResolveRankTypesForEmployee(
        int employeeId, int? organizationPositionId, string? organizationPositionCode,
        IEnumerable<OrganizationRankNode> nodes)
    {
        var currentNodes = nodes.ToList();
        var assignedPositionNodeIds = currentNodes
            .Where(node =>
                (organizationPositionId.HasValue && node.Id == organizationPositionId.Value) ||
                (!string.IsNullOrWhiteSpace(organizationPositionCode) &&
                 string.Equals(node.Code, organizationPositionCode, StringComparison.OrdinalIgnoreCase)))
            .Select(node => node.Id).ToHashSet();

        bool CsvContainsEmployee(string? csv) =>
            (csv ?? string.Empty)
                .Split([',', '،', ';', '؛'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(value => int.TryParse(value, out var parsed) && parsed == employeeId);

        return currentNodes
            .Where(node => node.PositionHolderEmployeeId == employeeId ||
                assignedPositionNodeIds.Contains(node.Id) || CsvContainsEmployee(node.AssignedEmployeeIdsCsv))
            .Select(node => node.RankType?.Trim() ?? string.Empty)
            .Where(rank => !string.IsNullOrWhiteSpace(rank))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task SeedAsync()
    {
        foreach (var item in DefaultPermissions)
        {
            if (!await db.Permissions.AnyAsync(x => x.Code == item.Code))
                db.Permissions.Add(new Permission { Code = item.Code, Title = item.Title, Module = item.Module });
        }
        await db.SaveChangesAsync();
        await ConsolidateLegacyContractRenewalPermissionsAsync();

        await EnsureRoleAsync("HR_MANAGER", "مدیریت کارکنان", "دسترسی عملیاتی به اطلاعات کارکنان و کارتابل تمدید قرارداد",
            ["Employees.View", "Employees.Create", "Employees.Edit", "Employees.Deactivate", "Employees.Delete", "Employees.ImportExport", "ContractRenewal.Manage", "Workflow.Inbox"]);
        await EnsureRoleAsync("ORG_MANAGER", "مدیریت ساختار سازمانی", "مدیریت نسخه‌ها و ساختار درختی",
            ["Organization.View", "Organization.Create", "Organization.Edit", "Organization.Delete", "Organization.Move", "Organization.Finalize"]);
        await EnsureRoleAsync("SETTINGS_MANAGER", "مدیریت سامانه", "مدیریت تنظیمات سامانه",
            ["Settings.View", "Settings.Edit", "InputQueries.View", "InputQueries.Manage", "Announcements.Manage"]);
        await EnsureRoleAsync("PAYROLL_USER", "گزارش فیش حقوقی", "دسترسی به فیش حقوقی",
            ["Payroll.View", "PersonnelOrder.View", "Workflow.Inbox"]);
        await EnsureRoleAsync("EMPLOYEE", "کاربر کارکنان", "نقش پیش‌فرض کارکنان برای استفاده از خدمات پرسنلی و مشاهده عملکرد شخصی",
            ["Payroll.View", "PersonnelOrder.View", "Workflow.Inbox", "MonthlyPerformance.View", "Suggestions.Submit"]);
        var defaultRoleId = await db.Roles.Where(x => x.Code == "EMPLOYEE").Select(x => x.Id).SingleAsync();
        var assignedEmployees = await db.EmployeeRoles
            .Where(x => x.Role.Code == "EMPLOYEE")
            .Select(x => x.EmployeeId)
            .Distinct()
            .ToListAsync();
        var employeesWithoutRole = await db.Employees
            .Where(x => !assignedEmployees.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();
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
        await EnsureRoleAsync("SUGGESTION_MANAGER", "مدیریت نظام پیشنهادات", "مشاهده و مدیریت پیشنهادها و دسترسی به ارزیابی تخصصی و گزارش‌ها",
            ["Suggestions.ViewAll", "Suggestions.Manage", "Suggestions.Evaluate", "Suggestions.Reports", "Suggestions.Export"]);

        // Existing roles that already hold the legacy all-access renewal permission
        // receive the new granular capabilities too; no existing assignment is revoked.
        var renewalManagerRoleIds = await db.RolePermissions.AsNoTracking()
            .Where(x => x.Permission.Code == "ContractRenewal.Manage")
            .Select(x => x.RoleId).Distinct().ToListAsync();
        var renewalPermissionIds = await db.Permissions.AsNoTracking()
            .Where(x => x.Code.StartsWith("ContractRenewal."))
            .Select(x => x.Id).ToListAsync();
        var currentRenewalAssignments = await db.RolePermissions.AsNoTracking()
            .Where(x => renewalManagerRoleIds.Contains(x.RoleId))
            .Select(x => new { x.RoleId, x.PermissionId }).ToListAsync();
        foreach (var roleId in renewalManagerRoleIds)
        foreach (var permissionId in renewalPermissionIds)
            if (!currentRenewalAssignments.Any(x => x.RoleId == roleId && x.PermissionId == permissionId))
                db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        if (renewalManagerRoleIds.Count > 0) await db.SaveChangesAsync();

        // The manager role is a first-class seeded role. Existing locally-created
        // roles with the established Persian title are repaired in place below,
        // so their current employee assignments are preserved.
        await EnsureRoleAsync(
            "PERFORMANCE_MANAGER", "مدیریت ارزیابی عملکرد",
            "مدیریت اطلاعات پایه، دوره‌ها و گزارش‌های ماژول ارزیابی عملکرد کارکنان",
            ["MonthlyPerformance.Manage", "MonthlyPerformance.Reports"]);

        var managementRoles = await db.Roles.ToListAsync();
        foreach (var managementRole in managementRoles.Where(x =>
            IsMonthlyPerformanceManagerRole(x.Code, x.Title)))
        {
            await EnsureRolePermissionsAsync(
                managementRole, ["MonthlyPerformance.Manage", "MonthlyPerformance.Reports"]);
        }

        var evaluatorRoleExisted = await db.Roles.AnyAsync(x => x.Code == "PERFORMANCE_EVALUATOR");
        var evaluatorRole = await EnsureRoleAsync(
            "PERFORMANCE_EVALUATOR", "ارزیاب عملکرد",
            "دسترسی به کارتابل ارزیابی کارکنان زیرمجموعه طبق ساختار سازمانی",
            ["MonthlyPerformance.View", "MonthlyPerformance.Evaluate"]);
        if (!evaluatorRoleExisted)
        {
            var evaluatorIds = await db.Employees.AsNoTracking()
                .Where(x => x.IsPerformanceEvaluator).Select(x => x.Id).ToListAsync();
            var alreadyAssigned = (await db.EmployeeRoles.AsNoTracking()
                .Where(x => x.RoleId == evaluatorRole.Id).Select(x => x.EmployeeId).ToListAsync()).ToHashSet();
            var added = 0;
            foreach (var employeeId in evaluatorIds.Except(alreadyAssigned))
            {
                db.EmployeeRoles.Add(new EmployeeRole { EmployeeId = employeeId, RoleId = evaluatorRole.Id });
                added++;
            }
            if (added > 0) await db.SaveChangesAsync();
        }
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

        await EnsureRolePermissionsAsync(role, permissionCodes);
        return role;
    }

    private async Task EnsureRolePermissionsAsync(Role role, IEnumerable<string> permissionCodes)
    {
        var codes = permissionCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var permissionIds = await db.Permissions
            .Where(x => codes.Contains(x.Code))
            .Select(x => x.Id)
            .ToListAsync();

        var existing = await db.RolePermissions
            .Where(x => x.RoleId == role.Id)
            .Select(x => x.PermissionId)
            .ToListAsync();

        foreach (var id in permissionIds.Except(existing))
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = id });

        await db.SaveChangesAsync();
    }

    public async Task<List<Role>> GetRolesAsync() =>
        await db.Roles
            .OrderBy(r => r.Title)
            .ToListAsync();

    // ContractRenewal.Manage remains in the database only as a compatibility
    // claim for old cookies/integrations. Administrators configure the nine
    // granular renewal permissions instead of assigning this technical alias.
    public async Task<List<Permission>> GetPermissionsAsync() =>
        await db.Permissions
            .Where(x => x.Code != "ContractRenewal.Manage")
            .OrderBy(x => x.Module).ThenBy(x => x.Title)
            .ToListAsync();

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
        // A deactivated employee must lose all role-derived access even if their
        // browser still holds an otherwise valid authentication cookie.
        var isActiveSystemUser = await db.Employees.AsNoTracking()
            .AnyAsync(x => x.Id == employeeId && x.IsSystemUser && x.Status == "فعال");
        if (!isActiveSystemUser)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var directCodes = await db.EmployeeRoles.AsNoTracking()
            .Where(er => er.EmployeeId == employeeId && er.Role.IsActive)
            .SelectMany(er => db.RolePermissions.Where(rp => rp.RoleId == er.RoleId)
                .Select(rp => rp.Permission.Code))
            .ToListAsync();

        var inheritedCodes = new List<string>();
        var revisionId = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.FinalizedAt).ThenByDescending(x => x.Id)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync();

        if (revisionId.HasValue)
        {
            var employee = await db.Employees.AsNoTracking()
                .Where(x => x.Id == employeeId)
                .Select(x => new { x.OrganizationPositionId }).FirstOrDefaultAsync();

            if (employee is not null)
            {
                string? positionCode = null;
                if (employee.OrganizationPositionId.HasValue)
                    positionCode = await db.OrganizationNodes.AsNoTracking()
                        .Where(x => x.Id == employee.OrganizationPositionId.Value)
                        .Select(x => x.Code).FirstOrDefaultAsync();

                var nodes = await db.OrganizationNodes.AsNoTracking()
                    .Where(x => x.OrganizationStructureRevisionId == revisionId.Value && x.IsActive)
                    .Select(x => new OrganizationRankNode(
                        x.Id, x.Code, x.RankType, x.PositionHolderEmployeeId, x.AssignedEmployeeIdsCsv))
                    .ToListAsync();

                var rankTypes = ResolveRankTypesForEmployee(
                    employeeId, employee.OrganizationPositionId, positionCode, nodes);
                if (rankTypes.Count > 0)
                {
                    var rankRoleIds = await db.RoleOrganizationRanks.AsNoTracking()
                        .Join(db.Roles.AsNoTracking().Where(role => role.IsActive),
                            assignment => assignment.RoleId, role => role.Id,
                            (assignment, role) => new { assignment.RoleId, assignment.RankType })
                        .Where(x => rankTypes.Contains(x.RankType))
                        .Select(x => x.RoleId).Distinct().ToListAsync();

                    if (rankRoleIds.Count > 0)
                        inheritedCodes = await db.RolePermissions.AsNoTracking()
                            .Where(x => rankRoleIds.Contains(x.RoleId))
                            .Select(x => x.Permission.Code).ToListAsync();
                }
            }
        }

        var personalCodes = await db.EmployeePermissions.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => x.Permission.Code).ToListAsync();
        return AggregatePermissionCodes(directCodes, inheritedCodes, personalCodes);
    }

    public async Task<bool> IsPerformanceEvaluatorAsync(int employeeId) =>
        await db.Employees.AsNoTracking()
            .AnyAsync(x => x.Id == employeeId && x.IsSystemUser &&
                x.Status == "فعال" && x.IsPerformanceEvaluator);

    public async Task SynchronizePerformanceEvaluatorRoleAsync(int employeeId, bool isEvaluator)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Code == "PERFORMANCE_EVALUATOR" && x.IsActive);
        if (role is null) return;

        var assignment = await db.EmployeeRoles.FirstOrDefaultAsync(x =>
            x.EmployeeId == employeeId && x.RoleId == role.Id);
        if (isEvaluator && assignment is null)
            db.EmployeeRoles.Add(new EmployeeRole { EmployeeId = employeeId, RoleId = role.Id });
        else if (!isEvaluator && assignment is not null)
            db.EmployeeRoles.Remove(assignment);

        await db.SaveChangesAsync();
    }

    public async Task<List<string>> GetOrganizationRankTypesAsync()
    {
        var configured = await db.OrganizationRankDefinitions.AsNoTracking()
            .Select(x => x.Title).ToListAsync();
        var used = await db.OrganizationNodes.AsNoTracking()
            .Where(x => x.IsActive).Select(x => x.RankType).ToListAsync();
        return configured.Concat(used)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    public async Task<Dictionary<int, List<string>>> GetRoleRankAssignmentsAsync()
    {
        var rows = await db.RoleOrganizationRanks.AsNoTracking().ToListAsync();
        return rows.GroupBy(x => x.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RankType).ToList());
    }

    public async Task SaveRoleRankTypesAsync(int roleId, IEnumerable<string> rankTypes)
    {
        if (!await db.Roles.AnyAsync(x => x.Id == roleId))
            throw new InvalidOperationException("نقش یافت نشد.");

        var validTypes = await GetOrganizationRankTypesAsync();
        var selected = rankTypes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => validTypes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        db.RoleOrganizationRanks.RemoveRange(
            db.RoleOrganizationRanks.Where(x => x.RoleId == roleId));
        foreach (var rankType in selected)
            db.RoleOrganizationRanks.Add(new RoleOrganizationRank { RoleId = roleId, RankType = rankType });
        await db.SaveChangesAsync();
    }

    public async Task<List<int>> GetEmployeePersonalPermissionIdsAsync(int employeeId) =>
        await db.EmployeePermissions.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.Permission.Code != "ContractRenewal.Manage")
            .Select(x => x.PermissionId).ToListAsync();

    public async Task SaveEmployeePersonalPermissionsAsync(int employeeId, IEnumerable<int> permissionIds)
    {
        var selectedIds = (permissionIds ?? []).Distinct().ToArray();
        var valid = await db.Permissions
            .Where(x => selectedIds.Contains(x.Id) && x.Code != "ContractRenewal.Manage")
            .Select(x => x.Id).ToListAsync();
        db.EmployeePermissions.RemoveRange(db.EmployeePermissions.Where(x => x.EmployeeId == employeeId));
        foreach (var permissionId in valid.Distinct())
            db.EmployeePermissions.Add(new EmployeePermission { EmployeeId = employeeId, PermissionId = permissionId });
        await db.SaveChangesAsync();
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
                : await db.RolePermissions.Where(x => x.RoleId == role.Id && x.Permission.Code != "ContractRenewal.Manage")
                    .Select(x => x.PermissionId).ToListAsync()
        };
    }

    private async Task ConsolidateLegacyContractRenewalPermissionsAsync()
    {
        static string Normalize(string? value) => (value ?? string.Empty)
            .Replace("ي", "ی", StringComparison.Ordinal)
            .Replace("ك", "ک", StringComparison.Ordinal)
            .Replace("‌", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

        var targets = await db.Permissions.AsNoTracking()
            .Where(x => x.Code == "ContractRenewal.Create" || x.Code == "ContractRenewal.List")
            .ToDictionaryAsync(x => x.Code, x => x.Id);
        if (!targets.TryGetValue("ContractRenewal.Create", out var createId) ||
            !targets.TryGetValue("ContractRenewal.List", out var listId))
            return;

        // Older databases may still contain one broad permission under a
        // separate "مدیریت قراردادهای کارکنان" module. Fold its assignments
        // into the new Create + List capabilities before removing the old row.
        var legacyPermissions = (await db.Permissions.ToListAsync())
            .Where(x => x.Code != "ContractRenewal.Manage" &&
                        (Normalize(x.Module) == Normalize("مدیریت قراردادهای کارکنان") ||
                         Normalize(x.Title).Contains(Normalize("ثبت و پیگیری تمدید قرارداد"), StringComparison.Ordinal)))
            .ToList();
        if (legacyPermissions.Count > 0)
        {
            var legacyIds = legacyPermissions.Select(x => x.Id).ToArray();
            var legacyRoleRows = await db.RolePermissions
                .Where(x => legacyIds.Contains(x.PermissionId)).ToListAsync();
            var legacyEmployeeRows = await db.EmployeePermissions
                .Where(x => legacyIds.Contains(x.PermissionId)).ToListAsync();

            var existingRolePairs = (await db.RolePermissions.AsNoTracking()
                .Where(x => x.PermissionId == createId || x.PermissionId == listId)
                .Select(x => new { x.RoleId, x.PermissionId }).ToListAsync())
                .Select(x => (x.RoleId, x.PermissionId)).ToHashSet();
            var existingEmployeePairs = (await db.EmployeePermissions.AsNoTracking()
                .Where(x => x.PermissionId == createId || x.PermissionId == listId)
                .Select(x => new { x.EmployeeId, x.PermissionId }).ToListAsync())
                .Select(x => (x.EmployeeId, x.PermissionId)).ToHashSet();

            foreach (var row in legacyRoleRows)
            {
                foreach (var targetId in new[] { createId, listId })
                    if (existingRolePairs.Add((row.RoleId, targetId)))
                        db.RolePermissions.Add(new RolePermission { RoleId = row.RoleId, PermissionId = targetId });
            }

            foreach (var row in legacyEmployeeRows)
            {
                foreach (var targetId in new[] { createId, listId })
                    if (existingEmployeePairs.Add((row.EmployeeId, targetId)))
                        db.EmployeePermissions.Add(new EmployeePermission { EmployeeId = row.EmployeeId, PermissionId = targetId });
            }

            db.RolePermissions.RemoveRange(legacyRoleRows);
            db.EmployeePermissions.RemoveRange(legacyEmployeeRows);
            db.Permissions.RemoveRange(legacyPermissions);
        }

        // A personal assignment of the old all-access compatibility permission
        // is also expanded, so hiding the technical alias never strips access.
        var manageId = await db.Permissions.AsNoTracking()
            .Where(x => x.Code == "ContractRenewal.Manage")
            .Select(x => (int?)x.Id).FirstOrDefaultAsync();
        if (manageId.HasValue)
        {
            var granularIds = await db.Permissions.AsNoTracking()
                .Where(x => x.Code.StartsWith("ContractRenewal.") && x.Code != "ContractRenewal.Manage")
                .Select(x => x.Id).ToListAsync();
            var personalManageRows = await db.EmployeePermissions.AsNoTracking()
                .Where(x => x.PermissionId == manageId.Value)
                .Select(x => x.EmployeeId).Distinct().ToListAsync();
            var existing = (await db.EmployeePermissions.AsNoTracking()
                .Where(x => personalManageRows.Contains(x.EmployeeId) && granularIds.Contains(x.PermissionId))
                .Select(x => new { x.EmployeeId, x.PermissionId }).ToListAsync())
                .Select(x => (x.EmployeeId, x.PermissionId)).ToHashSet();

            foreach (var employeeId in personalManageRows)
                foreach (var permissionId in granularIds)
                    if (existing.Add((employeeId, permissionId)))
                        db.EmployeePermissions.Add(new EmployeePermission { EmployeeId = employeeId, PermissionId = permissionId });
        }

        await db.SaveChangesAsync();
    }

    public async Task<int> SaveRoleAsync(RoleEditModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var code = (model.Code ?? string.Empty).Trim().ToUpperInvariant();
        var title = (model.Title ?? string.Empty).Trim();
        var description = (model.Description ?? string.Empty).Trim();
        var permissionIds = (model.PermissionIds ?? [])
            .Where(x => x > 0).Distinct().ToArray();

        if (!Regex.IsMatch(code, @"^[A-Z0-9][A-Z0-9._-]{1,99}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("کد نقش باید حداقل ۲ و حداکثر ۱۰۰ نویسه و فقط شامل حروف انگلیسی، عدد، نقطه، خط تیره یا زیرخط باشد.");
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("عنوان نقش الزامی است.");
        if (title.Length > 200)
            throw new InvalidOperationException("عنوان نقش نمی‌تواند بیش از ۲۰۰ نویسه باشد.");
        if (description.Length > 1000)
            throw new InvalidOperationException("شرح نقش نمی‌تواند بیش از ۱۰۰۰ نویسه باشد.");

        if (await db.Roles.AnyAsync(x => x.Code == code && x.Id != model.Id))
            throw new InvalidOperationException("این کد نقش قبلاً ثبت شده است؛ کد دیگری انتخاب کنید.");

        var validPermissionIds = await db.Permissions
            .Where(x => permissionIds.Contains(x.Id) && x.Code != "ContractRenewal.Manage")
            .Select(x => x.Id).ToListAsync();
        var validPermissionSet = validPermissionIds.ToHashSet();

        Role role;
        List<RolePermission> currentAssignments = [];
        if (model.Id == 0)
        {
            role = new Role { Code = code, Title = title, Description = description, IsActive = model.IsActive };
            db.Roles.Add(role);
        }
        else
        {
            role = await db.Roles.FindAsync(model.Id) ?? throw new InvalidOperationException("نقش یافت نشد.");
            role.Code = code;
            role.Title = title;
            role.Description = description;
            role.IsActive = model.IsActive;
            currentAssignments = await db.RolePermissions
                .Where(x => x.RoleId == role.Id).ToListAsync();

            // Apply a set difference instead of delete/reinsert-all. This avoids
            // unique-index collisions and preserves unchanged assignment rows.
            db.RolePermissions.RemoveRange(
                currentAssignments.Where(x => !validPermissionSet.Contains(x.PermissionId)));
        }

        var currentPermissionIds = currentAssignments.Select(x => x.PermissionId).ToHashSet();
        foreach (var permissionId in validPermissionIds.Where(x => !currentPermissionIds.Contains(x)))
            db.RolePermissions.Add(new RolePermission { Role = role, PermissionId = permissionId });

        // One SaveChanges keeps the role and its permission set atomic. Empty
        // selections are valid and intentionally create a role with no access.
        await db.SaveChangesAsync();
        return role.Id;
    }
}
