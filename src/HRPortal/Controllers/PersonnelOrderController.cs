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

        var resolved = await reports.ResolveOrderIdAsync(employee);
        if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.OrderId))
        {
            ViewBag.Message = resolved.ErrorMessage;
            return View();
        }

        HttpContext.Session.SetString("PersonnelOrderId", resolved.OrderId);
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

        var orderId = HttpContext.Session.GetString("PersonnelOrderId");
        if (string.IsNullOrWhiteSpace(orderId))
            return StyledError("شناسه حکم کارگزینی در نشست کاربر یافت نشد.", StatusCodes.Status422UnprocessableEntity);

        var result = await reports.FetchForOrderIdAsync(orderId);
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
