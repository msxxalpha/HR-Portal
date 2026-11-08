using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public class ReportsController(HRPortalDbContext db, RoleService roles) : Controller
{
    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(ActivityPerformance));

    [HttpGet]
    public IActionResult ExecutiveWorkflow() => RedirectToAction(nameof(ActivityPerformance));

    [HttpGet]
    public async Task<IActionResult> ActivityPerformance(
        string? direction, string? status, string? workflow, string? employee, string? organizationalRank,
        DateTime? from, DateTime? to, string? fromJalali, string? toJalali, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(fromJalali))
        {
            if (PersianDateService.TryParse(fromJalali, out var parsedFrom)) from = parsedFrom;
            else ModelState.AddModelError(nameof(fromJalali), "تاریخ شروع شمسی معتبر نیست.");
        }
        if (!string.IsNullOrWhiteSpace(toJalali))
        {
            if (PersianDateService.TryParse(toJalali, out var parsedTo)) to = parsedTo;
            else ModelState.AddModelError(nameof(toJalali), "تاریخ پایان شمسی معتبر نیست.");
        }
        var model = new ExecutiveWorkflowReportViewModel
        {
            Direction = string.IsNullOrWhiteSpace(direction) ? "all" : direction,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status,
            Workflow = string.IsNullOrWhiteSpace(workflow) ? "all" : workflow,
            Employee = string.IsNullOrWhiteSpace(employee) ? "all" : employee,
            OrganizationalRank = string.IsNullOrWhiteSpace(organizationalRank) ? "all" : organizationalRank,
            From = from?.Date,
            To = to?.Date
        };

        if (model.From.HasValue && model.To.HasValue && model.From > model.To)
        {
            ModelState.AddModelError(nameof(from), "تاریخ شروع نباید بعد از تاریخ پایان باشد.");
            (model.From, model.To) = (model.To, model.From);
        }

        // Prefer the currently effective finalized revision. If the organization
        // has only finalized its first version with a future effective date, use
        // that sole available finalized version instead of rendering an empty report.
        var revisionId = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.FinalizedAt).ThenByDescending(x => x.Id)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
        revisionId ??= await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized)
            .OrderBy(x => x.EffectiveDate).ThenByDescending(x => x.FinalizedAt).ThenByDescending(x => x.Id)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);

        if (!revisionId.HasValue)
        {
            ViewBag.NoFinalizedStructure = true;
            return View("ActivityPerformance", model);
        }

        var nodes = await db.OrganizationNodes.AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == revisionId.Value && x.IsActive)
            .Select(x => new { x.Id, x.ParentId, x.Title, x.Code, x.RankType, x.PositionHolderEmployeeId, x.AssignedEmployeeIdsCsv })
            .ToListAsync(cancellationToken);
        static string NormalizeRank(string? value)
        {
            var normalized = (value ?? string.Empty).Trim()
                .Replace("ي", "ی", StringComparison.Ordinal)
                .Replace("ك", "ک", StringComparison.Ordinal)
                .Replace("‌", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal);
            // Accept the common Persian spelling variant used in some org charts.
            return normalized.Replace("رییس", "رئیس", StringComparison.Ordinal);
        }

        static HashSet<int> AssignedIds(string? csv, int? holderId) =>
            WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds(csv, holderId).ToHashSet();

        var isAdmin = User.HasClaim("IsAdmin", "1");
        var anchorNodes = new List<(int Id, string Title)>();
        if (isAdmin)
        {
            var ceo = nodes.FirstOrDefault(x => NormalizeRank(x.RankType) == NormalizeRank("مدیرعامل"));
            if (ceo is not null)
                anchorNodes.Add((ceo.Id, ceo.Title));
            else
            {
                // The administrator is not tied to an employee position. If a
                // finalized hierarchy has no explicitly tagged CEO node, retain
                // administrator access by scoping the report to all root branches.
                anchorNodes.AddRange(nodes.Where(x => !x.ParentId.HasValue)
                    .Select(x => (x.Id, x.Title)));
                if (anchorNodes.Count == 0)
                {
                    ViewBag.NoFinalizedStructure = true;
                    return View("ActivityPerformance", model);
                }
            }
        }
        else
        {
            if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var currentEmployeeId))
                return Forbid();

            // Read effective grants from the database on every report request.
            // This avoids a valid assignment being hidden by stale/missing cookie claims.
            var effectivePermissions = await roles.GetEmployeePermissionsAsync(currentEmployeeId);
            if (!effectivePermissions.Contains("Reports.ActivityPerformance.View") &&
                !effectivePermissions.Contains("Reports.View"))
                return Forbid();

            var employeeRecord = await db.Employees.AsNoTracking()
                .Where(x => x.Id == currentEmployeeId && x.IsSystemUser && x.Status == "فعال")
                .Select(x => new { x.OrganizationPositionId })
                .FirstOrDefaultAsync(cancellationToken);
            if (employeeRecord is null) return Forbid();

            string? currentPositionCode = null;
            if (employeeRecord.OrganizationPositionId.HasValue)
                currentPositionCode = await db.OrganizationNodes.AsNoTracking()
                    .Where(x => x.Id == employeeRecord.OrganizationPositionId.Value)
                    .Select(x => x.Code).FirstOrDefaultAsync(cancellationToken);

            var eligibleRanks = new HashSet<string>(
                new[] { "مدیرعامل", "معاونت", "معاون", "مدیریت", "مدیر", "ریاست", "رئیس" }
                    .Select(NormalizeRank), StringComparer.OrdinalIgnoreCase);

            anchorNodes = nodes.Where(node =>
                    eligibleRanks.Contains(NormalizeRank(node.RankType)) &&
                    (AssignedIds(node.AssignedEmployeeIdsCsv, node.PositionHolderEmployeeId).Contains(currentEmployeeId) ||
                     (employeeRecord.OrganizationPositionId.HasValue &&
                      (node.Id == employeeRecord.OrganizationPositionId.Value ||
                       (!string.IsNullOrWhiteSpace(currentPositionCode) &&
                        string.Equals(node.Code?.Trim(), currentPositionCode.Trim(), StringComparison.OrdinalIgnoreCase))))))
                .Select(node => (node.Id, node.Title)).ToList();

            if (anchorNodes.Count == 0) return Forbid();
        }

        // Include every descendant position, not just the CEO's immediate children.
        var childrenByParent = nodes.Where(x => x.ParentId.HasValue)
            .GroupBy(x => x.ParentId!.Value)
            .ToDictionary(x => x.Key, x => x.Select(n => n.Id).ToList());
        var anchorIds = anchorNodes.Select(x => x.Id).ToHashSet();
        var descendantIds = new HashSet<int>();
        var queue = new Queue<int>(anchorIds);
        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            if (!childrenByParent.TryGetValue(parentId, out var children)) continue;
            foreach (var childId in children)
                if (descendantIds.Add(childId)) queue.Enqueue(childId);
        }
        descendantIds.ExceptWith(anchorIds);

        var directNodes = nodes.Where(x => descendantIds.Contains(x.Id)).ToList();
        var directNodeIds = directNodes.Select(x => x.Id).ToHashSet();
        model.HasCeoNode = true;
        model.CeoTitle = isAdmin ? "کل ساختار زیرمجموعه مدیرعامل" :
            string.Join("، ", anchorNodes.Select(x => x.Title).Distinct());
        model.DirectPositionCount = directNodes.Count;

        var directEmployeeIds = directNodes
            .SelectMany(x => AssignedIds(x.AssignedEmployeeIdsCsv, x.PositionHolderEmployeeId))
            .ToHashSet();
        var positionedEmployeeIds = await db.Employees.AsNoTracking()
            .Where(x => x.Status == "فعال" && x.OrganizationPositionId.HasValue && directNodeIds.Contains(x.OrganizationPositionId.Value))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        directEmployeeIds.UnionWith(positionedEmployeeIds);
        if (!isAdmin && int.TryParse(User.FindFirst("EmployeeId")?.Value, out var excludeCurrentEmployeeId))
            directEmployeeIds.Remove(excludeCurrentEmployeeId);

        var directEmployees = await db.Employees.AsNoTracking()
            .Where(x => directEmployeeIds.Contains(x.Id) && x.Status == "فعال")
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new { x.Id, x.FirstName, x.LastName, x.PersonnelNumber, x.OrganizationPositionId })
            .ToListAsync(cancellationToken);
        directEmployeeIds = directEmployees.Select(x => x.Id).ToHashSet();
        model.DirectEmployeeCount = directEmployees.Count;

        // Resolve the rank from the employee's assigned position in this finalized hierarchy.
        var employeeRanks = new Dictionary<int, string>();
        foreach (var node in directNodes.OrderBy(x => x.Id))
        {
            var rank = string.IsNullOrWhiteSpace(node.RankType) ? "سایر" : node.RankType.Trim();
            foreach (var employeeId in AssignedIds(node.AssignedEmployeeIdsCsv, node.PositionHolderEmployeeId))
                if (directEmployeeIds.Contains(employeeId) && !employeeRanks.ContainsKey(employeeId))
                    employeeRanks[employeeId] = rank;
            foreach (var employee in directEmployees.Where(x => x.OrganizationPositionId == node.Id))
                if (!employeeRanks.ContainsKey(employee.Id))
                    employeeRanks[employee.Id] = rank;
        }

        model.OrganizationalRankOptions = directNodes.Select(x => string.IsNullOrWhiteSpace(x.RankType) ? "سایر" : x.RankType.Trim())
            .Concat(employeeRanks.Values)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCulture).ToList();
        model.EmployeeOptions = directEmployees.Select(x => new ExecutiveWorkflowEmployeeOption
        {
            Id = x.Id.ToString(),
            Title = $"{x.FirstName} {x.LastName} ({x.PersonnelNumber})",
            RankType = employeeRanks.TryGetValue(x.Id, out var employeeRank) ? employeeRank : "سایر"
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
                _ when latestHistory?.Action == "Return" => "برگشت برای اصلاح",
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
                _ when latestHistory?.Action == "Return" || ageDays >= 7 => "warning",
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
        if (model.OrganizationalRank != "all")
        {
            var employeesInRank = model.EmployeeOptions
                .Where(x => string.Equals(x.RankType, model.OrganizationalRank, StringComparison.OrdinalIgnoreCase))
                .Select(x => int.TryParse(x.Id, out var id) ? id : 0).Where(id => id > 0).ToHashSet();
            allRows = allRows.Where(row => instances.Any(instance => instance.Id == row.Id &&
                (employeesInRank.Contains(instance.RequesterEmployeeId) ||
                 instance.Tasks.Any(task => task.AssignedEmployeeId.HasValue && employeesInRank.Contains(task.AssignedEmployeeId.Value)) ||
                 instance.Tasks.Any(task => task.AssignedPositionId.HasValue && directNodes.Any(node =>
                     node.Id == task.AssignedPositionId.Value && string.Equals(node.RankType, model.OrganizationalRank, StringComparison.OrdinalIgnoreCase)))))).ToList();
        }
        if (model.Employee != "all" && int.TryParse(model.Employee, out var selectedEmployeeId))
        {
            var selectedPositionId = directEmployees.FirstOrDefault(x => x.Id == selectedEmployeeId)?.OrganizationPositionId;
            allRows = allRows.Where(x => instances.Any(i => i.Id == x.Id &&
                (i.RequesterEmployeeId == selectedEmployeeId ||
                 i.Tasks.Any(t => t.AssignedEmployeeId == selectedEmployeeId ||
                    (t.AssignedPositionId.HasValue && directNodes.Any(n => n.Id == t.AssignedPositionId.Value &&
                        (n.Id == selectedPositionId ||
                         WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds(
                             n.AssignedEmployeeIdsCsv, n.PositionHolderEmployeeId).Contains(selectedEmployeeId)))))))
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
        model.ReturnedCount = model.Rows.Count(x => x.Status == "Returned" || (x.Status == "InProgress" && x.LastAction == "برگشت"));
        model.CancelledCount = model.Rows.Count(x => x.Status == "Cancelled");
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
            new() { Label = "برگشت‌خورده/اصلاح", Count = model.ReturnedCount, Color = "#d97706" },
            new() { Label = "لغوشده", Count = model.CancelledCount, Color = "#64748b" }
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
