using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRPortal.Controllers;

[Authorize]
public class PayrollController(ReportService reports) : Controller
{
    [HttpGet]
    public IActionResult Payslip(string? yearMonth = null)
    {
        var personnelNumber = User.FindFirst("PersonnelNumber")?.Value;

        ViewBag.PersonnelNumber = personnelNumber;
        ViewBag.YearMonth = string.IsNullOrWhiteSpace(yearMonth) ? PersianMonth() : yearMonth;
        ViewBag.ReportUrl = null;

        if (string.IsNullOrWhiteSpace(personnelNumber))
        {
            ViewBag.Message = "این حساب کاربری شماره پرسنلی ندارد؛ فیش حقوقی برای حساب کارکنان فعال است.";
        }

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePayslip(string yearMonth)
    {
        var personnelNumber = User.FindFirst("PersonnelNumber")?.Value;

        ViewBag.YearMonth = yearMonth;
        ViewBag.PersonnelNumber = personnelNumber;

        if (string.IsNullOrWhiteSpace(personnelNumber))
        {
            ViewBag.Message = "این حساب کاربری شماره پرسنلی ندارد؛ فیش حقوقی برای حساب کارکنان فعال است.";
            return View("Payslip");
        }

        if (string.IsNullOrWhiteSpace(yearMonth))
        {
            ModelState.AddModelError("", "سال و ماه را به صورت ۱۴۰۵/۰۶ وارد کنید.");
            return View("Payslip");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(yearMonth.Trim(), @"^[0-9۰-۹]{4}/[0-9۰-۹]{2}$"))
        {
            ModelState.AddModelError("", "قالب سال و ماه باید مانند ۱۴۰۵/۰۶ باشد.");
            return View("Payslip");
        }

        ViewBag.ReportUrl = await reports.BuildUrlAsync(yearMonth.Trim(), personnelNumber);

        if (ViewBag.ReportUrl is null)
        {
            ViewBag.Message = "آدرس گزارش فیش حقوقی در تنظیمات سامانه ثبت نشده یا گزارش غیرفعال است.";
        }

        return View("Payslip");
    }

    private static string PersianMonth()
    {
        var pc = new System.Globalization.PersianCalendar();
        var now = DateTime.Now;
        return $"{pc.GetYear(now):0000}/{pc.GetMonth(now):00}";
    }
}