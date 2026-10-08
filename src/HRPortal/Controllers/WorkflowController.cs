using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public class WorkflowController(
    WorkflowService workflow,
    HRPortal.Data.HRPortalDbContext db) : Controller
{
    [HttpGet]
    [Authorize(Policy = "Workflow.Inbox")]
    public async Task<IActionResult> Inbox()
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return View(new List<WorkflowInboxRow>());

        return View(await workflow.GetInboxAsync(employeeId));
    }

    [HttpGet]
    [Authorize(Policy = "Workflow.Inbox")]
    public async Task<IActionResult> MyRequests()
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return View(new List<WorkflowInboxRow>());

        return View(await workflow.GetMyRequestsAsync(employeeId));
    }

    [HttpGet]
    [Authorize(Policy = "Workflow.Inbox")]
    public async Task<IActionResult> Details(int id)
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return Forbid();

        var instance = await workflow.GetInstanceAsync(id, employeeId);
        if (instance is null) return NotFound();

        ViewBag.History = await workflow.GetHistoryAsync(id);
        return View(instance);
    }

    [HttpGet]
    [Authorize(Policy = "Workflow.Inbox")]
    public async Task<IActionResult> Task(int id)
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return Forbid();

        var task = await workflow.GetTaskAsync(id, employeeId);
        if (task is null) return NotFound();

        ViewBag.History = await workflow.GetHistoryAsync(task.WorkflowInstanceId);
        ViewBag.FieldGroups = await workflow.GetTaskFieldGroupsAsync(id, employeeId);
        return View(task);
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Inbox")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(int id, string action, string? comment, Dictionary<string, string?>? values)
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return Forbid();

        try
        {
            await workflow.CompleteTaskAsync(id, employeeId, action, comment, values);
            TempData["Success"] = "اقدام گردش کار با موفقیت ثبت شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Inbox));
    }

    [HttpGet]
    [Authorize(Policy = "Workflow.Manage")]
    public async Task<IActionResult> Manage(int? id)
    {
        var model = new WorkflowManagementViewModel
        {
            Definitions = await workflow.GetDefinitionsAsync(),
            SelectedDefinition = id.HasValue ? await workflow.GetDefinitionAsync(id.Value) : null,
            Positions = await GetCurrentPositionsAsync()
        };
        return View(model);
    }

    private async Task<List<OrganizationNode>> GetCurrentPositionsAsync()
    {
        var revision = await db.OrganizationStructureRevisions
            .AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (revision is null) return [];
        return await db.OrganizationNodes
            .AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == revision.Id && x.IsActive)
            .OrderBy(x => x.RankType)
            .ThenBy(x => x.Title)
            .ToListAsync();
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDefinition(WorkflowDefinitionEditModel model)
    {
        try { await workflow.SaveDefinitionAsync(model); TempData["Success"] = "تعریف گردش کار ذخیره شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = model.Id > 0 ? model.Id : (int?)null });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStep(WorkflowStepEditModel model)
    {
        try { await workflow.SaveStepAsync(model); TempData["Success"] = "مرحله ذخیره شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = model.WorkflowDefinitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStepField(WorkflowStepFieldEditModel model)
    {
        try
        {
            await workflow.SaveStepFieldAsync(model);
            TempData["Success"] = "فیلد مرحله ذخیره شد.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        var step = await db.WorkflowSteps.AsNoTracking().FirstOrDefaultAsync(x => x.Id == model.WorkflowStepId);
        return RedirectToAction(nameof(Manage), new { id = step?.WorkflowDefinitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStepField(int id, int definitionId)
    {
        try { await workflow.DeleteStepFieldAsync(id); TempData["Success"] = "فیلد حذف شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = definitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStep(int id, int definitionId)
    {
        try { await workflow.DeleteStepAsync(id); TempData["Success"] = "مرحله حذف شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = definitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTransition(int stepId, int definitionId, string action, int? toStepId)
    {
        try { await workflow.SaveTransitionAsync(stepId, action, toStepId); TempData["Success"] = "مسیر گردش کار ذخیره شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = definitionId });
    }
}
