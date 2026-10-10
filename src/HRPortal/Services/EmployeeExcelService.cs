using ClosedXML.Excel;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class EmployeeExcelService(HRPortalDbContext db)
{
    private static readonly DateTime DefaultContractEndDate =
        new System.Globalization.PersianCalendar().ToDateTime(1405, 12, 29, 0, 0, 0, 0);

    private static readonly string[] Headers =
    [
        "شماره پرسنلی", "شناسه", "کد ملی", "نام", "نام خانوادگی", "موبایل", "جنسیت",
        "واحد سازمانی", "اداره", "بخش", "سمت", "نوع استخدام", "ایمیل", "نام پدر",
        "وضعیت", "تاریخ پایان قرارداد"
    ];

    public async Task<byte[]> ExportAsync(IQueryable<Employee> query)
    {
        var rows = await query.AsNoTracking()
            .Include(x => x.OrganizationUnit)
            .Include(x => x.OrganizationDepartment)
            .Include(x => x.OrganizationSection)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("کارکنان");
        for (var i = 0; i < Headers.Length; i++)
            ws.Cell(1, i + 1).Value = Headers[i];

        for (var r = 0; r < rows.Count; r++)
        {
            var e = rows[r];
            var values = new object?[]
            {
                e.PersonnelNumber, e.Identifier, e.NationalId, e.FirstName, e.LastName,
                e.Mobile, e.Gender, e.OrganizationUnit?.Code, e.OrganizationDepartment?.Code,
                e.OrganizationSection?.Code, e.PositionTitle, NormalizeEmploymentType(e.EmploymentType),
                e.Email, e.FatherName, e.Status,
                e.ContractEndDate.HasValue ? PersianDateService.Format(e.ContractEndDate.Value) : ""
            };
            for (var c = 0; c < values.Length; c++)
                ws.Cell(r + 2, c + 1).Value = values[c]?.ToString() ?? "";
        }

        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<List<string>> ImportAsync(Stream stream)
    {
        var errors = new List<string>();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        var currentRevision = await db.OrganizationStructureRevisions
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        var nodes = currentRevision == null
            ? new List<OrganizationNode>()
            : await db.OrganizationNodes
                .Where(x => x.OrganizationStructureRevisionId == currentRevision.Id && x.IsActive)
                .ToListAsync();

        var unitMap = nodes.Where(x => x.RankType == "مدیریت").ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var departmentMap = nodes.Where(x => x.RankType == "ریاست").ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var sectionMap = nodes.Where(x => x.RankType == "سرپرستی").ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var employees = await db.Employees.ToListAsync();
        var newEmployees = new List<Employee>();
        var seenPersonnelNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var row = 2; row <= lastRow; row++)
        {
            string Cell(int column) => ws.Cell(row, column).GetString().Trim();
            var personnelNumber = Cell(1);
            if (string.IsNullOrWhiteSpace(personnelNumber))
            {
                if (Enumerable.Range(2, Headers.Length - 1).Any(c => !string.IsNullOrWhiteSpace(Cell(c))))
                    errors.Add($"ردیف {row}: شماره پرسنلی الزامی است.");
                continue;
            }

            if (!seenPersonnelNumbers.Add(personnelNumber))
            {
                errors.Add($"ردیف {row}: شماره پرسنلی {personnelNumber} بیش از یک‌بار در فایل آمده است.");
                continue;
            }

            var identifier = Cell(2);
            var nationalId = Cell(3);
            var firstName = Cell(4);
            var lastName = Cell(5);
            var mobile = Cell(6);
            var gender = Cell(7);
            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(nationalId) ||
                string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(mobile) || gender is not ("مرد" or "زن"))
            {
                errors.Add($"ردیف {row}: شناسه، کد ملی، نام، نام خانوادگی، موبایل و جنسیت معتبر الزامی هستند.");
                continue;
            }

            var matches = employees.Where(e =>
                string.Equals(e.PersonnelNumber, personnelNumber, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Identifier, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.NationalId, nationalId, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(e => e.Id).ToList();

            if (matches.Count > 1)
            {
                errors.Add($"ردیف {row}: شماره پرسنلی، شناسه یا کد ملی به کارکنان متفاوتی تعلق دارند؛ ابتدا اطلاعات هویتی را اصلاح کنید.");
                continue;
            }

            var target = matches.SingleOrDefault();
            var identityConflict = employees.Any(e => (target == null || e.Id != target.Id) &&
                (string.Equals(e.PersonnelNumber, personnelNumber, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(e.Identifier, identifier, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(e.NationalId, nationalId, StringComparison.OrdinalIgnoreCase)));
            if (identityConflict)
            {
                errors.Add($"ردیف {row}: شماره پرسنلی، شناسه یا کد ملی با اطلاعات کارمند دیگری تداخل دارد.");
                continue;
            }

            var employmentType = NormalizeEmploymentType(Cell(12));
            if (employmentType is not ("قراردادی" or "غیرقراردادی"))
            {
                errors.Add($"ردیف {row}: نوع استخدام باید «قراردادی» یا «غیرقراردادی» باشد.");
                continue;
            }

            DateTime? contractEndDate = null;
            if (employmentType == "قراردادی")
            {
                var rawDate = ReadDateText(ws.Cell(row, 16));
                if (string.IsNullOrWhiteSpace(rawDate))
                    contractEndDate = DefaultContractEndDate;
                else if (PersianDateService.TryParse(rawDate, out var parsed))
                    contractEndDate = parsed.Date;
                else
                {
                    errors.Add($"ردیف {row}: تاریخ پایان قرارداد معتبر نیست؛ نمونه صحیح ۱۴۰۵/۱۲/۲۹ است.");
                    continue;
                }
            }

            int? unitId = null, departmentId = null, sectionId = null;
            var unitCode = Cell(8);
            var departmentCode = Cell(9);
            var sectionCode = Cell(10);
            if (!string.IsNullOrWhiteSpace(unitCode))
            {
                if (!unitMap.TryGetValue(unitCode, out var unit))
                {
                    errors.Add($"ردیف {row}: کد واحد سازمانی «{unitCode}» در ساختار جاری یافت نشد.");
                    continue;
                }
                unitId = unit.Id;
            }
            if (!string.IsNullOrWhiteSpace(departmentCode))
            {
                if (!departmentMap.TryGetValue(departmentCode, out var department) ||
                    (unitId.HasValue && department.ParentId != unitId.Value))
                {
                    errors.Add($"ردیف {row}: کد اداره «{departmentCode}» معتبر نیست یا زیرمجموعه واحد انتخاب‌شده نیست.");
                    continue;
                }
                departmentId = department.Id;
            }
            if (!string.IsNullOrWhiteSpace(sectionCode))
            {
                if (!sectionMap.TryGetValue(sectionCode, out var section) ||
                    (departmentId.HasValue && section.ParentId != departmentId.Value))
                {
                    errors.Add($"ردیف {row}: کد بخش «{sectionCode}» معتبر نیست یا زیرمجموعه اداره انتخاب‌شده نیست.");
                    continue;
                }
                sectionId = section.Id;
            }

            var isNew = target is null;
            target ??= new Employee
            {
                PersonnelNumber = personnelNumber,
                CreatedAt = DateTime.UtcNow,
                IsSystemUser = true,
                Status = "فعال"
            };

            target.PersonnelNumber = personnelNumber;
            target.Identifier = identifier;
            target.NationalId = nationalId;
            target.FirstName = firstName;
            target.LastName = lastName;
            target.Mobile = mobile;
            target.Gender = gender;
            target.OrganizationUnitId = unitId;
            target.OrganizationDepartmentId = departmentId;
            target.OrganizationSectionId = sectionId;
            target.PositionTitle = EmptyToNull(Cell(11));
            target.EmploymentType = employmentType;
            target.ContractEndDate = contractEndDate;
            target.Email = EmptyToNull(Cell(13));
            target.FatherName = EmptyToNull(Cell(14));
            target.Status = string.IsNullOrWhiteSpace(Cell(15)) ? (isNew ? "فعال" : target.Status) : Cell(15);
            target.IsSystemUser = target.Status == "فعال";
            target.UpdatedAt = DateTime.UtcNow;

            if (isNew)
            {
                db.Employees.Add(target);
                employees.Add(target);
                newEmployees.Add(target);
            }
        }

        await db.SaveChangesAsync();

        var defaultRole = await db.Roles.FirstOrDefaultAsync(x => x.Code == "EMPLOYEE" && x.IsActive);
        if (defaultRole is not null && newEmployees.Count > 0)
        {
            foreach (var employee in newEmployees)
                db.EmployeeRoles.Add(new EmployeeRole { EmployeeId = employee.Id, RoleId = defaultRole.Id });
            await db.SaveChangesAsync();
        }

        return errors;
    }

    private static string NormalizeEmploymentType(string? value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized is "غیرقراردادی" or "غیر قراردادی" or "غیرقراردادی " or "Non-contractual")
            return "غیرقراردادی";
        if (normalized is "قراردادی" or "Contractual" or "")
            return "قراردادی";
        return normalized;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string ReadDateText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        if (cell.DataType == XLDataType.DateTime)
        {
            var date = cell.GetDateTime();
            return PersianDateService.Format(date);
        }
        return cell.GetString().Trim();
    }
}
