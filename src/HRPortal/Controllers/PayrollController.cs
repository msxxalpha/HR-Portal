using HRPortal.Data;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "Payroll.View")]
public class PayrollController(HRPortalDbContext db, ReportService reports) : Controller
{
    [HttpGet]
    public IActionResult Payslip(string? yearMonth = null)
    {
        if (User.HasClaim("IsAdmin", "1"))
            return Forbid();

        var personnelNumber = GetAuthenticatedPersonnelNumber();
        ViewBag.PersonnelNumber = personnelNumber;
        ViewBag.YearMonth = string.IsNullOrWhiteSpace(yearMonth) ? PersianMonth() : NormalizeDigits(yearMonth);

        if (personnelNumber is null)
            ViewBag.Message = "شماره پرسنلی کاربر احراز‌شده یافت نشد.";

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePayslip(string yearMonth)
    {
        if (User.HasClaim("IsAdmin", "1"))
            return Forbid();

        var personnelNumber = GetAuthenticatedPersonnelNumber();
        ViewBag.PersonnelNumber = personnelNumber;
        ViewBag.YearMonth = NormalizeDigits(yearMonth);

        if (personnelNumber is null)
        {
            ViewBag.Message = "شماره پرسنلی کاربر احراز‌شده یافت نشد.";
            return View("Payslip");
        }

        var settings = await db.PayrollReportSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is null || !settings.Enabled)
        {
            ViewBag.Message = "نمایش فیش حقوقی در تنظیمات سامانه غیرفعال است.";
            return View("Payslip");
        }

        if (string.IsNullOrWhiteSpace(yearMonth) ||
            !System.Text.RegularExpressions.Regex.IsMatch(NormalizeDigits(yearMonth).Trim(), @"^[0-9]{4}/(0[1-9]|1[0-2])$"))
        {
            ModelState.AddModelError("", "سال و ماه را به صورت ۱۴۰۵/۰۶ وارد کنید.");
            return View("Payslip");
        }

        ViewBag.ReportUrl = await reports.BuildUrlAsync(NormalizeDigits(yearMonth).Trim(), personnelNumber);
        if (ViewBag.ReportUrl is null)
            ViewBag.Message = "آدرس گزارش فیش حقوقی در تنظیمات سامانه ثبت نشده است.";

        return View("Payslip");
    }

    private string? GetAuthenticatedPersonnelNumber()
    {
        var employeeIdValue = User.FindFirst("EmployeeId")?.Value;
        if (!int.TryParse(employeeIdValue, out var employeeId))
            return null;

        return db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId && x.IsSystemUser && x.Status == "فعال")
            .Select(x => x.PersonnelNumber)
            .FirstOrDefault();
    }

    private static string PersianMonth()
    {
        var pc = new System.Globalization.PersianCalendar();
        var now = DateTime.Now;
        return $@"{pc.GetYear(now):0000}/{pc.GetMonth(now):00}";
    }

    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        const string fa = "۰۱۲۳۴۵۶۷۸۹";
        const string ar = "٠١٢٣٤٥٦٧٨٩";
        foreach (var ch in fa.Select((c, i) => (c, i)))
            value = value.Replace(ch.c, (char)('0' + ch.i));
        foreach (var ch in ar.Select((c, i) => (c, i)))
            value = value.Replace(ch.c, (char)('0' + ch.i));
        return value;
    }
}
