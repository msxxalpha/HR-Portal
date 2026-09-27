using HRPortal.Services;
using Microsoft.AspNetCore.Mvc;

namespace HRPortal.Controllers;

public class PayrollController(ReportService reports) : Controller
{
    [HttpGet]
    public IActionResult Payslip(string? yearMonth)
    {
        var personnel = User.FindFirst("PersonnelNumber")?.Value;

        if (string.IsNullOrWhiteSpace(personnel))
        {
            ViewBag.Message = "برای مشاهده فیش حقوقی ابتدا وارد سامانه شوید.";
            return View();
        }

        ViewBag.YearMonth = yearMonth ?? PersianMonth();
        ViewBag.PersonnelNumber = personnel;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePayslip(string yearMonth)
    {
        var personnel = User.FindFirst("PersonnelNumber")?.Value;

        if (string.IsNullOrWhiteSpace(personnel))
        {
            return RedirectToAction("Login", "Account", new { returnUrl = "/Payroll/Payslip" });
        }

        ViewBag.YearMonth = yearMonth;
        ViewBag.PersonnelNumber = personnel;

        if (string.IsNullOrWhiteSpace(yearMonth))
        {
            ModelState.AddModelError("", "سال و ماه را وارد کنید.");
            return View("Payslip");
        }

        var url = await reports.BuildUrlAsync(yearMonth, personnel);
        ViewBag.ReportUrl = url;

        if (url == null)
        {
            ViewBag.Message = "تنظیمات گزارش فیش حقوقی تکمیل نشده است.";
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
