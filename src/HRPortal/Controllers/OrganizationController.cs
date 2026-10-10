using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HRPortal.Controllers;

[Authorize(Policy = "Organization.View")]
public class OrganizationController(
    HRPortalDbContext db,
    OrganizationService service,
    EnvironmentSettingsService settings) : Controller
{
    public async Task<IActionResult> Index(int? revisionId)
    {
        var revisions = await service.RevisionsAsync();
        var canEditStructure =
            User.HasClaim("IsAdmin", "1") ||
            User.HasClaim("Permission", "Organization.Create") ||
            User.HasClaim("Permission", "Organization.Edit") ||
            User.HasClaim("Permission", "Organization.Delete") ||
            User.HasClaim("Permission", "Organization.Move") ||
            User.HasClaim("Permission", "Organization.Finalize");

        var current = await service.CurrentAsync();
        var selected = revisionId.HasValue
            ? revisions.FirstOrDefault(x => x.Id == revisionId.Value)
            : null;

        OrganizationStructureRevision? revision;
        if (selected is null)
        {
            revision = revisions.FirstOrDefault(x => !x.IsFinalized);
            if (revision is null)
                revision = canEditStructure ? await service.GetOrCreateDraftAsync() : current;
        }
        else if (selected.IsFinalized && canEditStructure && current?.Id == selected.Id)
        {
            // Selecting the current finalized version opens its editable working copy for an authorized user.
            revision = revisions.FirstOrDefault(x => !x.IsFinalized) ?? await service.GetOrCreateDraftAsync();
        }
        else
        {
            revision = selected;
        }

        if (revision is not null && !revisions.Any(x => x.Id == revision.Id))
            revisions = await service.RevisionsAsync();

        ViewBag.CompanyName = (await settings.GetAsync()).System.OrganizationName;
        ViewBag.Revisions = revisions;
        ViewBag.IsEditable = revision is not null && !revision.IsFinalized && canEditStructure;
        ViewBag.CanCreate = User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", "Organization.Create");
        ViewBag.CanEdit = User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", "Organization.Edit");
        ViewBag.CanDelete = User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", "Organization.Delete");
        ViewBag.CanMove = User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", "Organization.Move");
        ViewBag.CanFinalize = User.HasClaim("IsAdmin", "1") || User.HasClaim("Permission", "Organization.Finalize");

        if (revision is not null)
        {
            var nodes = await db.OrganizationNodes
                .AsNoTracking()
                .Where(x => x.OrganizationStructureRevisionId == revision.Id)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Title)
                .ToListAsync();

            var tree = nodes.Select(n => new
            {
                id = "node-" + n.Id,
                parent = n.ParentId.HasValue ? "node-" + n.ParentId.Value : "root",
                text = n.Title + "  [" + n.RankType + "]",
                data = new
                {
                    id = n.Id,
                    code = n.Code,
                    title = n.Title,
                    rankType = n.RankType,
                    sortOrder = n.SortOrder,
                    isActive = n.IsActive,
                    notes = n.Notes,
                    parentId = n.ParentId,
                    revisionId = n.OrganizationStructureRevisionId
                },
                state = new { opened = true }
            }).ToList();

            ViewBag.TreeJson = JsonSerializer.Serialize(tree);
            ViewBag.RevisionId = revision.Id;
            ViewBag.RevisionTitle = revision.Title;
            ViewBag.EffectiveDate = PersianDateService.Format(revision.EffectiveDate);
            ViewBag.IsFinalized = revision.IsFinalized;
            ViewBag.ChangeCount = await db.OrganizationChanges.CountAsync(x => x.OrganizationStructureRevisionId == revision.Id);
        }
        else
        {
            ViewBag.TreeJson = "[]";
            ViewBag.RevisionId = 0;
            ViewBag.RevisionTitle = "نسخه‌ای ایجاد نشده است";
            ViewBag.EffectiveDate = "";
            ViewBag.IsFinalized = true;
            ViewBag.ChangeCount = 0;
        }

        return View();
    }

    [HttpPost, Authorize(Policy = "Organization.Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> NewRevision(string effectiveDate, string title, string? notes)
    {
        if (!PersianDateService.TryParse(effectiveDate, out var date))
        {
            TempData["Error"] = "تاریخ اجرا را به‌صورت شمسی مانند ۱۴۰۵/۰۷/۰۵ وارد کنید.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "عنوان بازنگری الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        var revision = await service.CreateRevisionAsync(date, title.Trim(), notes);
        return RedirectToAction(nameof(Index), new { revisionId = revision.Id });
    }

    [HttpPost, Authorize(Policy = "Organization.Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateNode(
        int revisionId,
        int? parentId,
        string code,
        string title,
        string rankType,
        int sortOrder = 0,
        bool isActive = true,
        string? notes = null)
    {
        var result = await EnsureEditableRevision(revisionId);
        if (result is not null)
            return result;

        var validation = await ValidateNodeParentAsync(revisionId, parentId, rankType);
        if (validation is not null)
            return Json(new { success = false, message = validation });

        code = code.Trim();
        title = title.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title))
            return Json(new { success = false, message = "کد و عنوان الزامی است." });

        if (await db.OrganizationNodes.AnyAsync(x =>
                x.OrganizationStructureRevisionId == revisionId &&
                x.Code == code))
        {
            return Json(new { success = false, message = "کد این گره در این نسخه قبلاً استفاده شده است." });
        }

        var node = new OrganizationNode
        {
            OrganizationStructureRevisionId = revisionId,
            ParentId = parentId,
            Code = code,
            Title = title,
            RankType = rankType,
            SortOrder = sortOrder,
            IsActive = isActive,
            Notes = notes?.Trim()
        };

        db.OrganizationNodes.Add(node);
        db.OrganizationChanges.Add(new OrganizationChange
        {
            OrganizationStructureRevisionId = revisionId,
            ChangeType = "افزودن",
            EntityCode = code,
            Description = $"افزودن {title}"
        });

        await db.SaveChangesAsync();

        return Json(new
        {
            success = true,
            node = new
            {
                id = node.Id,
                parentId = node.ParentId,
                code = node.Code,
                title = node.Title,
                rankType = node.RankType,
                sortOrder = node.SortOrder,
                isActive = node.IsActive,
                notes = node.Notes
            }
        });
    }

    [HttpPost, Authorize(Policy = "Organization.Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNode(
        int id,
        string code,
        string title,
        string rankType,
        int sortOrder,
        bool isActive,
        string? notes)
    {
        var node = await db.OrganizationNodes
            .Include(x => x.Revision)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (node is null)
            return Json(new { success = false, message = "گره یافت نشد." });

        if (node.Revision.IsFinalized)
            return Json(new { success = false, message = "نسخه نهایی قابل ویرایش نیست." });

        if (await IsNodeUsedAsync(id))
            return Json(new { success = false, message = "این گره در اطلاعات کارکنان استفاده شده و قابل ویرایش مستقیم نیست." });

        var validation = await ValidateNodeParentAsync(node.OrganizationStructureRevisionId, node.ParentId, rankType);
        if (validation is not null)
            return Json(new { success = false, message = validation });

        if (await db.OrganizationNodes.AnyAsync(x =>
                x.Id != id &&
                x.OrganizationStructureRevisionId == node.OrganizationStructureRevisionId &&
                x.Code == code.Trim()))
            return Json(new { success = false, message = "کد این گره قبلاً استفاده شده است." });

        node.Code = code.Trim();
        node.Title = title.Trim();
        node.RankType = rankType;
        node.SortOrder = sortOrder;
        node.IsActive = isActive;
        node.Notes = notes?.Trim();

        db.OrganizationChanges.Add(new OrganizationChange
        {
            OrganizationStructureRevisionId = node.OrganizationStructureRevisionId,
            ChangeType = "ویرایش",
            EntityCode = node.Code,
            Description = $"ویرایش {node.Title}"
        });

        await db.SaveChangesAsync();

        return Json(new { success = true });
    }

    [HttpPost, Authorize(Policy = "Organization.Move"), ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveNode(int id, int? parentId, int position)
    {
        var node = await db.OrganizationNodes
            .Include(x => x.Revision)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (node is null)
            return Json(new { success = false, message = "گره یافت نشد." });

        if (node.Revision.IsFinalized)
            return Json(new { success = false, message = "نسخه نهایی قابل تغییر نیست." });

        if (node.ParentId == parentId)
        {
            node.SortOrder = position * 10;
            await db.SaveChangesAsync();
            return Json(new { success = true });
        }

        if (parentId == id)
            return Json(new { success = false, message = "یک گره نمی‌تواند والد خودش باشد." });

        var validation = await ValidateNodeParentAsync(node.OrganizationStructureRevisionId, parentId, node.RankType);
        if (validation is not null)
            return Json(new { success = false, message = validation });

        if (parentId.HasValue && await WouldCreateCycleAsync(id, parentId.Value))
            return Json(new { success = false, message = "جابجایی باعث ایجاد چرخه در ساختار می‌شود." });

        node.ParentId = parentId;
        node.SortOrder = position * 10;

        db.OrganizationChanges.Add(new OrganizationChange
        {
            OrganizationStructureRevisionId = node.OrganizationStructureRevisionId,
            ChangeType = "جابجایی",
            EntityCode = node.Code,
            Description = $"جابجایی {node.Title}"
        });

        await db.SaveChangesAsync();

        return Json(new { success = true });
    }

    [HttpPost, Authorize(Policy = "Organization.Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNode(int id)
    {
        var node = await db.OrganizationNodes
            .Include(x => x.Revision)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (node is null)
            return Json(new { success = false, message = "گره یافت نشد." });

        if (node.Revision.IsFinalized)
            return Json(new { success = false, message = "نسخه نهایی قابل حذف نیست." });

        if (await IsNodeUsedAsync(id))
            return Json(new { success = false, message = "این گره در اطلاعات کارکنان استفاده شده است." });

        if (await db.OrganizationNodes.AnyAsync(x => x.ParentId == id))
            return Json(new { success = false, message = "ابتدا زیرگره‌های این گره را منتقل یا حذف کنید." });

        db.OrganizationChanges.Add(new OrganizationChange
        {
            OrganizationStructureRevisionId = node.OrganizationStructureRevisionId,
            ChangeType = "حذف",
            EntityCode = node.Code,
            Description = $"حذف {node.Title}"
        });

        db.OrganizationNodes.Remove(node);
        await db.SaveChangesAsync();

        return Json(new { success = true });
    }

    [HttpPost, Authorize(Policy = "Organization.Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDraft(int revisionId)
    {
        if (!User.HasClaim("IsAdmin", "1"))
        {
            return Forbid();
        }

        var revision = await db.OrganizationStructureRevisions
            .FirstOrDefaultAsync(x => x.Id == revisionId);
        if (revision is null)
        {
            TempData["Error"] = "نسخه ساختار یافت نشد.";
            return RedirectToAction(nameof(Index));
        }

        if (revision.IsFinalized)
        {
            TempData["Error"] = "حذف کامل فقط برای نسخه پیشنویس مجاز است؛ نسخه نهایی قابل حذف نیست.";
            return RedirectToAction(nameof(Index), new { revisionId });
        }

        var nodeIds = await db.OrganizationNodes
            .Where(x => x.OrganizationStructureRevisionId == revisionId)
            .Select(x => x.Id)
            .ToListAsync();

        if (nodeIds.Count > 0 &&
            (await db.Employees.AnyAsync(e =>
                (e.OrganizationUnitId.HasValue && nodeIds.Contains(e.OrganizationUnitId.Value)) ||
                (e.OrganizationDepartmentId.HasValue && nodeIds.Contains(e.OrganizationDepartmentId.Value)) ||
                (e.OrganizationSectionId.HasValue && nodeIds.Contains(e.OrganizationSectionId.Value))) ||
             await db.WorkflowSteps.AnyAsync(x => x.OrganizationNodeId.HasValue && nodeIds.Contains(x.OrganizationNodeId.Value)) ||
             await db.WorkflowTasks.AnyAsync(x => x.AssignedPositionId.HasValue && nodeIds.Contains(x.AssignedPositionId.Value)) ||
             await db.WorkflowHistory.AnyAsync(x => x.ActorPositionId.HasValue && nodeIds.Contains(x.ActorPositionId.Value))))
        {
            TempData["Error"] = "این پیش‌نویس به کارکنان یا سوابق گردش کار ارجاع دارد و برای جلوگیری از شکستن ارجاعات حذف نشد.";
            return RedirectToAction(nameof(Index), new { revisionId });
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        db.OrganizationChanges.RemoveRange(
            db.OrganizationChanges.Where(x => x.OrganizationStructureRevisionId == revisionId));
        await db.SaveChangesAsync();

        var remainingNodes = await db.OrganizationNodes
            .Where(x => x.OrganizationStructureRevisionId == revisionId)
            .ToListAsync();
        while (remainingNodes.Count > 0)
        {
            var parentIdsWithChildren = remainingNodes
                .Where(x => x.ParentId.HasValue)
                .Select(x => x.ParentId!.Value)
                .ToHashSet();
            var leaves = remainingNodes.Where(x => !parentIdsWithChildren.Contains(x.Id)).ToList();
            if (leaves.Count == 0)
                throw new InvalidOperationException("ساختار پیش‌نویس دارای چرخه است و حذف ایمن انجام نشد.");
            db.OrganizationNodes.RemoveRange(leaves);
            await db.SaveChangesAsync();
            var removedIds = leaves.Select(x => x.Id).ToHashSet();
            remainingNodes.RemoveAll(x => removedIds.Contains(x.Id));
        }

        db.OrganizationStructureRevisions.Remove(revision);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Success"] = "پیش‌نویس ساختار سازمانی به‌همراه تمام گره‌ها و تاریخچه تغییرات آن حذف شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = "Organization.Finalize"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalize(int revisionId)
    {
        try
        {
            await service.FinalizeAsync(revisionId);
            TempData["Success"] = "نسخه ساختار با موفقیت نهایی شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { revisionId });
    }

    private async Task<IActionResult?> EnsureEditableRevision(int revisionId)
    {
        var revision = await db.OrganizationStructureRevisions.FindAsync(revisionId);
        if (revision is null)
            return Json(new { success = false, message = "نسخه ساختار یافت نشد." });

        if (revision.IsFinalized)
            return Json(new { success = false, message = "ابتدا یک نسخه بازنگری جدید ایجاد کنید." });

        return null;
    }

    private async Task<string?> ValidateNodeParentAsync(int revisionId, int? parentId, string childRank)
    {
        if (parentId is null)
            return OrganizationService.ValidateHierarchy("", childRank, true);

        var parent = await db.OrganizationNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == parentId && x.OrganizationStructureRevisionId == revisionId);

        if (parent is null)
            return "والد انتخاب‌شده یافت نشد.";

        return OrganizationService.ValidateHierarchy(parent.RankType, childRank, false);
    }

    private async Task<bool> IsNodeUsedAsync(int id) =>
        await db.Employees.AnyAsync(e =>
            e.OrganizationUnitId == id ||
            e.OrganizationDepartmentId == id ||
            e.OrganizationSectionId == id) ||
        await db.WorkflowSteps.AnyAsync(x => x.OrganizationNodeId == id) ||
        await db.WorkflowTasks.AnyAsync(x => x.AssignedPositionId == id) ||
        await db.WorkflowHistory.AnyAsync(x => x.ActorPositionId == id);

    private async Task<bool> WouldCreateCycleAsync(int id, int newParentId)
    {
        var current = await db.OrganizationNodes.FindAsync(newParentId);
        var guard = 0;

        while (current is not null && current.ParentId.HasValue && guard++ < 1000)
        {
            if (current.ParentId.Value == id)
                return true;

            current = await db.OrganizationNodes.FindAsync(current.ParentId.Value);
        }

        return false;
    }
}