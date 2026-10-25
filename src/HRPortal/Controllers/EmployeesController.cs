using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "Employees.View")]
public class EmployeesController(HRPortalDbContext db, EmployeeExcelService excel, AuditService audit, RoleService roles) : Controller
{
    public async Task<IActionResult> Index(string? q, string? status, string sort = "PersonnelNumber", string dir = "asc")
    {
        var query = db.Employees
            .Include(x => x.OrganizationUnit)
            .Include(x => x.OrganizationDepartment)
            .Include(x => x.OrganizationSection)
            .Include(x => x.OrganizationPosition)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x =>
                x.PersonnelNumber.Contains(q) ||
                x.Identifier.Contains(q) ||
                x.NationalId.Contains(q) ||
                x.FirstName.Contains(q) ||
                x.LastName.Contains(q) ||
                x.Mobile.Contains(q) ||
                x.Gender.Contains(q) ||
                x.Status.Contains(q) ||
                (x.OrganizationUnit != null && x.OrganizationUnit.Title.Contains(q)) ||
                (x.OrganizationDepartment != null && x.OrganizationDepartment.Title.Contains(q)) ||
                (x.OrganizationSection != null && x.OrganizationSection.Title.Contains(q)) ||
                (x.OrganizationPosition != null && x.OrganizationPosition.Title.Contains(q)));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        query = sort switch
        {
            "LastName" => dir == "desc" ? query.OrderByDescending(x => x.LastName) : query.OrderBy(x => x.LastName),
            "Status" => dir == "desc" ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            _ => dir == "desc" ? query.OrderByDescending(x => x.PersonnelNumber) : query.OrderBy(x => x.PersonnelNumber)
        };

