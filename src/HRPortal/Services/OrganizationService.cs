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
            Notes = oldNode.Notes
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
            e.OrganizationSectionId == id) &&
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

        if (await db.OrganizationStructureRevisions.AnyAsync(x =>
                x.Id != id &&
                x.IsFinalized &&
                x.EffectiveDate == revision.EffectiveDate))
        {
            throw new InvalidOperationException("برای این تاریخ نسخه نهایی دیگری وجود دارد.");
        }

        revision.IsFinalized = true;
        revision.FinalizedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        await audit.WriteAsync(
            "نهایی‌سازی ساختار",
            nameof(OrganizationStructureRevision),
            revision.Id.ToString(),
            revision.Title);
    }

    public static string? ValidateHierarchy(
        string parentRank, string childRank, bool isRoot)
    {
        if (childRank is not ("معاونت" or "مدیریت" or "ریاست" or "سرپرستی"))
            return "رده سازمانی معتبر نیست.";

        if (isRoot)
            return childRank is "معاونت" or "مدیریت"
                ? null
                : "در ریشه درخت فقط رده معاونت یا مدیریت مجاز است.";

        return parentRank switch
        {
            "معاونت" when childRank == "مدیریت" => null,
            "مدیریت" when childRank == "ریاست" => null,
            "ریاست" when childRank == "سرپرستی" => null,
            _ => "ترتیب رده سازمانی نامعتبر است. ترتیب مجاز: معاونت ← مدیریت ← ریاست ← سرپرستی."
        };
    }
}