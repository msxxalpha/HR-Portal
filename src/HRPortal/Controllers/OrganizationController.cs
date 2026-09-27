using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "AdminOnly")]
public class OrganizationController(HRPortalDbContext db, OrganizationService service) : Controller
{
    public async Task<IActionResult> Index(int? revisionId)
    {
        var revisions = await service.RevisionsAsync();
        var current = revisionId.HasValue
            ? revisions.FirstOrDefault(x => x.Id == revisionId)
            : await service.CurrentAsync();

        if (current is not null)
        {
            var nodes = await db.OrganizationNodes
                .Where(x => x.OrganizationStructureRevisionId == current.Id)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Title)
                .ToListAsync();

            var map = nodes.ToDictionary(x => x.Id);
            foreach (var node in nodes)
                node.Children = new List<OrganizationNode>();

            foreach (var node in nodes.Where(x => x.ParentId.HasValue))
                if (map.TryGetValue(node.ParentId!.Value, out var parent))
                    parent.Children.Add(node);

            current.Nodes = nodes;
            current.Changes = await db.OrganizationChanges
                .Where(x => x.OrganizationStructureRevisionId == current.Id)
                .OrderByDescending(x => x.ChangedAt)
                .ToListAsync();
        }

        ViewBag.Revisions = revisions;
        return View(current);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> NewRevision(string effectiveDate, string title, string? notes)
    {
        if (!PersianDateService.TryParse(effectiveDate, out var date))
        {
            TempData["Error"] = "تاریخ اجرا را به صورت شمسی مانند ۱۴۰۵/۰۷/۰۵ وارد کنید.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "عنوان بازنگری الزامی است.";
            return RedirectToAction(nameof(Index));
        }

        await service.CreateRevisionAsync(date, title.Trim(), notes);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNode(
        int revisionId, int? parentId, string code, string title, string rankType,
        int sortOrder = 0, bool isActive = true, string? notes = null)
    {
        var revision = await db.OrganizationStructureRevisions.FindAsync(revisionId);
        if (revision is null || revision.IsFinalized)
        {
            TempData["Error"] = "فقط نسخه در حال بازنگری قابل تغییر است.";
            return RedirectToAction(nameof(Index), new { revisionId });
        }

        code = code?.Trim() ?? "";
        title = title?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "کد و عنوان گره الزامی است.";
            return RedirectToAction(nameof(Index), new { revisionId });
        }

        var validation = await ValidateHierarchyAsync(parentId, rankType, revisionId);
        if (validation is not null)
        {
            TempData["Error"] = validation;
            return RedirectToAction(nameof(Index), new { revisionId });
        }

        if (await db.OrganizationNodes.AnyAsync(x =>
                x.OrganizationStructureRevisionId == revisionId && x.Code == code))
        {
            TempData["Error"] = "کد این گره در این نسخه قبلاً استفاده شده است.";
            return RedirectToAction(nameof(Index), new { revisionId });
        }

        db.OrganizationNodes.Add(new OrganizationNode
        {
            OrganizationStructureRevisionId = revisionId,
            ParentId = parentId,
            Code = code,
            Title = title,
            RankType = rankType,
            SortOrder = sortOrder,
            IsActive = isActive,
            Notes = notes
        });

        db.OrganizationChanges.Add(new OrganizationChange
        {
            OrganizationStructureRevisionId = revisionId,
            ChangeType = "افزودن",
            EntityCode = code,
            Description = $"افزودن {title}"
        });

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { revisionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditNode(
        int id, string code, string title, string rankType,
        int sortOrder, bool isActive, string? notes)
    {
        var node = await db.OrganizationNodes
            .Include(x => x.Revision)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (node is null)
            return NotFound();

        if (node.Revision.IsFinalized)
        {
            TempData["Error"] = "نسخه نهایی قابل ویرایش نیست.";
        }
        else if (await IsNodeUsedAsync(node.Id))
        {
            TempData["Error"] = "این گره در اطلاعات کارکنان استفاده شده است و ویرایش مستقیم آن مجاز نیست.";
        }
        else
        {
            var validation = await ValidateHierarchyAsync(node.ParentId, rankType, node.OrganizationStructureRevisionId);
            if (validation is not null)
                TempData["Error"] = validation;
            else
            {
                node.Code = code.Trim();
                node.Title = title.Trim();
                node.RankType = rankType;
                node.SortOrder = sortOrder;
                node.IsActive = isActive;
                node.Notes = notes;

                db.OrganizationChanges.Add(new OrganizationChange
                {
                    OrganizationStructureRevisionId = node.OrganizationStructureRevisionId,
                    ChangeType = "ویرایش",
                    EntityCode = node.Code,
                    Description = $"ویرایش {node.Title}"
                });

                await db.SaveChangesAsync();
            }
        }

        return RedirectToAction(nameof(Index), new { revisionId = node.OrganizationStructureRevisionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNode(int id)
    {
        var node = await db.OrganizationNodes
            .Include(x => x.Revision)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (node is null)
            return NotFound();

        if (node.Revision.IsFinalized ||
            await IsNodeUsedAsync(node.Id) ||
            await db.OrganizationNodes.AnyAsync(x => x.ParentId == node.Id))
        {
            TempData["Error"] = "حذف این گره مجاز نیست؛ نسخه نهایی، مورد استفاده کارکنان یا دارای زیرگره است.";
        }
        else
        {
            db.OrganizationChanges.Add(new OrganizationChange
            {
                OrganizationStructureRevisionId = node.OrganizationStructureRevisionId,
                ChangeType = "حذف",
                EntityCode = node.Code,
                Description = $"حذف {node.Title}"
            });

            db.OrganizationNodes.Remove(node);
            await db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index), new { revisionId = node.OrganizationStructureRevisionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalize(int revisionId)
    {
        try { await service.FinalizeAsync(revisionId); }
        catch (Exception ex) { TempData["Error"] = ex.Message; }

        return RedirectToAction(nameof(Index), new { revisionId });
    }

    private async Task<bool> IsNodeUsedAsync(int id) =>
        await db.Employees.AnyAsync(e =>
            e.OrganizationUnitId == id ||
            e.OrganizationDepartmentId == id ||
            e.OrganizationSectionId == id);

    private async Task<string?> ValidateHierarchyAsync(int? parentId, string rankType, int revisionId)
    {
        if (rankType is not ("مدیریت" or "ریاست" or "سرپرستی"))
            return "نوع رده نامعتبر است.";

        if (parentId is null)
            return rankType == "مدیریت" ? null : "گره ریشه فقط باید از رده مدیریت باشد.";

        var parent = await db.OrganizationNodes.FirstOrDefaultAsync(
            x => x.Id == parentId && x.OrganizationStructureRevisionId == revisionId);

        if (parent is null)
            return "والد انتخاب‌شده یافت نشد.";

        if (parent.RankType == "مدیریت" && rankType != "ریاست")
            return "زیرمجموعه مدیریت باید از رده ریاست باشد.";

        if (parent.RankType == "ریاست" && rankType != "سرپرستی")
            return "زیرمجموعه ریاست باید از رده سرپرستی باشد.";

        if (parent.RankType == "سرپرستی")
            return "رده سرپرستی نمی‌تواند زیررده دیگری داشته باشد.";

        return null;
    }
}