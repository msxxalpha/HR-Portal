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
        {
            ViewBag.Message = "این کاربر فیش حقوقی ندارد.";
            ViewBag.MessageType = "info";
            return View();
        }

        var employee = GetAuthenticatedEmployee();
        ViewBag.PersonnelNumber = employee?.PersonnelNumber;

        var requestedYearMonth = string.IsNullOrWhiteSpace(yearMonth)
            ? PersianMonth()
            : NormalizeDigits(yearMonth).Trim();

        ViewBag.YearMonth = requestedYearMonth;

        if (employee is null)
        {
            ViewBag.Message = "کارمند فعال و احراز‌شده یافت نشد.";
        }
        else if (string.IsNullOrWhiteSpace(employee.Identifier))
        {
            ViewBag.Message = "شناسه گزارش این کارمند در اطلاعات کارکنان ثبت نشده است.";
        }
        else if (!string.IsNullOrWhiteSpace(yearMonth) &&
                 ReportService.TryNormalizeYearMonth(yearMonth, out var normalizedYearMonth))
        {
            ViewBag.ReportEndpoint = Url.Action(nameof(Report), "Payroll", new { yearMonth = normalizedYearMonth });
        }

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePayslip(string yearMonth)
    {
        if (User.HasClaim("IsAdmin", "1"))
        {
            ViewBag.Message = "این کاربر فیش حقوقی ندارد.";
            ViewBag.MessageType = "info";
            return View("Payslip");
        }

        var employee = GetAuthenticatedEmployee();
        ViewBag.PersonnelNumber = employee?.PersonnelNumber;
        ViewBag.YearMonth = NormalizeDigits(yearMonth).Trim();

        if (employee is null)
        {
            ViewBag.Message = "کارمند فعال و احراز‌شده یافت نشد.";
            return View("Payslip");
        }

        if (string.IsNullOrWhiteSpace(employee.Identifier))
        {
            ViewBag.Message = "شناسه گزارش این کارمند در اطلاعات کارکنان ثبت نشده است.";
            return View("Payslip");
        }

        if (!ReportService.TryNormalizeYearMonth(yearMonth, out var normalizedYearMonth))
        {
            ModelState.AddModelError("", "سال و ماه را به صورت ۱۴۰۵۰۶ وارد کنید؛ دقیقاً ۶ رقم و بدون اسلش.");
            return View("Payslip");
        }

        var settings = await db.PayrollReportSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is null || !settings.Enabled)
        {
            ViewBag.Message = "نمایش فیش حقوقی در تنظیمات سامانه غیرفعال است.";
            return View("Payslip");
        }

        ViewBag.YearMonth = normalizedYearMonth;
        ViewBag.ReportEndpoint = Url.Action(nameof(Report), "Payroll", new { yearMonth = normalizedYearMonth });
        return View("Payslip");
    }

    [HttpGet]
    public async Task<IActionResult> Report(string yearMonth)
    {
        if (User.HasClaim("IsAdmin", "1"))
            return StyledReportError("این کاربر فیش حقوقی ندارد.", StatusCodes.Status403Forbidden);

        var employee = GetAuthenticatedEmployee();
        if (employee is null)
            return StyledReportError("کارمند فعال و احراز‌شده یافت نشد.", StatusCodes.Status403Forbidden);

        if (string.IsNullOrWhiteSpace(employee.Identifier))
            return StyledReportError("شناسه گزارش این کارمند در اطلاعات کارکنان ثبت نشده است.", StatusCodes.Status422UnprocessableEntity);

        if (!ReportService.TryNormalizeYearMonth(yearMonth, out var normalizedYearMonth))
            return StyledReportError("پارامتر سال و ماه باید دقیقاً با فرمت ۱۴۰۵۰۶ (شش رقم، بدون اسلش) باشد.", StatusCodes.Status400BadRequest);

        // The report receives the employee's internal report identifier,
        // not the login personnel number.
        var result = await reports.FetchAsync(normalizedYearMonth, employee.Identifier);
        if (!result.Success || result.Content is null)
            return StyledReportError(result.ErrorMessage, StatusCodes.Status502BadGateway);

        Response.Headers.ContentDisposition = "inline";
        return File(result.Content, result.ContentType);
    }

    private ContentResult StyledReportError(string message, int statusCode)
    {
        Response.StatusCode = statusCode;
        var safe = System.Net.WebUtility.HtmlEncode(message);
        var html =
            "<!doctype html><html lang=\"fa\" dir=\"rtl\">" +
            "<head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            "<title>خطا در گزارش فیش حقوقی</title>" +
            "<style>" +
            "body{margin:0;background:#f5f7fa;font-family:Vazir,Arial,sans-serif;color:#263746}" +
            ".box{max-width:760px;margin:70px auto;padding:28px;background:#fff;border:1px solid #e1e6eb;border-radius:14px;box-shadow:0 8px 30px rgba(20,40,60,.08)}" +
            "h3{margin-top:0}.msg{padding:16px;background:#fff4f4;border:1px solid #f0cccc;border-radius:10px;line-height:2}" +
            "</style></head><body><div class=\"box\">" +
            "<h3>گزارش فیش حقوقی قابل دریافت نیست</h3><div class=\"msg\">" +
            safe +
            "</div></div></body></html>";

        return Content(html, "text/html; charset=utf-8");
    }

    private HRPortal.Models.Employee? GetAuthenticatedEmployee()
    {
        var employeeIdValue = User.FindFirst("EmployeeId")?.Value;
        if (!int.TryParse(employeeIdValue, out var employeeId))
            return null;

        return db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId && x.IsSystemUser && x.Status == "فعال")
            .Select(x => new HRPortal.Models.Employee
            {
                Id = x.Id,
                PersonnelNumber = x.PersonnelNumber,
                Identifier = x.Identifier,
                FirstName = x.FirstName,
                LastName = x.LastName
            })
            .FirstOrDefault();
    }

    private static string PersianMonth()
    {
        var pc = new System.Globalization.PersianCalendar();
        var now = DateTime.Now;
        return $"{pc.GetYear(now):0000}{pc.GetMonth(now):00}";
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
