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
        "وضعیت", "تاریخ پایان قرارداد", "ارزیاب عملکرد", "رده ارزیابی عملکرد",
        "تاریخ استخدام", "تاریخ تولد", "شماره شناسنامه", "محل صدور", "آدرس",
        "وضعیت نظام وظیفه", "وضعیت تأهل", "تعداد اولاد", "شماره بیمه", "مهارت", "نوع شغل"
    ];

    public async Task<byte[]> ExportAsync(IQueryable<Employee> query)
    {
        var rows = await query.AsNoTracking()
            .Include(x => x.OrganizationUnit)
            .Include(x => x.OrganizationDepartment)
            .Include(x => x.OrganizationSection)
            .ToListAsync();

        var ranks = await db.MonthlyPerformanceRanks.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Code);
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
                e.ContractEndDate.HasValue ? PersianDateService.Format(e.ContractEndDate.Value) : "",
                e.IsPerformanceEvaluator ? "بله" : "خیر",
                e.PerformanceRankId.HasValue ? ranks.GetValueOrDefault(e.PerformanceRankId.Value, "") : "",
                e.HireDate.HasValue ? PersianDateService.Format(e.HireDate.Value) : "",
                e.BirthDate.HasValue ? PersianDateService.Format(e.BirthDate.Value) : "",
                e.BirthCertificateNumber ?? "", e.BirthCertificateIssuePlace ?? "", e.Address ?? "",
                e.MilitaryServiceStatus ?? "", e.MaritalStatus ?? "",
                e.ChildrenCount?.ToString() ?? "", e.InsuranceNumber ?? "", e.Skill ?? "", e.JobType ?? ""
            };
            for (var c = 0; c < values.Length; c++)
                ws.Cell(r + 2, c + 1).Value = values[c]?.ToString() ?? "";
        }

        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        AddGuideWorksheet(wb);
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
        var headerColumns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastHeaderColumn = ws.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var column = 1; column <= lastHeaderColumn; column++)
        {
            var header = NormalizeHeader(ws.Cell(1, column).GetString());
            if (!string.IsNullOrWhiteSpace(header) && !headerColumns.ContainsKey(header))
                headerColumns.Add(header, column);
        }

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
        var activePositionTitles = await db.OrganizationPositionDefinitions.AsNoTracking()
            .Where(x => x.IsActive).Select(x => x.Title).ToListAsync();
        var activePositionSet = activePositionTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var performanceRanks = await db.MonthlyPerformanceRanks.AsNoTracking()
            .Where(x => x.IsActive).ToListAsync();
        var employees = await db.Employees.ToListAsync();
        var newEmployees = new List<Employee>();
        var seenPersonnelNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var row = 2; row <= lastRow; row++)
        {
            string Cell(int column) => ws.Cell(row, column).GetString().Trim();
            int HeaderColumn(string title) => headerColumns.GetValueOrDefault(NormalizeHeader(title));
            bool HasHeader(string title) => HeaderColumn(title) > 0;
            string HeaderCell(string title)
            {
                var column = HeaderColumn(title);
                return column > 0 ? Cell(column) : "";
            }
            var personnelNumber = Cell(1);
            if (string.IsNullOrWhiteSpace(personnelNumber))
            {
                if (Enumerable.Range(2, Math.Max(0, lastHeaderColumn - 1)).Any(c => !string.IsNullOrWhiteSpace(Cell(c))))
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
                .Distinct().ToList();

            if (matches.Count > 1)
            {
                errors.Add($"ردیف {row}: شماره پرسنلی، شناسه یا کد ملی به کارکنان متفاوتی تعلق دارند؛ ابتدا اطلاعات هویتی را اصلاح کنید.");
                continue;
            }

            var target = matches.SingleOrDefault();
            var identityConflict = employees.Any(e => (target == null || !object.ReferenceEquals(e, target)) &&
                (string.Equals(e.PersonnelNumber, personnelNumber, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(e.Identifier, identifier, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(e.NationalId, nationalId, StringComparison.OrdinalIgnoreCase)));
            if (identityConflict)
            {
                errors.Add($"ردیف {row}: شماره پرسنلی، شناسه یا کد ملی با اطلاعات کارمند دیگری تداخل دارد.");
                continue;
            }

            var positionTitle = Cell(11);
            if (!string.IsNullOrWhiteSpace(positionTitle) &&
                !activePositionSet.Contains(positionTitle) &&
                (target is null || !string.Equals(target.PositionTitle, positionTitle, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"ردیف {row}: سمت «{positionTitle}» در میان پست‌های فعال ثبت نشده است؛ کد یا عنوان پست را اصلاح کنید.");
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

            // New optional fields are resolved by header names, not fixed positions.
            // If a legacy workbook does not contain a new column, the existing value
            // is deliberately retained rather than overwritten with null.
            var hireDateColumn = HeaderColumn("تاریخ استخدام");
            var birthDateColumn = HeaderColumn("تاریخ تولد");
            DateTime? hireDate = null, birthDate = null;
            if (hireDateColumn > 0)
            {
                var raw = ReadDateText(ws.Cell(row, hireDateColumn));
                if (!string.IsNullOrWhiteSpace(raw) && !PersianDateService.TryParse(raw, out var parsedHire))
                {
                    errors.Add($"ردیف {row}: تاریخ استخدام معتبر نیست؛ نمونه صحیح ۱۴۰۰/۰۱/۰۱ است.");
                    continue;
                }
                else if (!string.IsNullOrWhiteSpace(raw))
                    hireDate = parsedHire.Date;
            }
            if (birthDateColumn > 0)
            {
                var raw = ReadDateText(ws.Cell(row, birthDateColumn));
                if (!string.IsNullOrWhiteSpace(raw) && !PersianDateService.TryParse(raw, out var parsedBirth))
                {
                    errors.Add($"ردیف {row}: تاریخ تولد معتبر نیست؛ نمونه صحیح ۱۳۷۰/۰۱/۰۱ است.");
                    continue;
                }
                else if (!string.IsNullOrWhiteSpace(raw))
                    birthDate = parsedBirth.Date;
            }

            var birthCertificateNumberColumn = HeaderColumn("شماره شناسنامه");
            var birthCertificateIssuePlaceColumn = HeaderColumn("محل صدور");
            var addressColumn = HeaderColumn("آدرس");
            var militaryStatusColumn = HeaderColumn("وضعیت نظام وظیفه");
            var maritalStatusColumn = HeaderColumn("وضعیت تأهل");
            var childrenCountColumn = HeaderColumn("تعداد اولاد");
            var insuranceNumberColumn = HeaderColumn("شماره بیمه");
            var skillColumn = HeaderColumn("مهارت");
            var jobTypeColumn = HeaderColumn("نوع شغل");

            var birthCertificateNumber = HeaderCell("شماره شناسنامه");
            var birthCertificateIssuePlace = HeaderCell("محل صدور");
            var address = HeaderCell("آدرس");
            var militaryStatus = HeaderCell("وضعیت نظام وظیفه");
            var maritalStatus = HeaderCell("وضعیت تأهل");
            var childrenRaw = HeaderCell("تعداد اولاد");
            var insuranceNumber = HeaderCell("شماره بیمه");
            var skill = HeaderCell("مهارت");
            var jobType = HeaderCell("نوع شغل");

            if (!ValidateOptionalText(birthCertificateNumber, 30, "شماره شناسنامه", row, errors) ||
                !ValidateOptionalText(birthCertificateIssuePlace, 150, "محل صدور", row, errors) ||
                !ValidateOptionalText(address, 1000, "آدرس", row, errors) ||
                !ValidateOptionalText(insuranceNumber, 50, "شماره بیمه", row, errors) ||
                !ValidateOptionalText(skill, 1000, "مهارت", row, errors) ||
                !ValidateOptionalText(jobType, 100, "نوع شغل", row, errors))
                continue;

            if (militaryStatusColumn > 0 && !string.IsNullOrWhiteSpace(militaryStatus) &&
                militaryStatus is not ("انجام داده" or "معاف" or "غیبت" or "در حال انجام" or "خرید خدمت"))
            {
                errors.Add($"ردیف {row}: وضعیت نظام وظیفه باید یکی از گزینه‌های انجام داده، معاف، غیبت، در حال انجام یا خرید خدمت باشد.");
                continue;
            }

            if (maritalStatusColumn > 0 && !string.IsNullOrWhiteSpace(maritalStatus))
            {
                maritalStatus = maritalStatus.Replace("متأهل", "متاهل", StringComparison.Ordinal);
                if (maritalStatus is not ("مجرد" or "متاهل"))
                {
                    errors.Add($"ردیف {row}: وضعیت تأهل باید «مجرد» یا «متاهل» باشد.");
                    continue;
                }
            }

            int? childrenCount = null;
            if (childrenCountColumn > 0 && !string.IsNullOrWhiteSpace(childrenRaw))
            {
                if (!int.TryParse(NormalizeDigits(childrenRaw), out var parsedChildren) ||
                    parsedChildren is < 0 or > 30)
                {
                    errors.Add($"ردیف {row}: تعداد اولاد باید عددی بین صفر تا ۳۰ باشد.");
                    continue;
                }
                childrenCount = parsedChildren;
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
            target.PositionTitle = EmptyToNull(positionTitle);
            target.EmploymentType = employmentType;
            target.ContractEndDate = contractEndDate;
            target.Email = EmptyToNull(Cell(13));
            target.FatherName = EmptyToNull(Cell(14));
            target.Status = string.IsNullOrWhiteSpace(Cell(15)) ? (isNew ? "فعال" : target.Status) : Cell(15);
            if (hireDateColumn > 0) target.HireDate = hireDate;
            if (birthDateColumn > 0) target.BirthDate = birthDate;
            if (birthCertificateNumberColumn > 0) target.BirthCertificateNumber = EmptyToNull(birthCertificateNumber);
            if (birthCertificateIssuePlaceColumn > 0) target.BirthCertificateIssuePlace = EmptyToNull(birthCertificateIssuePlace);
            if (addressColumn > 0) target.Address = EmptyToNull(address);
            if (militaryStatusColumn > 0) target.MilitaryServiceStatus = EmptyToNull(militaryStatus);
            if (maritalStatusColumn > 0) target.MaritalStatus = EmptyToNull(maritalStatus);
            if (childrenCountColumn > 0) target.ChildrenCount = childrenCount;
            if (insuranceNumberColumn > 0) target.InsuranceNumber = EmptyToNull(insuranceNumber);
            if (skillColumn > 0) target.Skill = EmptyToNull(skill);
            if (jobTypeColumn > 0) target.JobType = EmptyToNull(jobType);
            var rankCell = Cell(18);
            if (!string.IsNullOrWhiteSpace(rankCell))
            {
                var rank = performanceRanks.FirstOrDefault(x =>
                    x.Code.Equals(rankCell, StringComparison.OrdinalIgnoreCase) ||
                    x.Title.Equals(rankCell, StringComparison.OrdinalIgnoreCase));
                if (rank is null)
                {
                    errors.Add($"ردیف {row}: کد یا عنوان رده ارزیابی «{rankCell}» معتبر نیست.");
                    continue;
                }
                target.PerformanceRankId = rank.Id;
            }
            target.IsSystemUser = target.Status == "فعال";
            // Optional 17th column. Legacy 16-column imports retain the existing evaluator flag.
            var evaluatorCell = Cell(17);
            if (evaluatorCell is "بله" or "خیر" or "true" or "false" or "1" or "0")
                target.IsPerformanceEvaluator = evaluatorCell is "بله" or "true" or "1";
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

    private static bool ValidateOptionalText(string value, int maxLength, string title, int row, List<string> errors)
    {
        if (value.Length <= maxLength) return true;
        errors.Add($"ردیف {row}: طول فیلد «{title}» نباید بیشتر از {maxLength} نویسه باشد.");
        return false;
    }

    private static string NormalizeHeader(string? value) =>
        (value ?? string.Empty).Trim()
            .Replace("ي", "ی", StringComparison.Ordinal)
            .Replace("ك", "ک", StringComparison.Ordinal)
            .Replace("‌", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string NormalizeDigits(string value)
    {
        const string fa = "۰۱۲۳۴۵۶۷۸۹";
        const string ar = "٠١٢٣٤٥٦٧٨٩";
        for (var i = 0; i < 10; i++)
            value = value.Replace(fa[i], (char)('0' + i)).Replace(ar[i], (char)('0' + i));
        return value;
    }

    private static void AddGuideWorksheet(XLWorkbook workbook)
    {
        var guide = workbook.Worksheets.Add("راهنمای تکمیل");
        var rows = new (string Field, string Requirement, string Guide)[]
        {
            ("شماره پرسنلی، شناسه، کد ملی، نام، نام خانوادگی، موبایل، جنسیت", "اجباری", "برای هر کارمند یکتا و معتبر باشد. جنسیت فقط مرد یا زن."),
            ("واحد سازمانی، اداره، بخش", "اختیاری", "کد گره‌های نسخه نهایی ساختار سازمانی را وارد کنید؛ کد اداره و بخش باید با والد خود سازگار باشد."),
            ("نوع استخدام", "در قالب فعلی لازم", "قراردادی یا غیرقراردادی؛ برای کارمند غیرقراردادی تاریخ پایان قرارداد خالی می‌ماند."),
            ("تاریخ پایان قرارداد، تاریخ استخدام، تاریخ تولد", "اختیاری / وابسته به نوع استخدام", "تاریخ را به شمسی و با قالب yyyy/MM/dd مانند ۱۴۰۵/۱۲/۲۹ وارد کنید. تاریخ پایان قرارداد برای استخدام قراردادی در صورت خالی بودن مقدار پیش‌فرض سامانه را می‌گیرد."),
            ("شماره شناسنامه، محل صدور، آدرس", "اختیاری", "در صورت نبود اطلاعات خالی بگذارید."),
            ("وضعیت نظام وظیفه", "اختیاری", "یکی از مقادیر: انجام داده، معاف، غیبت، در حال انجام، خرید خدمت."),
            ("وضعیت تأهل", "اختیاری", "یکی از مقادیر: مجرد، متاهل."),
            ("تعداد اولاد", "اختیاری", "عدد صحیح بین صفر تا ۳۰."),
            ("شماره بیمه، مهارت، نوع شغل", "اختیاری", "متن آزاد در حدود طول ستون مربوط."),
            ("ارزیاب عملکرد، رده ارزیابی عملکرد", "اختیاری", "ارزیاب: بله/خیر؛ رده ارزیابی را با کد یا عنوان معتبر وارد کنید.")
        };
        guide.Cell(1, 1).Value = "فیلد";
        guide.Cell(1, 2).Value = "الزام";
        guide.Cell(1, 3).Value = "راهنما";
        for (var i = 0; i < rows.Length; i++)
        {
            guide.Cell(i + 2, 1).Value = rows[i].Field;
            guide.Cell(i + 2, 2).Value = rows[i].Requirement;
            guide.Cell(i + 2, 3).Value = rows[i].Guide;
        }
        guide.Row(1).Style.Font.Bold = true;
        guide.SheetView.FreezeRows(1);
        guide.Columns().AdjustToContents();
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
