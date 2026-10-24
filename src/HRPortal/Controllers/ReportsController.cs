using HRPortal.Data;
using HRPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "Reports.View")]
public class ReportsController(HRPortalDbContext db) : Controller
{
    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(ExecutiveWorkflow));

    [HttpGet]
    public async Task<IActionResult> ExecutiveWorkflow(
        string? direction, string? status, string? workflow, string? employee,
        DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        var model = new ExecutiveWorkflowReportViewModel
        {
            Direction = string.IsNullOrWhiteSpace(direction) ? "all" : direction,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status,
            Workflow = string.IsNullOrWhiteSpace(workflow) ? "all" : workflow,
            Employee = string.IsNullOrWhiteSpace(employee) ? "all" : employee,
            From = from?.Date,
            To = to?.Date
        };

        if (model.From.HasValue && model.To.HasValue && model.From > model.To)
        {
            ModelState.AddModelError(nameof(from), "تاریخ شروع نباید بعد از تاریخ پایان باشد.");
            (model.From, model.To) = (model.To, model.From);
        }

        var revisionId = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.FinalizedAt).ThenByDescending(x => x.Id)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);

        if (!revisionId.HasValue)
        {
            ViewBag.NoFinalizedStructure = true;
            return View(model);
        }

        var nodes = await db.OrganizationNodes.AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == revisionId.Value && x.IsActive)
            .Select(x => new { x.Id, x.ParentId, x.Title, x.Code, x.RankType, x.PositionHolderEmployeeId, x.AssignedEmployeeIdsCsv })
            .ToListAsync(cancellationToken);
        var ceo = nodes.FirstOrDefault(x => x.RankType == "مدیرعامل");
        if (ceo is null)
        {
            ViewBag.NoFinalizedStructure = true;
            return View(model);
        }

        model.HasCeoNode = true;
        model.CeoTitle = ceo.Title;
        var directNodes = nodes.Where(x => x.ParentId == ceo.Id).ToList();
        var directNodeIds = directNodes.Select(x => x.Id).ToHashSet();
        model.DirectPositionCount = directNodes.Count;

        var directEmployeeIds = directNodes
            .SelectMany(x => new[] { x.PositionHolderEmployeeId }.Where(id => id.HasValue).Select(id => id!.Value)
                .Concat((x.AssignedEmployeeIdsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(raw => int.TryParse(raw, out var id) ? id : 0).Where(id => id > 0)))
            .ToHashSet();
        var positionedEmployeeIds = await db.Employees.AsNoTracking()
            .Where(x => x.Status == "فعال" && x.OrganizationPositionId.HasValue && directNodeIds.Contains(x.OrganizationPositionId.Value))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        directEmployeeIds.UnionWith(positionedEmployeeIds);

        var directEmployees = await db.Employees.AsNoTracking()
            .Where(x => directEmployeeIds.Contains(x.Id) && x.Status == "فعال")
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new { x.Id, x.FirstName, x.LastName, x.PersonnelNumber })
            .ToListAsync(cancellationToken);
        directEmployeeIds = directEmployees.Select(x => x.Id).ToHashSet();
        model.DirectEmployeeCount = directEmployees.Count;
        model.EmployeeOptions = directEmployees.Select(x => new ExecutiveWorkflowEmployeeOption
        {
            Id = x.Id.ToString(),
            Title = $"{x.FirstName} {x.LastName} ({x.PersonnelNumber})"
        }).ToList();

        var instances = await db.WorkflowInstances.AsNoTracking()
            .Where(x => directEmployeeIds.Contains(x.RequesterEmployeeId) ||
                x.Tasks.Any(t =>
                    (t.AssignedPositionId.HasValue && directNodeIds.Contains(t.AssignedPositionId.Value)) ||
                    (t.AssignedEmployeeId.HasValue && directEmployeeIds.Contains(t.AssignedEmployeeId.Value))))
            .Include(x => x.RequesterEmployee)
            .Include(x => x.WorkflowDefinition)
            .Include(x => x.CurrentStep).ThenInclude(x => x!.OrganizationNode)
            .Include(x => x.Tasks).ThenInclude(x => x.WorkflowStep).ThenInclude(x => x.OrganizationNode)
            .Include(x => x.Tasks).ThenInclude(x => x.AssignedEmployee)
            .Include(x => x.Tasks).ThenInclude(x => x.AssignedPosition)
            .Include(x => x.History)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken);

        var allRows = instances.Select(instance =>
        {
            var relevantTasks = instance.Tasks.Where(t =>
                (t.AssignedPositionId.HasValue && directNodeIds.Contains(t.AssignedPositionId.Value)) ||
                (t.AssignedEmployeeId.HasValue && directEmployeeIds.Contains(t.AssignedEmployeeId.Value))).ToList();
            var isIncoming = relevantTasks.Count > 0;
            var isOutgoing = directEmployeeIds.Contains(instance.RequesterEmployeeId);
            var currentTasks = instance.Tasks.Where(t => t.Status == "Pending").OrderBy(t => t.CreatedAt).ToList();
            var currentOwner = currentTasks.Select(t =>
                t.AssignedEmployee is not null
                    ? $"{t.AssignedEmployee.FirstName} {t.AssignedEmployee.LastName}"
                    : t.AssignedPosition?.Title ?? t.WorkflowStep.OrganizationNode?.Title ?? t.WorkflowStep.Title)
                .FirstOrDefault() ?? instance.CurrentStep?.OrganizationNode?.Title ?? instance.CurrentStep?.Title ?? "—";
            var latestHistory = instance.History.OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id).FirstOrDefault();
            var ageDays = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - instance.StartedAt).TotalDays));
            var statusTitle = instance.Status switch
            {
                "Approved" => "تأیید نهایی",
                "Completed" => "تکمیل‌شده",
                "Rejected" => "ردشده",
                "Returned" => "برگشت‌خورده",
                "Cancelled" => "لغوشده",
                _ => "در حال گردش"
            };
            var qualitySignal = instance.Status switch
            {
                "Rejected" => "ردشده؛ بررسی علت",
                "Returned" => "برگشت برای اصلاح",
                "Approved" or "Completed" => ageDays <= 3 ? "تکمیل سریع" : "تکمیل‌شده",
                _ when currentTasks.Count == 0 => "نیازمند بررسی مالک مرحله",
                _ when ageDays >= 14 => "ماندگاری بحرانی",
                _ when ageDays >= 7 => "ماندگاری بالا",
                _ => "در جریان عادی"
            };
            var qualityClass = instance.Status switch
            {
                "Rejected" => "danger",
                "Returned" => "warning",
                "Approved" or "Completed" => ageDays <= 3 ? "success" : "neutral",
                _ when currentTasks.Count == 0 || ageDays >= 14 => "danger",
                _ when ageDays >= 7 => "warning",
                _ => "info"
            };
            return new ExecutiveWorkflowReportRow
            {
                Id = instance.Id,
                RequestCode = $"{instance.EntityType} · {instance.EntityId}",
                WorkflowTitle = instance.WorkflowDefinition?.Title ?? "گردش کار",
                RequesterName = $"{instance.RequesterEmployee?.FirstName} {instance.RequesterEmployee?.LastName}".Trim(),
                RequesterPersonnelNumber = instance.RequesterEmployee?.PersonnelNumber ?? "—",
                Direction = isIncoming && isOutgoing ? "ورودی و خروجی" : isIncoming ? "ورودی" : "خروجی",
                Status = instance.Status,
                StatusTitle = statusTitle,
                CurrentStep = instance.CurrentStep?.Title ?? "—",
                CurrentOwner = currentOwner,
                StartedAt = instance.StartedAt,
                CompletedAt = instance.CompletedAt,
                AgeDays = ageDays,
                QualitySignal = qualitySignal,
                QualityClass = qualityClass,
                LastAction = latestHistory?.Action switch
                {
                    "Submit" => "ثبت درخواست",
                    "Approve" => "تأیید",
                    "Reject" => "رد",
                    "Return" => "برگشت",
                    "SaveDraft" => "ذخیره پیش‌نویس",
                    _ => latestHistory?.Action ?? "—"
                },
                LastComment = latestHistory?.Comment
            };
        }).ToList();

        if (model.Direction == "incoming") allRows = allRows.Where(x => x.Direction.Contains("ورودی", StringComparison.Ordinal)).ToList();
        else if (model.Direction == "outgoing") allRows = allRows.Where(x => x.Direction.Contains("خروجی", StringComparison.Ordinal)).ToList();

        if (model.Status != "all")
            allRows = allRows.Where(x => model.Status switch
            {
                "inprogress" => x.Status == "InProgress",
                "approved" => x.Status == "Approved" || x.Status == "Completed",
                "rejected" => x.Status == "Rejected",
                "returned" => x.Status == "Returned",
                "cancelled" => x.Status == "Cancelled",
                _ => true
            }).ToList();
        if (model.Workflow != "all")
            allRows = allRows.Where(x => x.WorkflowTitle == model.Workflow).ToList();
        if (model.Employee != "all" && int.TryParse(model.Employee, out var selectedEmployeeId))
        {
            allRows = allRows.Where(x => instances.Any(i => i.Id == x.Id &&
                (i.RequesterEmployeeId == selectedEmployeeId ||
                 i.Tasks.Any(t => t.AssignedEmployeeId == selectedEmployeeId ||
                    (t.AssignedPositionId.HasValue && directNodes.Any(n => n.Id == t.AssignedPositionId.Value &&
                        (n.PositionHolderEmployeeId == selectedEmployeeId ||
                         (n.AssignedEmployeeIdsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(selectedEmployeeId.ToString())))))))
            ).ToList();
        }
        if (model.From.HasValue) allRows = allRows.Where(x => x.StartedAt.Date >= model.From.Value.Date).ToList();
        if (model.To.HasValue) allRows = allRows.Where(x => x.StartedAt.Date <= model.To.Value.Date).ToList();

        model.Rows = allRows.OrderByDescending(x => x.StartedAt).ToList();
        model.TotalCount = model.Rows.Count;
        model.IncomingCount = model.Rows.Count(x => x.Direction.Contains("ورودی", StringComparison.Ordinal));
        model.OutgoingCount = model.Rows.Count(x => x.Direction.Contains("خروجی", StringComparison.Ordinal));
        model.InProgressCount = model.Rows.Count(x => x.Status == "InProgress");
        model.CompletedCount = model.Rows.Count(x => x.Status == "Approved" || x.Status == "Completed");
        model.RejectedCount = model.Rows.Count(x => x.Status == "Rejected");
        model.ReturnedCount = model.Rows.Count(x => x.Status == "Returned");
        model.PendingTaskCount = instances.Where(i => model.Rows.Any(r => r.Id == i.Id)).SelectMany(i => i.Tasks).Count(t => t.Status == "Pending");
        model.AgingCount = model.Rows.Count(x => x.Status == "InProgress" && x.AgeDays >= 7);
        var completedRows = model.Rows.Where(x => x.CompletedAt.HasValue && x.CompletedAt.Value >= x.StartedAt).ToList();
        model.AverageCompletionDays = completedRows.Count == 0 ? 0 :
            Math.Round(completedRows.Average(x => (x.CompletedAt!.Value - x.StartedAt).TotalDays), 1);

        model.WorkflowOptions = model.Rows.Select(x => x.WorkflowTitle).Distinct().OrderBy(x => x).ToList();
        model.StatusChart =
        [
            new() { Label = "در حال گردش", Count = model.InProgressCount, Color = "#2563eb" },
            new() { Label = "تکمیل/تأیید", Count = model.CompletedCount, Color = "#16a34a" },
            new() { Label = "ردشده", Count = model.RejectedCount, Color = "#dc2626" },
            new() { Label = "برگشت‌خورده", Count = model.ReturnedCount, Color = "#d97706" }
        ];
        model.DirectionChart =
        [
            new() { Label = "ورودی", Count = model.IncomingCount, Color = "#0f766e" },
            new() { Label = "خروجی", Count = model.OutgoingCount, Color = "#7c3aed" }
        ];
        model.WorkflowChart = model.Rows.GroupBy(x => x.WorkflowTitle)
            .Select(g => new ExecutiveWorkflowChartItem { Label = g.Key, Count = g.Count(), Color = "#475569" })
            .OrderByDescending(x => x.Count).Take(8).ToList();

        ViewBag.NoFinalizedStructure = false;
        return View(model);
    }
}