        ViewBag.Q = q;
        ViewBag.Status = status;
        ViewBag.Sort = sort;
        ViewBag.Dir = dir;
        return View(await query.ToListAsync());
    }

    [Authorize(Policy = "Employees.Create")]
    public async Task<IActionResult> Create() =>
        View("Form", await BuildFormVmAsync(new Employee()));

    [HttpPost, Authorize(Policy = "Employees.Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Employee employee, bool isNonContractual = false, string? contractEndDatePersian = null)
    {
        ClearGeneratedRequiredErrors();
        NormalizeEmployee(employee);
        ApplyEmploymentDetails(employee, isNonContractual, contractEndDatePersian);
        ValidateEmployee(employee);
        await ValidatePositionTitleAsync(employee);
        await ValidateOrganizationAssignmentsAsync(employee);

        if (!ModelState.IsValid)
            return View("Form", await BuildFormVmAsync(employee));

        employee.IsSystemUser = true;
        employee.Status = "فعال";
        employee.CreatedAt = DateTime.UtcNow;
        employee.UpdatedAt = DateTime.UtcNow;

        if (await db.Employees.AnyAsync(x => x.PersonnelNumber == employee.PersonnelNumber))
            ModelState.AddModelError("PersonnelNumber", "این شماره پرسنلی قبلاً ثبت شده است.");
        if (await db.Employees.AnyAsync(x => x.Identifier == employee.Identifier))
            ModelState.AddModelError("Identifier", "این شناسه قبلاً ثبت شده است.");
        if (await db.Employees.AnyAsync(x => x.NationalId == employee.NationalId))
            ModelState.AddModelError("NationalId", "این کد ملی قبلاً ثبت شده است.");

        if (!ModelState.IsValid)
            return View("Form", await BuildFormVmAsync(employee));

        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var defaultRole = await db.Roles.FirstOrDefaultAsync(x => x.Code == "EMPLOYEE" && x.IsActive);
        if (defaultRole is not null)
            await roles.SaveEmployeeRolesAsync(employee.Id, new[] { defaultRole.Id });

        await audit.WriteAsync("ایجاد کارمند", "Employee", employee.Id.ToString(), employee.PersonnelNumber);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = "Employees.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var employee = await db.Employees.FindAsync(id);
        return employee is null
            ? NotFound()
            : View("Form", await BuildFormVmAsync(employee));
    }

    [HttpPost, Authorize(Policy = "Employees.Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Employee employee, bool isNonContractual = false, string? contractEndDatePersian = null)
    {
        ClearGeneratedRequiredErrors();
        NormalizeEmployee(employee);
        ApplyEmploymentDetails(employee, isNonContractual, contractEndDatePersian);
        ValidateEmployee(employee);
        await ValidatePositionTitleAsync(employee);
        await ValidateOrganizationAssignmentsAsync(employee);

        if (!ModelState.IsValid)
            return View("Form", await BuildFormVmAsync(employee));

        var existing = await db.Employees.FindAsync(employee.Id);
        if (existing is null)
            return NotFound();

        if (await db.Employees.AnyAsync(x => x.Id != employee.Id && x.PersonnelNumber == employee.PersonnelNumber))
            ModelState.AddModelError("PersonnelNumber", "این شماره پرسنلی قبلاً برای کارمند دیگری ثبت شده است.");
        if (await db.Employees.AnyAsync(x => x.Id != employee.Id && x.Identifier == employee.Identifier))
            ModelState.AddModelError("Identifier", "این شناسه قبلاً برای کارمند دیگری ثبت شده است.");
        if (await db.Employees.AnyAsync(x => x.Id != employee.Id && x.NationalId == employee.NationalId))
            ModelState.AddModelError("NationalId", "این کد ملی قبلاً برای کارمند دیگری ثبت شده است.");

        if (!ModelState.IsValid)
            return View("Form", await BuildFormVmAsync(employee));

        // Security-only state is not editable through the employee profile form.
        var initialPasswordResetRequired = existing.MustChangeInitialPassword;
        var personalPasswordHash = existing.PersonalPasswordHash;
        db.Entry(existing).CurrentValues.SetValues(employee);
        existing.PersonalPasswordHash = personalPasswordHash;
        existing.MustChangeInitialPassword = initialPasswordResetRequired;
        existing.IsSystemUser = existing.Status == "فعال";
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await audit.WriteAsync("ویرایش کارمند", "Employee", existing.Id.ToString(), existing.PersonnelNumber);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = "Employees.Deactivate"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var employee = await db.Employees.FindAsync(id);
        if (employee is not null)
        {
            employee.Status = "غیرفعال";
            employee.IsSystemUser = false;
            employee.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await audit.WriteAsync("غیرفعال‌سازی کارمند", "Employee", id.ToString());
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = "Employees.Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var employee = await db.Employees.FindAsync(id);
        if (employee is not null)
        {
            if (await db.OtpChallenges.AnyAsync(x => x.EmployeeId == id) ||
                await db.AuditLogs.AnyAsync(x => x.EmployeeId == id) ||
                await db.WorkflowInstances.AnyAsync(x => x.RequesterEmployeeId == id) ||
                await db.OrganizationNodes.AnyAsync(x => x.PositionHolderEmployeeId == id))
            {
                TempData["Error"] = "این کارمند دارای سوابق سامانه یا متصدی یک جایگاه سازمانی است؛ حذف مستقیم مجاز نیست و باید او را غیرفعال کنید یا ابتدا متصدی جایگاه را تغییر دهید.";
            }
            else
            {
                db.Employees.Remove(employee);
                await db.SaveChangesAsync();
                await audit.WriteAsync("حذف کارمند", "Employee", id.ToString());
            }
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAction(int[] selectedIds, string action, string? gender)
    {
        if (selectedIds is null || selectedIds.Length == 0)
        {
            TempData["Error"] = "حداقل یک کارمند را انتخاب کنید.";
            return RedirectToAction(nameof(Index));
        }

        var isAdmin = User.HasClaim("IsAdmin", "1");
        bool Allowed(string permission) => isAdmin || User.HasClaim("Permission", permission);

        if (action == "deactivate" && !Allowed("Employees.Deactivate"))
        {
            TempData["Error"] = "شما مجوز غیرفعال‌سازی کارکنان را ندارید.";
            return RedirectToAction(nameof(Index));
        }

        if (action == "delete" && !Allowed("Employees.Delete"))
        {
            TempData["Error"] = "شما مجوز حذف کارکنان را ندارید.";
            return RedirectToAction(nameof(Index));
        }

        if (action == "gender" && !Allowed("Employees.Edit"))
        {
            TempData["Error"] = "شما مجوز ویرایش کارکنان را ندارید.";
            return RedirectToAction(nameof(Index));
        }

        if (action is not ("deactivate" or "delete" or "gender"))
        {
            TempData["Error"] = "عملیات گروهی نامعتبر است.";
            return RedirectToAction(nameof(Index));
        }

        if (action == "gender" && gender is not ("مرد" or "زن"))
        {
            TempData["Error"] = "جنسیت جدید را انتخاب کنید.";
            return RedirectToAction(nameof(Index));
        }

        var employees = await db.Employees
            .Where(x => selectedIds.Contains(x.Id))
            .ToListAsync();

        var success = 0;
        var blockedDelete = 0;

        foreach (var employee in employees)
        {
            if (action == "deactivate")
            {
                if (employee.Status != "غیرفعال")
                {
                    employee.Status = "غیرفعال";
                    employee.IsSystemUser = false;
                    employee.UpdatedAt = DateTime.UtcNow;
                    success++;
                    await audit.WriteAsync("غیرفعال‌سازی گروهی کارمند", "Employee", employee.Id.ToString(), employee.PersonnelNumber);
                }
            }
            else if (action == "gender")
            {
                employee.Gender = gender!;
                employee.UpdatedAt = DateTime.UtcNow;
                success++;
                await audit.WriteAsync("تغییر گروهی جنسیت کارمند", "Employee", employee.Id.ToString(), employee.PersonnelNumber);
            }
            else
            {
                var hasHistory = await db.OtpChallenges.AnyAsync(x => x.EmployeeId == employee.Id) ||
                                 await db.AuditLogs.AnyAsync(x => x.EmployeeId == employee.Id) ||
                                 await db.WorkflowInstances.AnyAsync(x => x.RequesterEmployeeId == employee.Id) ||
                                 await db.OrganizationNodes.AnyAsync(x => x.PositionHolderEmployeeId == employee.Id);

                if (hasHistory)
                {
                    blockedDelete++;
                    continue;
                }

                db.Employees.Remove(employee);
                success++;
                await audit.WriteAsync("حذف گروهی کارمند", "Employee", employee.Id.ToString(), employee.PersonnelNumber);
            }
        }

        await db.SaveChangesAsync();

        TempData["Success"] = action switch
        {
            "deactivate" => $"عملیات غیرفعال‌سازی انجام شد. {success} نفر به‌روزرسانی شدند.",
            "gender" => $"جنسیت {success} نفر به «{gender}» تغییر کرد.",
            _ => blockedDelete == 0
                ? $"حذف {success} نفر انجام شد."
                : $"حذف {success} نفر انجام شد؛ {blockedDelete} نفر به دلیل داشتن سوابق سامانه حذف نشدند و باید غیرفعال شوند."
        };

        return RedirectToAction(nameof(Index));
    }

    [HttpGet, Authorize(Policy = "Employees.ImportExport")]
    public async Task<IActionResult> Export(string? q, string? status)
    {
        var query = db.Employees.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x =>
                x.PersonnelNumber.Contains(q) ||
                x.Identifier.Contains(q) ||
                x.NationalId.Contains(q) ||
                x.FirstName.Contains(q) ||
                x.LastName.Contains(q) ||
                x.Mobile.Contains(q) ||
                x.Gender.Contains(q) ||
                x.Status.Contains(q) ||
                (x.OrganizationUnit != null && x.OrganizationUnit.Title.Contains(q)) ||
                (x.OrganizationDepartment != null && x.OrganizationDepartment.Title.Contains(q)) ||
                (x.OrganizationSection != null && x.OrganizationSection.Title.Contains(q)));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var bytes = await excel.ExportAsync(query);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Employees-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpGet, Authorize(Policy = "Employees.ImportExport")]
    public IActionResult Import() => View();

    [HttpGet, Authorize(Policy = "Employees.ImportExport")]
    public async Task<IActionResult> Template()
    {
        var bytes = await excel.ExportAsync(db.Employees.Where(x => false));
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Employees-Template.xlsx");
    }

    [HttpPost, Authorize(Policy = "Employees.ImportExport"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError("", "فایل Excel را انتخاب کنید.");
            return View();
        }

        try
        {
            var errors = await excel.ImportAsync(file.OpenReadStream());
            ViewBag.Errors = errors;
            ViewBag.Success = errors.Count == 0
                ? "اطلاعات با موفقیت وارد شد."
                : "فایل پردازش شد؛ موارد خطادار در همین صفحه نمایش داده شده‌اند.";
        }
        catch (Exception ex)
        {
            ViewBag.Errors = new List<string> { "خطای فایل: " + ex.Message };
        }

        return View();
    }

    private void ClearGeneratedRequiredErrors()
    {
        foreach (var key in new[] { "PersonnelNumber", "Identifier", "NationalId", "FirstName", "LastName", "Mobile", "Gender" })
        {
            if (ModelState.TryGetValue(key, out var state))
                state.Errors.Clear();
        }
    }

    private static void NormalizeEmployee(Employee employee)
    {
        employee.PersonnelNumber = employee.PersonnelNumber?.Trim() ?? "";
        employee.Identifier = employee.Identifier?.Trim() ?? "";
        employee.NationalId = employee.NationalId?.Trim() ?? "";
        employee.FirstName = employee.FirstName?.Trim() ?? "";
        employee.LastName = employee.LastName?.Trim() ?? "";
        employee.Mobile = employee.Mobile?.Trim() ?? "";
        employee.Gender = employee.Gender?.Trim() ?? "";
        employee.FatherName = employee.FatherName?.Trim();
        employee.PositionTitle = employee.PositionTitle?.Trim();
        employee.EmploymentType = employee.EmploymentType?.Trim();
        employee.Email = employee.Email?.Trim();
    }

    private void ApplyEmploymentDetails(Employee employee, bool isNonContractual, string? contractEndDatePersian)
    {
        if (isNonContractual)
        {
            employee.EmploymentType = "غیرقراردادی";
            employee.ContractEndDate = null;
            ModelState.Remove(nameof(Employee.ContractEndDate));
            ModelState.Remove("contractEndDatePersian");
            return;
        }

        employee.EmploymentType = "قراردادی";
        if (string.IsNullOrWhiteSpace(contractEndDatePersian))
        {
            employee.ContractEndDate ??= new System.Globalization.PersianCalendar().ToDateTime(1405, 12, 29, 0, 0, 0, 0);
            return;
        }

        if (PersianDateService.TryParse(contractEndDatePersian, out var parsed))
        {
            employee.ContractEndDate = parsed.Date;
            ModelState.Remove("contractEndDatePersian");
        }
        else
        {
            ModelState.AddModelError("contractEndDatePersian", "تاریخ پایان قرارداد را با تقویم شمسی وارد کنید.");
        }
    }

    private async Task ValidateOrganizationAssignmentsAsync(Employee employee)
    {
        if (employee.OrganizationUnitId.HasValue)
        {
            var unit = await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employee.OrganizationUnitId.Value);
            if (unit is null || unit.RankType != "مدیریت")
                ModelState.AddModelError("OrganizationUnitId", "واحد سازمانی باید از رده مدیریت انتخاب شود.");
        }

        if (employee.OrganizationDepartmentId.HasValue)
        {
            var department = await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employee.OrganizationDepartmentId.Value);
            if (department is null || department.RankType != "ریاست")
                ModelState.AddModelError("OrganizationDepartmentId", "اداره سازمانی باید از رده ریاست انتخاب شود.");

            if (employee.OrganizationUnitId.HasValue &&
                (department is null || department.ParentId != employee.OrganizationUnitId.Value))
                ModelState.AddModelError("OrganizationDepartmentId", "اداره انتخاب‌شده زیرمجموعه واحد انتخاب‌شده نیست.");
        }

        if (employee.OrganizationSectionId.HasValue)
        {
            var section = await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employee.OrganizationSectionId.Value);
            if (section is null || section.RankType != "سرپرستی")
                ModelState.AddModelError("OrganizationSectionId", "بخش سازمانی باید از رده سرپرستی انتخاب شود.");

            if (employee.OrganizationDepartmentId.HasValue &&
                (section is null || section.ParentId != employee.OrganizationDepartmentId.Value))
                ModelState.AddModelError("OrganizationSectionId", "بخش انتخاب‌شده زیرمجموعه اداره انتخاب‌شده نیست.");
        }

        if (employee.OrganizationPositionId.HasValue)
        {
            var position = await db.OrganizationNodes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == employee.OrganizationPositionId.Value &&
                    x.IsActive && x.RankType == "مدیرعامل");
            if (position is null)
            {
                ModelState.AddModelError("OrganizationPositionId", "جایگاه ویژه باید یک جایگاه فعال با رده مدیرعامل باشد.");
            }
            else if (employee.Status == "فعال" && await db.Employees.AnyAsync(other =>
                other.Id != employee.Id && other.Status == "فعال" &&
                other.OrganizationPositionId.HasValue &&
                db.OrganizationNodes.Any(node => node.Id == other.OrganizationPositionId.Value &&
                    node.RankType == "مدیرعامل")))
            {
                ModelState.AddModelError("OrganizationPositionId", "در میان کارکنان فعال، فقط یک نفر می‌تواند رده مدیرعامل داشته باشد.");
            }
        }
    }

    private async Task ValidatePositionTitleAsync(Employee employee)
    {
        if (string.IsNullOrWhiteSpace(employee.PositionTitle)) return;
        if (await db.OrganizationPositionDefinitions.AnyAsync(x => x.IsActive && x.Title == employee.PositionTitle))
            return;

        var existingTitle = employee.Id > 0
            ? await db.Employees.AsNoTracking().Where(x => x.Id == employee.Id).Select(x => x.PositionTitle).FirstOrDefaultAsync()
            : null;
        if (!string.Equals(existingTitle, employee.PositionTitle, StringComparison.Ordinal))
            ModelState.AddModelError(nameof(Employee.PositionTitle), "سمت جدید باید از میان پست‌های سازمانی فعال انتخاب شود.");
    }

    private void ValidateEmployee(Employee employee)
    {
        if (string.IsNullOrWhiteSpace(employee.PersonnelNumber))
            ModelState.AddModelError("PersonnelNumber", "شماره پرسنلی الزامی است.");
        if (string.IsNullOrWhiteSpace(employee.Identifier))
            ModelState.AddModelError("Identifier", "شناسه الزامی است.");
        if (string.IsNullOrWhiteSpace(employee.NationalId))
            ModelState.AddModelError("NationalId", "کد ملی الزامی است.");
        if (string.IsNullOrWhiteSpace(employee.FirstName))
            ModelState.AddModelError("FirstName", "نام الزامی است.");
        if (string.IsNullOrWhiteSpace(employee.LastName))
            ModelState.AddModelError("LastName", "نام خانوادگی الزامی است.");
        if (string.IsNullOrWhiteSpace(employee.Mobile))
            ModelState.AddModelError("Mobile", "شماره موبایل الزامی است.");
        if (employee.Gender is not ("مرد" or "زن"))
            ModelState.AddModelError("Gender", "جنسیت را انتخاب کنید.");
    }

    private async Task<FormVm> BuildFormVmAsync(Employee employee)
    {
        var revision = await db.OrganizationStructureRevisions
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync();

        var nodes = revision is null
            ? new List<OrganizationNode>()
            : await db.OrganizationNodes
                .Where(x => x.OrganizationStructureRevisionId == revision.Id && x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Title)
                .ToListAsync();

        // Previously assigned organization nodes can belong to an earlier finalized revision
        // or become inactive. Preserve them as selectable current values on the edit form
        // instead of silently rendering an empty dropdown (and erasing the assignment).
        var assignedIds = new[] {
            employee.OrganizationUnitId, employee.OrganizationDepartmentId,
            employee.OrganizationSectionId
        }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        foreach (var id in assignedIds)
        {
            if (nodes.Any(n => n.Id == id)) continue;
            var previous = await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id);
            if (previous is not null) nodes.Add(previous);
        }

        var ceoNodes = nodes.Where(x => x.RankType == "مدیرعامل").ToList();
        if (employee.OrganizationPositionId.HasValue &&
            ceoNodes.All(x => x.Id != employee.OrganizationPositionId.Value))
        {
            var existingPosition = await db.OrganizationNodes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == employee.OrganizationPositionId.Value && x.RankType == "مدیرعامل");
            if (existingPosition is not null) ceoNodes.Add(existingPosition);
        }

        var positions = await db.OrganizationPositionDefinitions.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Title)
            .ToListAsync();
        if (!string.IsNullOrWhiteSpace(employee.PositionTitle) &&
            !positions.Any(x => x.Title == employee.PositionTitle))
        {
            var historicalPosition = await db.OrganizationPositionDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Title == employee.PositionTitle);
            positions.Add(historicalPosition ?? new OrganizationPositionDefinition
            {
                Code = "قدیمی",
                Title = employee.PositionTitle,
                IsActive = false,
                SortOrder = int.MaxValue,
                Description = "عنوان قدیمی کارمند؛ برای سوابق نگه داشته شده است."
            });
        }

        return new FormVm
        {
            Employee = employee,
            Positions = positions.OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToList(),
            Units = nodes.Where(x => x.RankType == "مدیریت").ToList(),
            Departments = nodes.Where(x => x.RankType == "ریاست").ToList(),
            Sections = nodes.Where(x => x.RankType == "سرپرستی").ToList(),
            SpecialPositions = ceoNodes
        };
    }

    public class FormVm
    {
        public Employee Employee { get; set; } = new();
        public List<OrganizationNode> Units { get; set; } = [];
        public List<OrganizationNode> Departments { get; set; } = [];
        public List<OrganizationNode> Sections { get; set; } = [];
        public List<OrganizationNode> SpecialPositions { get; set; } = [];
        public List<OrganizationPositionDefinition> Positions { get; set; } = [];
    }
}
