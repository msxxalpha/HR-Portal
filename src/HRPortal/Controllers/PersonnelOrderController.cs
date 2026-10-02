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

        var result = await reports.FetchForEmployeeAsync(employee);
        if (!result.Success || result.Content is null)
        {
            ViewBag.Message = result.ErrorMessage;
            return View();
        }

        Response.Headers.ContentDisposition = "inline";
        return File(result.Content, result.ContentType);
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
