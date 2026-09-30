using ClosedXML.Excel;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Services;

public class EmployeeExcelService(HRPortalDbContext db)
{
    public async Task<byte[]> ExportAsync(IQueryable<Employee> query)
    {
        var rows = await query.AsNoTracking().Include(x => x.OrganizationUnit).Include(x => x.OrganizationDepartment).Include(x => x.OrganizationSection).ToListAsync();
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("کارکنان"); var headers = new[] { "شماره پرسنلی", "شناسه", "کد ملی", "نام", "نام خانوادگی", "موبایل", "جنسیت", "واحد سازمانی", "اداره", "بخش", "سمت", "نوع استخدام", "ایمیل", "نام پدر", "وضعیت" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        for (int r = 0; r < rows.Count; r++) { var e = rows[r]; var vals = new object?[] { e.PersonnelNumber, e.Identifier, e.NationalId, e.FirstName, e.LastName, e.Mobile, e.Gender, e.OrganizationUnit?.Code, e.OrganizationDepartment?.Code, e.OrganizationSection?.Code, e.PositionTitle, e.EmploymentType, e.Email, e.FatherName, e.Status }; for (int c = 0; c < vals.Length; c++) ws.Cell(r + 2, c + 1).Value = vals[c]?.ToString() ?? ""; }
        ws.Row(1).Style.Font.Bold = true; ws.Columns().AdjustToContents(); using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }
    public async Task<List<string>> ImportAsync(Stream stream)
    {
        var errors = new List<string>(); using var wb = new XLWorkbook(stream); var ws = wb.Worksheet(1); var last = ws.LastRowUsed()?.RowNumber() ?? 1; var current = await db.OrganizationStructureRevisions.Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today).OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(); var orgNodes = current == null ? new List<OrganizationNode>() : await db.OrganizationNodes.Where(x => x.OrganizationStructureRevisionId == current.Id).ToListAsync(); var unitMap = orgNodes.Where(x => x.RankType == "مدیریت").ToDictionary(x => x.Code); var deptMap = orgNodes.Where(x => x.RankType == "ریاست").ToDictionary(x => x.Code); var secMap = orgNodes.Where(x => x.RankType == "سرپرستی").ToDictionary(x => x.Code);
        var imported = new List<Employee>();
        for (int r = 2; r <= last; r++)
        {
            string v(int c) => ws.Cell(r, c).GetString().Trim(); var pn = v(1); if (string.IsNullOrWhiteSpace(pn)) { errors.Add($"ردیف {r}: شماره پرسنلی الزامی است."); continue; }
            if (await db.Employees.AnyAsync(x => x.PersonnelNumber == pn)) { errors.Add($"ردیف {r}: شماره پرسنلی {pn} تکراری است."); continue; }
            var identifier = v(2); if (string.IsNullOrWhiteSpace(identifier)) { errors.Add($"ردیف {r}: شناسه الزامی است."); continue; }
            if (await db.Employees.AnyAsync(x => x.Identifier == identifier)) { errors.Add($"ردیف {r}: شناسه {identifier} تکراری است."); continue; }
            var national = v(3); if (string.IsNullOrWhiteSpace(national) || string.IsNullOrWhiteSpace(v(4)) || string.IsNullOrWhiteSpace(v(5)) || string.IsNullOrWhiteSpace(v(6)) || string.IsNullOrWhiteSpace(v(7))) { errors.Add($"ردیف {r}: یکی از فیلدهای اجباری خالی است."); continue; }
            if (await db.Employees.AnyAsync(x => x.NationalId == national)) { errors.Add($"ردیف {r}: کد ملی {national} تکراری است."); continue; }
            var e = new Employee { PersonnelNumber = pn, Identifier = identifier, NationalId = national, FirstName = v(4), LastName = v(5), Mobile = v(6), Gender = v(7), PositionTitle = v(11), EmploymentType = v(12), Email = v(13), FatherName = v(14), Status = string.IsNullOrWhiteSpace(v(15)) ? "فعال" : v(15), IsSystemUser = true };
            if (unitMap.TryGetValue(v(8), out var u)) e.OrganizationUnitId = u.Id; if (deptMap.TryGetValue(v(9), out var d)) e.OrganizationDepartmentId = d.Id; if (secMap.TryGetValue(v(10), out var s)) e.OrganizationSectionId = s.Id; db.Employees.Add(e); imported.Add(e);
        }
        await db.SaveChangesAsync();
        var defaultRole = await db.Roles.FirstOrDefaultAsync(x => x.Code == "EMPLOYEE" && x.IsActive);
        if (defaultRole is not null && imported.Count > 0)
        {
            foreach (var e in imported) db.EmployeeRoles.Add(new EmployeeRole { EmployeeId = e.Id, RoleId = defaultRole.Id });
            await db.SaveChangesAsync();
        }
        return errors;
    }
}