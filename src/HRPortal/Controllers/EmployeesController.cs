using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "AdminOnly")]
public class EmployeesController(HRPortalDbContext db, EmployeeExcelService excel, AuditService audit) : Controller
{
    public async Task<IActionResult> Index(string? q, string? status, string sort = "PersonnelNumber", string dir = "asc")
    {
        var query = db.Employees
            .Include(x => x.OrganizationUnit)
            .Include(x => x.OrganizationDepartment)
            .Include(x => x.OrganizationSection)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x =>
                x.PersonnelNumber.Contains(q) ||
                x.NationalId.Contains(q) ||
                x.FirstName.Contains(q) ||
                x.LastName.Contains(q) ||
                x.Mobile.Contains(q));

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

    public async Task<IActionResult> Create() =>
        View("Form", await BuildFormVmAsync(new Employee()));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FormVm model)
    {
        ValidateEmployee(model.Employee);
        await ValidateOrganizationAssignmentsAsync(model.Employee);

        if (!ModelState.IsValid)
            return View("Form", await BuildFormVmAsync(model.Employee));

        model.Employee.IsSystemUser = true;
        model.Employee.Status = "فعال";
        db.Employees.Add(model.Employee);
        await db.SaveChangesAsync();

        await audit.WriteAsync("ایجاد کارمند", "Employee", model.Employee.Id.ToString(), model.Employee.PersonnelNumber);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var employee = await db.Employees.FindAsync(id);
        return employee is null
            ? NotFound()
            : View("Form", await BuildFormVmAsync(employee));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(FormVm model)
    {
        ValidateEmployee(model.Employee);
        await ValidateOrganizationAssignmentsAsync(model.Employee);

        if (!ModelState.IsValid)
            return View("Form", await BuildFormVmAsync(model.Employee));

        var employee = await db.Employees.FindAsync(model.Employee.Id);
        if (employee is null)
            return NotFound();

        db.Entry(employee).CurrentValues.SetValues(model.Employee);
        employee.IsSystemUser = employee.Status == "فعال";
        employee.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await audit.WriteAsync("ویرایش کارمند", "Employee", employee.Id.ToString(), employee.PersonnelNumber);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var employee = await db.Employees.FindAsync(id);
        if (employee is not null)
        {
            if (await db.OtpChallenges.AnyAsync(x => x.EmployeeId == id) ||
                await db.AuditLogs.AnyAsync(x => x.EmployeeId == id))
            {
                TempData["Error"] = "این کارمند دارای سوابق سامانه است و حذف مستقیم مجاز نیست؛ او را غیرفعال کنید.";
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

    [HttpGet]
    public async Task<IActionResult> Export(string? q, string? status)
    {
        var query = db.Employees.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x =>
                x.PersonnelNumber.Contains(q) ||
                x.NationalId.Contains(q) ||
                x.FirstName.Contains(q) ||
                x.LastName.Contains(q) ||
                x.Mobile.Contains(q));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var bytes = await excel.ExportAsync(query);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Employees-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpGet]
    public IActionResult Import() => View();

    [HttpGet]
    public async Task<IActionResult> Template()
    {
        var bytes = await excel.ExportAsync(db.Employees.Where(x => false));
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Employees-Template.xlsx");
    }

    [HttpPost, ValidateAntiForgeryToken]
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

    private async Task ValidateOrganizationAssignmentsAsync(Employee employee)
    {
        if (employee.OrganizationUnitId.HasValue)
        {
            var unit = await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employee.OrganizationUnitId.Value);
            if (unit is null || unit.RankType != "مدیریت")
                ModelState.AddModelError("Employee.OrganizationUnitId", "واحد سازمانی باید از رده مدیریت انتخاب شود.");
        }

        if (employee.OrganizationDepartmentId.HasValue)
        {
            var department = await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employee.OrganizationDepartmentId.Value);
            if (department is null || department.RankType != "ریاست")
                ModelState.AddModelError("Employee.OrganizationDepartmentId", "اداره سازمانی باید از رده ریاست انتخاب شود.");

            if (employee.OrganizationUnitId.HasValue &&
                (department is null || department.ParentId != employee.OrganizationUnitId.Value))
                ModelState.AddModelError("Employee.OrganizationDepartmentId", "اداره انتخاب‌شده زیرمجموعه واحد انتخاب‌شده نیست.");
        }

        if (employee.OrganizationSectionId.HasValue)
        {
            var section = await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employee.OrganizationSectionId.Value);
            if (section is null || section.RankType != "سرپرستی")
                ModelState.AddModelError("Employee.OrganizationSectionId", "بخش سازمانی باید از رده سرپرستی انتخاب شود.");

            if (employee.OrganizationDepartmentId.HasValue &&
                (section is null || section.ParentId != employee.OrganizationDepartmentId.Value))
                ModelState.AddModelError("Employee.OrganizationSectionId", "بخش انتخاب‌شده زیرمجموعه اداره انتخاب‌شده نیست.");
        }
    }

    private static void ValidateEmployee(Employee employee)
    {
        if (string.IsNullOrWhiteSpace(employee.PersonnelNumber))
            ModelState.AddModelError("Employee.PersonnelNumber", "شماره پرسنلی الزامی است.");
        if (string.IsNullOrWhiteSpace(employee.NationalId))
            ModelState.AddModelError("Employee.NationalId", "کد ملی الزامی است.");
        if (string.IsNullOrWhiteSpace(employee.FirstName))
            ModelState.AddModelError("Employee.FirstName", "نام الزامی است.");
        if (string.IsNullOrWhiteSpace(employee.LastName))
            ModelState.AddModelError("Employee.LastName", "نام خانوادگی الزامی است.");
        if (string.IsNullOrWhiteSpace(employee.Mobile))
            ModelState.AddModelError("Employee.Mobile", "شماره موبایل الزامی است.");
        if (employee.Gender is not ("مرد" or "زن"))
            ModelState.AddModelError("Employee.Gender", "جنسیت را انتخاب کنید.");
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

        return new FormVm
        {
            Employee = employee,
            Units = nodes.Where(x => x.RankType == "مدیریت").ToList(),
            Departments = nodes.Where(x => x.RankType == "ریاست").ToList(),
            Sections = nodes.Where(x => x.RankType == "سرپرستی").ToList()
        };
    }

    public class FormVm
    {
        public Employee Employee { get; set; } = new();
        public List<OrganizationNode> Units { get; set; } = [];
        public List<OrganizationNode> Departments { get; set; } = [];
        public List<OrganizationNode> Sections { get; set; } = [];
    }
}