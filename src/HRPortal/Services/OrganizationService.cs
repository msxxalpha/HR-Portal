using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class OrganizationService(HRPortalDbContext db, AuditService audit)
{
    public async Task<OrganizationStructureRevision?> CurrentAsync() =>
        await db.OrganizationStructureRevisions
            .Include(x => x.Nodes)
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.FinalizedAt)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task<List<OrganizationStructureRevision>> RevisionsAsync() =>
        await db.OrganizationStructureRevisions
            .Include(x => x.Changes)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync();

    public async Task<OrganizationStructureRevision> GetOrCreateDraftAsync()
    {
        var existing = await db.OrganizationStructureRevisions
            .Where(x => !x.IsFinalized)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
        if (existing is not null)
            return existing;

        var current = await CurrentAsync();
        return await CreateRevisionAsync(
            DateTime.Today,
            current is null ? "نسخه در حال ایجاد ساختار سازمانی" : "بازنگری ساختار سازمانی",
            "نسخه کاری مدیر سامانه برای ایجاد یا اصلاح ساختار سازمانی");
    }

    public async Task<OrganizationStructureRevision> CreateRevisionAsync(
        DateTime effectiveDate, string title, string? notes)
    {
        var current = await CurrentAsync();

        var revision = new OrganizationStructureRevision
        {
            RevisionCode = $"ORG-{DateTime.Now:yyyyMMddHHmmssfff}",
            EffectiveDate = effectiveDate.Date,
            Title = title.Trim(),
            Notes = notes?.Trim()
        };

        db.OrganizationStructureRevisions.Add(revision);
        await db.SaveChangesAsync();

        if (current != null)
        {
            var nodes = await db.OrganizationNodes
                .Where(x => x.OrganizationStructureRevisionId == current.Id)
                .AsNoTracking()
                .ToListAsync();

            foreach (var root in nodes.Where(x => x.ParentId == null)
                                       .OrderBy(x => x.SortOrder)
                                       .ThenBy(x => x.Title))
            {
                await CloneTreeAsync(nodes, root, revision.Id, null);
            }
        }

        await audit.WriteAsync(
            "ایجاد بازنگری ساختار",
            nameof(OrganizationStructureRevision),
            revision.Id.ToString(),
            revision.Title);

        return revision;
    }

    private async Task CloneTreeAsync(
        List<OrganizationNode> source,
        OrganizationNode oldNode,
        int revisionId,
        int? newParentId)
    {
        var clone = new OrganizationNode
        {
            OrganizationStructureRevisionId = revisionId,
            ParentId = newParentId,
            Code = oldNode.Code,
            Title = oldNode.Title,
            RankType = oldNode.RankType,
            SortOrder = oldNode.SortOrder,
            IsActive = oldNode.IsActive,
            Notes = oldNode.Notes,
            PositionHolderEmployeeId = oldNode.PositionHolderEmployeeId,
            AssignedEmployeeIdsCsv = oldNode.AssignedEmployeeIdsCsv
        };

        db.OrganizationNodes.Add(clone);
        await db.SaveChangesAsync();

        foreach (var child in source.Where(x => x.ParentId == oldNode.Id)
                                    .OrderBy(x => x.SortOrder)
                                    .ThenBy(x => x.Title))
        {
            await CloneTreeAsync(source, child, revisionId, clone.Id);
        }
    }

    public async Task<bool> CanDeleteAsync(int id) =>
        !await db.Employees.AnyAsync(e =>
            e.OrganizationUnitId == id ||
            e.OrganizationDepartmentId == id ||
            e.OrganizationSectionId == id ||
            e.OrganizationPositionId == id) &&
        !await db.WorkflowSteps.AnyAsync(x => x.OrganizationNodeId == id) &&
        !await db.WorkflowTasks.AnyAsync(x => x.AssignedPositionId == id) &&
        !await db.WorkflowHistory.AnyAsync(x => x.ActorPositionId == id) &&
        !await db.OrganizationNodes.AnyAsync(x => x.ParentId == id);

    public async Task FinalizeAsync(int id)
    {
        var revision = await db.OrganizationStructureRevisions
            .Include(x => x.Nodes)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("نسخه ساختار یافت نشد.");

        if (revision.IsFinalized)
            return;

        revision.IsFinalized = true;
        revision.FinalizedAt = DateTime.UtcNow;

        // If the CEO position exists in the new finalized revision, carry forward the single
        // active holder's assignment from an older cloned version to the new CEO node.
        var newCeoNode = revision.Nodes.FirstOrDefault(x => x.RankType == "مدیرعامل" && x.IsActive);
        if (newCeoNode is not null)
        {
            var oldCeoAssignments = await db.Employees
                .Where(e => e.OrganizationPositionId.HasValue &&
                    db.OrganizationNodes.Any(n => n.Id == e.OrganizationPositionId.Value && n.RankType == "مدیرعامل"))
                .ToListAsync();
            foreach (var employee in oldCeoAssignments)
                employee.OrganizationPositionId = newCeoNode.Id;
        }

        await db.SaveChangesAsync();

        await audit.WriteAsync(
            "نهایی‌سازی ساختار",
            nameof(OrganizationStructureRevision),
            revision.Id.ToString(),
            revision.Title);
    }


    /// <summary>
    /// Administrator-only maintenance is enforced by the controller. Keep operational
    /// records and history, rebind compatible references, and never leave orphan inbox tasks.
    /// All changes and the revision removal are committed atomically.
    /// </summary>
    public async Task<string> DeleteRevisionWithDependenciesAsync(int revisionId, int? actorEmployeeId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var revision = await db.OrganizationStructureRevisions
            .FirstOrDefaultAsync(x => x.Id == revisionId)
            ?? throw new InvalidOperationException("نسخه ساختار سازمانی یافت نشد.");

        var oldNodes = await db.OrganizationNodes
            .Where(x => x.OrganizationStructureRevisionId == revisionId).ToListAsync();
        var oldIds = oldNodes.Select(x => x.Id).ToHashSet();
        var oldById = oldNodes.ToDictionary(x => x.Id);

        // Prefer the operative finalized snapshot, excluding the version being removed.
        // Never silently redirect an employee or task to a draft.
        var replacementRevision = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.Id != revisionId && x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.FinalizedAt)
            .ThenByDescending(x => x.Id).FirstOrDefaultAsync();
        replacementRevision ??= await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.Id != revisionId && x.IsFinalized)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.FinalizedAt)
            .ThenByDescending(x => x.Id).FirstOrDefaultAsync();

        var replacementNodes = replacementRevision is null
            ? new List<OrganizationNode>()
            : await db.OrganizationNodes.AsNoTracking()
                .Where(x => x.OrganizationStructureRevisionId == replacementRevision.Id && x.IsActive)
                .ToListAsync();
        var replacementByKey = replacementNodes
            .GroupBy(x => (x.Code, x.RankType))
            .ToDictionary(x => x.Key, x => x.First().Id);

        int? Remap(int? id)
        {
            if (!id.HasValue || !oldById.TryGetValue(id.Value, out var old)) return id;
            return replacementByKey.TryGetValue((old.Code, old.RankType), out var newId)
                ? newId : null;
        }

        var employees = await db.Employees.Where(e =>
            (e.OrganizationUnitId.HasValue && oldIds.Contains(e.OrganizationUnitId.Value)) ||
            (e.OrganizationDepartmentId.HasValue && oldIds.Contains(e.OrganizationDepartmentId.Value)) ||
            (e.OrganizationSectionId.HasValue && oldIds.Contains(e.OrganizationSectionId.Value)) ||
            (e.OrganizationPositionId.HasValue && oldIds.Contains(e.OrganizationPositionId.Value))).ToListAsync();
        foreach (var employee in employees)
        {
            employee.OrganizationUnitId = Remap(employee.OrganizationUnitId);
            employee.OrganizationDepartmentId = Remap(employee.OrganizationDepartmentId);
            employee.OrganizationSectionId = Remap(employee.OrganizationSectionId);
            employee.OrganizationPositionId = Remap(employee.OrganizationPositionId);
            employee.UpdatedAt = DateTime.UtcNow;
        }

        var steps = await db.WorkflowSteps
            .Include(x => x.WorkflowDefinition)
            .Where(x => x.OrganizationNodeId.HasValue && oldIds.Contains(x.OrganizationNodeId.Value))
            .ToListAsync();
        var disabledDefinitions = new HashSet<int>();
        foreach (var step in steps)
        {
            step.OrganizationNodeId = Remap(step.OrganizationNodeId);
            // A position-based template without a corresponding surviving position
            // must not launch new requests with an invalid routing configuration.
            if (!step.OrganizationNodeId.HasValue &&
                step.AssignmentType == "Position" && !step.WorkflowDefinition.IsRuntimeInstance)
            {
                step.WorkflowDefinition.IsActive = false;
                step.WorkflowDefinition.UpdatedAt = DateTime.UtcNow;
                disabledDefinitions.Add(step.WorkflowDefinitionId);
            }
        }

        var tasks = await db.WorkflowTasks
            .Where(x => x.AssignedPositionId.HasValue && oldIds.Contains(x.AssignedPositionId.Value))
            .ToListAsync();
        var unrouteableInstances = new HashSet<int>();
        foreach (var task in tasks)
        {
            var previousPositionId = task.AssignedPositionId;
            task.AssignedPositionId = Remap(previousPositionId);
            if (task.Status == "Pending" && !task.AssignedPositionId.HasValue &&
                !task.AssignedEmployeeId.HasValue)
                unrouteableInstances.Add(task.WorkflowInstanceId);
        }

        var history = await db.WorkflowHistory
            .Where(x => x.ActorPositionId.HasValue && oldIds.Contains(x.ActorPositionId.Value))
            .ToListAsync();
        foreach (var record in history)
        {
            var old = oldById[record.ActorPositionId!.Value];
            // Historical actions retain the actual actor and the original position label;
            // mapping an old actor to a later organizational position would falsify history.
            var note = "جایگاه در ساختار حذف‌شده: " + old.Title + " (" + old.Code + ")";
            record.Comment = string.IsNullOrWhiteSpace(record.Comment)
                ? note : record.Comment + Environment.NewLine + note;
            record.ActorPositionId = null;
        }

        var renewalSettings = await db.ContractRenewalWorkflowSettings
            .Where(x => (x.SecurityPositionId.HasValue && oldIds.Contains(x.SecurityPositionId.Value)) ||
                        (x.HumanResourcesPositionId.HasValue && oldIds.Contains(x.HumanResourcesPositionId.Value)))
            .ToListAsync();
        foreach (var item in renewalSettings)
        {
            item.SecurityPositionId = Remap(item.SecurityPositionId);
            item.HumanResourcesPositionId = Remap(item.HumanResourcesPositionId);
            item.UpdatedAt = DateTime.UtcNow;
        }

        // For active tasks with no valid successor and no explicit recipient, cancel
        // the entire unfinished workflow, not just one task: otherwise the inbox
        // and the parent request would disagree about the active routing state.
        var cancelled = 0;
        if (unrouteableInstances.Count > 0)
        {
            var instances = await db.WorkflowInstances
                .Where(x => unrouteableInstances.Contains(x.Id) && x.Status == "InProgress")
                .ToListAsync();
            var validInstanceIds = instances.Select(x => x.Id).ToHashSet();
            var pending = await db.WorkflowTasks
                .Where(x => validInstanceIds.Contains(x.WorkflowInstanceId) && x.Status == "Pending")
                .ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var pendingTask in pending)
            {
                pendingTask.Status = "Cancelled";
                pendingTask.CompletedAt = now;
                pendingTask.Action = "OrganizationRevisionDeleted";
            }
            foreach (var instance in instances)
            {
                instance.Status = "Cancelled";
                instance.CompletedAt = now;
                db.WorkflowHistory.Add(new WorkflowHistory
                {
                    WorkflowInstanceId = instance.Id,
                    FromStatus = "InProgress",
                    ToStatus = "Cancelled",
                    Action = "OrganizationRevisionDeleted",
                    Comment = "گردش کار به‌علت حذف نسخه ساختار سازمانی و نبود جایگاه جایگزین لغو شد."
                });
            }
            var renewalRequests = await db.ContractRenewalRequests
                .Where(x => x.WorkflowInstanceId.HasValue && validInstanceIds.Contains(x.WorkflowInstanceId.Value))
                .ToListAsync();
            foreach (var request in renewalRequests)
            {
                request.Status = "Cancelled";
                request.CompletedAt = now;
            }
            cancelled = instances.Count;
        }

        // Persist FK updates before deleting the dependent structure nodes.
        await db.SaveChangesAsync();
        db.OrganizationChanges.RemoveRange(db.OrganizationChanges
            .Where(x => x.OrganizationStructureRevisionId == revisionId));
        await db.SaveChangesAsync();

        var remaining = oldNodes.ToList();
        while (remaining.Count > 0)
        {
            var parents = remaining.Where(x => x.ParentId.HasValue)
                .Select(x => x.ParentId!.Value).ToHashSet();
            var leaves = remaining.Where(x => !parents.Contains(x.Id)).ToList();
            if (leaves.Count == 0)
                throw new InvalidOperationException("درخت ساختار دارای چرخه است؛ هیچ داده‌ای حذف نشد.");
            db.OrganizationNodes.RemoveRange(leaves);
            await db.SaveChangesAsync();
            var leafIds = leaves.Select(x => x.Id).ToHashSet();
            remaining.RemoveAll(x => leafIds.Contains(x.Id));
        }

        db.OrganizationStructureRevisions.Remove(revision);
        db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = actorEmployeeId,
            Action = "حذف نسخه ساختار با انتقال وابستگی‌ها",
            EntityName = nameof(OrganizationStructureRevision),
            EntityId = revisionId.ToString(),
            Description = "نسخه: " + revision.RevisionCode +
                " | گره‌ها: " + oldNodes.Count +
                " | کارکنان: " + employees.Count +
                " | مراحل: " + steps.Count +
                " | وظایف: " + tasks.Count +
                " | تاریخچه: " + history.Count +
                " | تنظیمات: " + renewalSettings.Count +
                " | گردش‌کار لغوشده: " + cancelled
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return "نسخه ساختار حذف شد. " + employees.Count + " کارمند و " +
            tasks.Count + " وظیفه بررسی شدند؛ " + cancelled +
            " گردش‌کار فاقد مقصد جایگزین لغو شد و " +
            disabledDefinitions.Count + " تعریف گردش‌کار نیازمند تنظیم مجدد است.";
    }

    public static string? ValidateHierarchy(
        string parentRank, string childRank, bool isRoot)
    {
        if (childRank is not ("مدیرعامل" or "معاونت" or "مدیریت" or "ریاست" or "سرپرستی"))
            return "رده سازمانی معتبر نیست.";

        if (isRoot)
            return childRank is "مدیرعامل" or "معاونت" or "مدیریت"
                ? null
                : "در ریشه درخت فقط رده مدیرعامل، معاونت یا مدیریت مجاز است.";

        return parentRank switch
        {
            "مدیرعامل" when childRank == "معاونت" => null,
            "معاونت" when childRank == "مدیریت" => null,
            "مدیریت" when childRank == "ریاست" => null,
            "ریاست" when childRank == "سرپرستی" => null,
            _ => "ترتیب رده سازمانی نامعتبر است. ترتیب پیشنهادی: مدیرعامل ← معاونت ← مدیریت ← ریاست ← سرپرستی."
        };
    }
}