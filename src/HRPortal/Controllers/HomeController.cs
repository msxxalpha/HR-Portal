using HRPortal.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public class HomeController(HRPortalDbContext db, AnnouncementService announcements) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.EmployeeCount = await db.Employees.CountAsync();
        ViewBag.ActiveEmployeeCount = await db.Employees.CountAsync(x => x.Status == "فعال");
        ViewBag.RevisionCount = await db.OrganizationStructureRevisions.CountAsync(x => x.IsFinalized);
        ViewBag.Announcements = (await announcements.GetActiveAsync()).Take(8).ToList();

        ViewBag.HasPersonalPassword = true;
        if (!User.HasClaim("IsAdmin", "1") &&
            int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
        {
            ViewBag.HasPersonalPassword = await db.Employees
                .AsNoTracking()
                .Where(x => x.Id == employeeId)
                .Select(x => x.PersonalPasswordHash != null && x.PersonalPasswordHash != "")
                .FirstOrDefaultAsync();
        }

        return View();
    }

    public IActionResult Error() => View();
}