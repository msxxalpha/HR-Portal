using HRPortal.Data;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "PersonnelOrder.View")]
public class PersonnelOrderController(
    HRPortalDbContext db,
    PersonnelOrderReportService reports) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (User.HasClaim("IsAdmin", "1"))
        {
            ViewBag.Message = "این کاربر حکم کارگزینی ندارد.";
            ViewBag.MessageType = "info";
            return View();
        }

        var employee = await GetAuthenticatedEmployeeAsync();
        if (employee is null)
        {
            ViewBag.Message = "کارمند فعال و احراز‌شده یافت نشد.";
            return View();
        }

        ViewBag.PersonnelNumber = employee.PersonnelNumber;
        ViewBag.EmployeeName = $"{employee.FirstName} {employee.LastName}";

        var reportSettings = await db.PersonnelOrderReportSettings
            .AsNoTracking()
            .FirstOrDefaultAsync();
        ViewBag.ReportAllowPrint = reportSettings?.AllowPrint ?? true;
        ViewBag.ReportAllowDownload = reportSettings?.AllowDownload ?? true;

        var resolved = await reports.ResolveOrderIdAsync(employee.PersonnelNumber);
        if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.OrderId))
        {
            ViewBag.Message = resolved.ErrorMessage;
            return View();
        }

        ViewBag.ReportEndpoint = Url.Action(nameof(Report), "PersonnelOrder");
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Report()
    {
        if (User.HasClaim("IsAdmin", "1"))
            return StyledError("این کاربر حکم کارگزینی ندارد.", StatusCodes.Status403Forbidden);

        var employee = await GetAuthenticatedEmployeeAsync();
        if (employee is null)
            return StyledError("کارمند فعال و احراز‌شده یافت نشد.", StatusCodes.Status403Forbidden);

        // Resolve the order for the currently authenticated employee every time
        // the report is requested. This prevents stale/session-cross-user values.
        var resolved = await reports.ResolveOrderIdAsync(employee.PersonnelNumber);
        if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.OrderId))
            return StyledError(resolved.ErrorMessage, StatusCodes.Status422UnprocessableEntity);

        var result = await reports.FetchForOrderIdAsync(resolved.OrderId);
        if (!result.Success || result.Content is null)
            return StyledError(result.ErrorMessage, StatusCodes.Status502BadGateway);

        Response.Headers.ContentDisposition = "inline";
        return File(result.Content, result.ContentType);
    }

    private ContentResult StyledError(string message, int statusCode)
    {
        Response.StatusCode = statusCode;
        var safe = System.Net.WebUtility.HtmlEncode(message);
        return Content("<!doctype html><html lang='fa' dir='rtl'><meta charset='utf-8'><style>body{font-family:Vazir,Arial;background:#f5f7fa}.box{max-width:760px;margin:70px auto;padding:28px;background:#fff;border:1px solid #e1e6eb;border-radius:14px}h3{margin-top:0}.msg{padding:16px;background:#fff4f4;border:1px solid #f0cccc;border-radius:10px;line-height:2}</style><div class='box'><h3>گزارش حکم کارگزینی قابل دریافت نیست</h3><div class='msg'>"+safe+"</div></div>", "text/html; charset=utf-8");
    }

    private async Task<HRPortal.Models.Employee?> GetAuthenticatedEmployeeAsync()
    {
        // The employee username is the personnel number. Resolve the employee
        // from that claim so both OTP and personal-password login paths use the
        // exact same source of truth for the «شناسه» value.
        var personnelNumber = User.FindFirst("PersonnelNumber")?.Value?.Trim();

        if (!string.IsNullOrWhiteSpace(personnelNumber))
        {
            var employeeByUsername = await db.Employees.AsNoTracking()
                .Where(x => x.PersonnelNumber == personnelNumber &&
                            x.IsSystemUser &&
                            x.Status == "فعال")
                .Select(x => new HRPortal.Models.Employee
                {
                    Id = x.Id,
                    PersonnelNumber = x.PersonnelNumber,
                    Identifier = x.Identifier,
                    FirstName = x.FirstName,
                    LastName = x.LastName
                })
                .FirstOrDefaultAsync();

            if (employeeByUsername is not null)
                return employeeByUsername;
        }

        // Backward-compatible fallback for older authentication cookies.
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return null;

        return await db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId && x.IsSystemUser && x.Status == "فعال")
            .Select(x => new HRPortal.Models.Employee
            {
                Id = x.Id,
                PersonnelNumber = x.PersonnelNumber,
                Identifier = x.Identifier,
                FirstName = x.FirstName,
                LastName = x.LastName
            })
            .FirstOrDefaultAsync();
    }
}
