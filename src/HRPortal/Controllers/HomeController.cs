using HRPortal.Data;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public class HomeController(HRPortalDbContext db, AnnouncementService announcements, WorkflowService workflow) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.EmployeeCount = await db.Employees.CountAsync();
        ViewBag.ActiveEmployeeCount = await db.Employees.CountAsync(x => x.Status == "فعال");
        ViewBag.RevisionCount = await db.OrganizationStructureRevisions.CountAsync(x => x.IsFinalized);
        ViewBag.Announcements = (await announcements.GetActiveAsync()).Take(8).ToList();

        var isAdmin = User.HasClaim("IsAdmin", "1");
        var canViewWorkflow = isAdmin || User.HasClaim("Permission", "Workflow.Inbox");
        ViewBag.CanViewWorkflowDashboard = canViewWorkflow;
        ViewBag.WorkflowInbox = new List<HRPortal.Models.WorkflowInboxRow>();
        ViewBag.DashboardRequests = new List<HRPortal.Models.WorkflowInboxRow>();
        ViewBag.WorkflowInboxCount = 0;
        ViewBag.DashboardRequestCount = 0;
        if (canViewWorkflow)
        {
            if (int.TryParse(User.FindFirst("EmployeeId")?.Value, out var workflowEmployeeId))
            {
                var inboxItems = await workflow.GetInboxAsync(workflowEmployeeId);
                ViewBag.WorkflowInbox = inboxItems.Take(5).ToList();
                ViewBag.WorkflowInboxCount = inboxItems.Count;
                if (isAdmin)
                {
                    var allRequests = await workflow.GetAllRequestsAsync();
                    ViewBag.DashboardRequests = allRequests.Take(5).ToList();
                    ViewBag.DashboardRequestCount = allRequests.Count;
                }
                else
                {
                    var myRequests = await workflow.GetMyRequestsAsync(workflowEmployeeId);
                    ViewBag.DashboardRequests = myRequests.Take(5).ToList();
                    ViewBag.DashboardRequestCount = myRequests.Count;
                }
            }
            else if (isAdmin)
            {
                var allRequests = await workflow.GetAllRequestsAsync();
                ViewBag.DashboardRequests = allRequests.Take(5).ToList();
                ViewBag.DashboardRequestCount = allRequests.Count;
            }
        }

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