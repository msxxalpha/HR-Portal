using HRPortal.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Controllers;
public class HomeController(HRPortalDbContext db):Controller
{
 public async Task<IActionResult> Index(){ViewBag.EmployeeCount=await db.Employees.CountAsync();ViewBag.ActiveEmployeeCount=await db.Employees.CountAsync(x=>x.Status=="فعال");ViewBag.RevisionCount=await db.OrganizationStructureRevisions.CountAsync(x=>x.IsFinalized);return View();}
}