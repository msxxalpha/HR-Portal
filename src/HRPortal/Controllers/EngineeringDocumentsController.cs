using System.Security.Claims;
using System.Text.Json;
using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public sealed class EngineeringDocumentsController(
    HRPortalDbContext db,
    EngineeringDocumentFileStore fileStore,
    EngineeringDocumentImageStore imageStore,
    IAntiforgery antiforgery,
    AuditService audit) : Controller
{
    [HttpGet, Authorize(Policy = "EngineeringDocuments.Manage")]
    public async Task<IActionResult> Manage(int? nodeId)
    {
        var nodes = await db.EngineeringDocumentNodes.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Title)
            .Select(x => new { id = x.Id, parentId = x.ParentId, title = x.Title, sortOrder = x.SortOrder, isActive = x.IsActive })
            .ToListAsync();
        ViewBag.TreeJson = JsonSerializer.Serialize(nodes);
        ViewBag.SelectedNodeId = nodeId;
        ViewBag.Types = await db.EngineeringDocumentTypes.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToListAsync();
        return View();
    }

    [HttpGet, Authorize(Policy = "EngineeringDocuments.Manage")]
    public async Task<IActionResult> BasicInfo(int? editId)
    {
        ViewBag.Editing = editId.HasValue
            ? await db.EngineeringDocumentTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == editId.Value)
            : null;
        return View(await db.EngineeringDocumentTypes.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToListAsync());
    }

    [HttpGet, Authorize(Policy = "EngineeringDocuments.Manage")]
    public async Task<IActionResult> NodeDocuments(int nodeId)
    {
        var exists = await db.EngineeringDocumentNodes.AsNoTracking().AnyAsync(x => x.Id == nodeId);
        if (!exists) return NotFound();

        var rows = await db.EngineeringDocuments.AsNoTracking()
            .Where(x => x.NodeId == nodeId)
            .OrderBy(x => x.Title)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.TypeId,
                TypeTitle = x.Type.Title,
                x.DocumentNumber,
                x.RevisionLabel,
                x.Keywords,
                x.Description,
                x.PublishDate,
                x.EffectiveDate,
                x.IsActive,
                VersionNumber = x.Versions.Where(v => v.IsCurrent).Select(v => (int?)v.VersionNumber).FirstOrDefault() ?? 0,
                UploadedAtUtc = x.Versions.Where(v => v.IsCurrent).Select(v => (DateTime?)v.UploadedAtUtc).FirstOrDefault(),
                UploadedByName = x.Versions.Where(v => v.IsCurrent).Select(v => v.UploadedByName).FirstOrDefault() ?? ""
            })
            .ToListAsync();

        return Json(rows.Select(x => new
        {
            x.Id, x.Title, x.TypeId, x.TypeTitle, x.DocumentNumber, x.RevisionLabel, x.Keywords, x.Description,
            publishDate = PersianDateService.Format(x.PublishDate),
            effectiveDate = x.EffectiveDate.HasValue ? PersianDateService.Format(x.EffectiveDate.Value) : "",
            x.IsActive, x.VersionNumber,
            uploadedAt = x.UploadedAtUtc.HasValue ? PersianDateService.FormatDateTime(x.UploadedAtUtc.Value.ToLocalTime()) : "—",
            x.UploadedByName
        }));
    }

    [HttpPost, Authorize(Policy = "EngineeringDocuments.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateNode(int? parentId, string title, int sortOrder = 0)
    {
        title = (title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return Json(new { success = false, message = "عنوان گره الزامی و حداکثر ۲۰۰ نویسه است." });
        if (parentId.HasValue)
        {
            var parent = await db.EngineeringDocumentNodes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == parentId.Value);
            if (parent is null)
                return Json(new { success = false, message = "گره والد پیدا نشد." });
            if (await db.EngineeringDocuments.AnyAsync(x => x.NodeId == parentId.Value))
                return Json(new { success = false, message = "گره‌ای که مستند دارد نمی‌تواند زیرگره جدید داشته باشد؛ ابتدا مستندها را به گره دیگری منتقل کنید." });
        }

        var lastOrder = await db.EngineeringDocumentNodes
            .Where(x => x.ParentId == parentId)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync() ?? 0;
        var node = new EngineeringDocumentNode
        {
            ParentId = parentId,
            Title = title,
            SortOrder = sortOrder > 0 ? sortOrder : lastOrder + 10,
            IsActive = true
        };
        db.EngineeringDocumentNodes.Add(node);
        await db.SaveChangesAsync();
        await audit.WriteAsync("ایجاد گره آرشیو مستندات مهندسی", nameof(EngineeringDocumentNode), node.Id.ToString(), title);
        return Json(new { success = true, id = node.Id, title = node.Title, parentId = node.ParentId });
    }

    [HttpPost, Authorize(Policy = "EngineeringDocuments.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNode(int id, string title)
    {
        var node = await db.EngineeringDocumentNodes.FindAsync(id);
        if (node is null) return Json(new { success = false, message = "گره پیدا نشد." });
        title = (title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return Json(new { success = false, message = "عنوان گره الزامی و حداکثر ۲۰۰ نویسه است." });
        node.Title = title;
        node.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync("ویرایش گره آرشیو مستندات مهندسی", nameof(EngineeringDocumentNode), id.ToString(), title);
        return Json(new { success = true, id = node.Id });
    }

    [HttpPost, Authorize(Policy = "EngineeringDocuments.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNode(int id)
    {
        var node = await db.EngineeringDocumentNodes.FindAsync(id);
        if (node is null) return Json(new { success = false, message = "گره پیدا نشد." });
        if (await db.EngineeringDocumentNodes.AnyAsync(x => x.ParentId == id))
            return Json(new { success = false, message = "ابتدا زیرگره‌های این گره را جابجا یا حذف کنید." });
        if (await db.EngineeringDocuments.AnyAsync(x => x.NodeId == id))
            return Json(new { success = false, message = "این گره دارای مستند یا سابقه نسخه است و قابل حذف نیست." });

        db.EngineeringDocumentNodes.Remove(node);
        await db.SaveChangesAsync();
        await audit.WriteAsync("حذف گره آرشیو مستندات مهندسی", nameof(EngineeringDocumentNode), id.ToString(), node.Title);
        return Json(new { success = true });
    }

    [HttpPost, Authorize(Policy = "EngineeringDocuments.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveNode(int id, int? parentId, int position)
    {
        var node = await db.EngineeringDocumentNodes.FindAsync(id);
        if (node is null) return Json(new { success = false, message = "گره مبدأ پیدا نشد." });
        if (parentId == id)
            return Json(new { success = false, message = "یک گره نمی‌تواند والد خودش باشد." });
        if (parentId.HasValue)
        {
            if (!await db.EngineeringDocumentNodes.AnyAsync(x => x.Id == parentId.Value))
                return Json(new { success = false, message = "گره مقصد پیدا نشد." });
            if (await db.EngineeringDocuments.AnyAsync(x => x.NodeId == parentId.Value))
                return Json(new { success = false, message = "گره مقصد دارای مستند است و نمی‌تواند والد گره دیگری شود." });

            var ancestorId = parentId;
            while (ancestorId.HasValue)
            {
                if (ancestorId.Value == id)
                    return Json(new { success = false, message = "جابجایی باعث ایجاد چرخه در درختواره می‌شود." });
                ancestorId = await db.EngineeringDocumentNodes.AsNoTracking()
                    .Where(x => x.Id == ancestorId.Value)
                    .Select(x => x.ParentId)
                    .FirstOrDefaultAsync();
            }
        }

        node.ParentId = parentId;
        node.SortOrder = Math.Max(0, position) * 10;
        node.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync("جابجایی گره آرشیو مستندات مهندسی", nameof(EngineeringDocumentNode), id.ToString(),
            "گره «" + node.Title + "» جابجا شد.");
        return Json(new { success = true });
    }

    [HttpPost, Authorize(Policy = "EngineeringDocuments.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveType(int id, string title, string? description, int sortOrder = 0, bool isActive = true)
    {
        title = (title ?? "").Trim();
        description = (description ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 150)
        {
            TempData["Error"] = "عنوان نوع مستند الزامی و حداکثر ۱۵۰ نویسه است.";
            return RedirectToAction(nameof(BasicInfo));
        }
        if (description.Length > 500)
        {
            TempData["Error"] = "شرح نوع مستند نمی‌تواند بیش از ۵۰۰ نویسه باشد.";
            return RedirectToAction(nameof(BasicInfo));
        }
        if (await db.EngineeringDocumentTypes.AnyAsync(x => x.Id != id && x.Title == title))
        {
            TempData["Error"] = "این عنوان نوع مستند قبلاً ثبت شده است.";
            return RedirectToAction(nameof(BasicInfo), new { editId = id > 0 ? id : (int?)null });
        }

        EngineeringDocumentType type;
        if (id > 0)
        {
            type = await db.EngineeringDocumentTypes.FindAsync(id) ?? throw new InvalidOperationException("نوع مستند پیدا نشد.");
        }
        else
        {
            type = new EngineeringDocumentType();
            db.EngineeringDocumentTypes.Add(type);
        }
        type.Title = title;
        type.Description = description;
        type.SortOrder = Math.Max(0, sortOrder);
        type.IsActive = isActive;
        type.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync(id > 0 ? "ویرایش نوع مستند مهندسی" : "ایجاد نوع مستند مهندسی",
            nameof(EngineeringDocumentType), type.Id.ToString(), title);
        TempData["Success"] = "اطلاعات نوع مستند ذخیره شد.";
        return RedirectToAction(nameof(BasicInfo));
    }

    [HttpPost, Authorize(Policy = "EngineeringDocuments.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleType(int id)
    {
        var type = await db.EngineeringDocumentTypes.FindAsync(id);
        if (type is null) return NotFound();
        type.IsActive = !type.IsActive;
        type.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync(type.IsActive ? "فعال‌سازی نوع مستند مهندسی" : "غیرفعال‌سازی نوع مستند مهندسی",
            nameof(EngineeringDocumentType), id.ToString(), type.Title);
        return RedirectToAction(nameof(BasicInfo));
    }

    [HttpPost, Authorize(Policy = "EngineeringDocuments.Manage"), ValidateAntiForgeryToken, RequestSizeLimit(40_000_000)]
    public async Task<IActionResult> Upload(
        int nodeId, int documentId, int typeId, string title, string publishDate,
        string? effectiveDate, string? documentNumber, string? revisionLabel,
        string? keywords, string? description, string? changeNote, IFormFile? file)
    {
        title = (title ?? "").Trim();
        if (title.Length is < 1 or > 250)
            return UploadError(nodeId, "عنوان مستند الزامی و حداکثر ۲۵۰ نویسه است.");
        if (!PersianDateService.TryParse(publishDate, out var parsedPublishDate))
            return UploadError(nodeId, "تاریخ انتشار را با تقویم شمسی وارد کنید.");
        DateTime? parsedEffectiveDate = null;
        if (!string.IsNullOrWhiteSpace(effectiveDate))
        {
            if (!PersianDateService.TryParse(effectiveDate, out var effective))
                return UploadError(nodeId, "تاریخ اجرا معتبر نیست.");
            parsedEffectiveDate = effective;
        }

        documentNumber = (documentNumber ?? "").Trim();
        revisionLabel = (revisionLabel ?? "").Trim();
        keywords = (keywords ?? "").Trim();
        description = (description ?? "").Trim();
        changeNote = (changeNote ?? "").Trim();
        if (documentNumber.Length > 100 || revisionLabel.Length > 50 || keywords.Length > 500 ||
            description.Length > 2000 || changeNote.Length > 500)
            return UploadError(nodeId, "طول یکی از فیلدهای شماره، بازنگری، کلیدواژه، شرح یا یادداشت نسخه بیش از حد مجاز است.");

        var node = await db.EngineeringDocumentNodes.FirstOrDefaultAsync(x => x.Id == nodeId && x.IsActive);
        if (node is null) return UploadError(nodeId, "گره انتخاب‌شده پیدا نشده یا غیرفعال است.");
        if (await db.EngineeringDocumentNodes.AnyAsync(x => x.ParentId == nodeId))
            return UploadError(nodeId, "بارگذاری مستند فقط در آخرین گره هر شاخه مجاز است.");
        if (!await db.EngineeringDocumentTypes.AnyAsync(x => x.Id == typeId && x.IsActive))
            return UploadError(nodeId, "نوع مستند معتبر و فعال را انتخاب کنید.");
        if (file is null || file.Length == 0)
            return UploadError(nodeId, "انتخاب فایل PDF الزامی است.");

        EngineeringDocument? document = null;
        if (documentId > 0)
        {
            document = await db.EngineeringDocuments.FirstOrDefaultAsync(x => x.Id == documentId && x.NodeId == nodeId);
            if (document is null) return UploadError(nodeId, "مستند انتخاب‌شده متعلق به این گره نیست یا پیدا نشد.");
        }

        string storedName;
        try
        {
            storedName = await fileStore.SavePdfAsync(file, HttpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return UploadError(nodeId, exception.Message);
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
            var actorName = GetCurrentUserName();
            var personnelNumber = User.FindFirst("PersonnelNumber")?.Value ?? "";

            if (document is null)
            {
                document = new EngineeringDocument
                {
                    NodeId = nodeId,
                    CreatedByName = actorName,
                    CreatedByPersonnelNumber = personnelNumber
                };
                db.EngineeringDocuments.Add(document);
            }

            document.TypeId = typeId;
            document.Title = title;
            document.DocumentNumber = documentNumber;
            document.RevisionLabel = revisionLabel;
            document.PublishDate = parsedPublishDate;
            document.EffectiveDate = parsedEffectiveDate;
            document.Keywords = keywords;
            document.Description = description;
            document.IsActive = true;
            document.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(HttpContext.RequestAborted);

            var previousVersions = await db.EngineeringDocumentVersions
                .Where(x => x.EngineeringDocumentId == document.Id && x.IsCurrent)
                .ToListAsync(HttpContext.RequestAborted);
            foreach (var previous in previousVersions) previous.IsCurrent = false;
            if (previousVersions.Count > 0)
                await db.SaveChangesAsync(HttpContext.RequestAborted);

            var nextVersion = (await db.EngineeringDocumentVersions
                .Where(x => x.EngineeringDocumentId == document.Id)
                .Select(x => (int?)x.VersionNumber)
                .MaxAsync(HttpContext.RequestAborted) ?? 0) + 1;

            var originalName = (file.FileName ?? "document.pdf").Replace('\\', '/').Split('/').Last();
            if (originalName.Length > 260) originalName = originalName[^260..];
            db.EngineeringDocumentVersions.Add(new EngineeringDocumentVersion
            {
                EngineeringDocumentId = document.Id,
                VersionNumber = nextVersion,
                StoredFileName = storedName,
                OriginalFileName = originalName,
                ContentType = "application/pdf",
                FileSize = file.Length,
                IsCurrent = true,
                UploadedAtUtc = DateTime.UtcNow,
                UploadedByName = actorName,
                UploadedByPersonnelNumber = personnelNumber,
                ChangeNote = changeNote
            });

            await db.SaveChangesAsync(HttpContext.RequestAborted);
            await transaction.CommitAsync(HttpContext.RequestAborted);

            await audit.WriteAsync(documentId > 0 ? "بارگذاری نسخه جدید مستند مهندسی" : "ثبت مستند مهندسی",
                nameof(EngineeringDocument), document.Id.ToString(),
                "عنوان: " + title + " | نسخه: " + nextVersion + " | فایل: " + originalName);
            TempData["Success"] = documentId > 0
                ? "نسخه جدید مستند ثبت شد و نسخه قبلی در سوابق باقی ماند."
                : "مستند مهندسی با موفقیت ثبت شد.";
            return RedirectToAction(nameof(Manage), new { nodeId });
        }
        catch (Exception exception)
        {
            fileStore.Delete(storedName);
            TempData["Error"] = "ثبت مستند انجام نشد: " + exception.Message;
            return RedirectToAction(nameof(Manage), new { nodeId });
        }
    }

    [HttpPost, Authorize(Policy = "EngineeringDocuments.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleDocument(int id, int nodeId)
    {
        var document = await db.EngineeringDocuments.FirstOrDefaultAsync(x => x.Id == id && x.NodeId == nodeId);
        if (document is null) return NotFound();
        document.IsActive = !document.IsActive;
        document.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync(document.IsActive ? "فعال‌سازی مستند مهندسی" : "غیرفعال‌سازی مستند مهندسی",
            nameof(EngineeringDocument), id.ToString(), document.Title);
        TempData["Success"] = document.IsActive ? "مستند فعال شد." : "مستند از فهرست کاربران خارج شد؛ سوابق نسخه‌ها حفظ شده‌اند.";
        return RedirectToAction(nameof(Manage), new { nodeId });
    }

    [HttpGet, Authorize(Policy = "EngineeringDocuments.View")]
    public async Task<IActionResult> Index(
        string? q, int? nodeId, int? typeId, string? fromDate, string? toDate,
        string sort = "published", bool descending = true, int page = 1, int pageSize = 20)
    {
        var query = db.EngineeringDocuments.AsNoTracking()
            .Where(x => x.IsActive && x.Node.IsActive && !x.Node.Children.Any() && x.Versions.Any(v => v.IsCurrent));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x =>
                x.Title.Contains(term) ||
                x.DocumentNumber.Contains(term) ||
                x.RevisionLabel.Contains(term) ||
                x.Keywords.Contains(term) ||
                x.Description.Contains(term) ||
                x.Node.Title.Contains(term) ||
                x.Type.Title.Contains(term) ||
                x.Versions.Any(v => v.IsCurrent &&
                    (v.OriginalFileName.Contains(term) || v.UploadedByName.Contains(term))));
        }
        if (nodeId.HasValue) query = query.Where(x => x.NodeId == nodeId.Value);
        if (typeId.HasValue) query = query.Where(x => x.TypeId == typeId.Value);
        if (PersianDateService.TryParse(fromDate, out var from)) query = query.Where(x => x.PublishDate >= from);
        if (PersianDateService.TryParse(toDate, out var to)) query = query.Where(x => x.PublishDate <= to);

        IOrderedQueryable<EngineeringDocument> ordered = sort switch
        {
            "title" => descending ? query.OrderByDescending(x => x.Title) : query.OrderBy(x => x.Title),
            "type" => descending ? query.OrderByDescending(x => x.Type.Title) : query.OrderBy(x => x.Type.Title),
            "node" => descending ? query.OrderByDescending(x => x.Node.Title) : query.OrderBy(x => x.Node.Title),
            "number" => descending ? query.OrderByDescending(x => x.DocumentNumber) : query.OrderBy(x => x.DocumentNumber),
            "uploaded" => descending ? query.OrderByDescending(x => x.UpdatedAtUtc) : query.OrderBy(x => x.UpdatedAtUtc),
            _ => descending ? query.OrderByDescending(x => x.PublishDate).ThenBy(x => x.Title) : query.OrderBy(x => x.PublishDate).ThenBy(x => x.Title)
        };

        var total = await query.CountAsync();
        pageSize = new[] { 10, 20, 50, 100 }.Contains(pageSize) ? pageSize : 20;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, pageCount);

        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new EngineeringDocumentListItem
            {
                Id = x.Id,
                Title = x.Title,
                TypeTitle = x.Type.Title,
                NodeTitle = x.Node.Title,
                DocumentNumber = x.DocumentNumber,
                RevisionLabel = x.RevisionLabel,
                PublishDate = x.PublishDate,
                EffectiveDate = x.EffectiveDate,
                Keywords = x.Keywords,
                Description = x.Description,
                VersionNumber = x.Versions.Where(v => v.IsCurrent).Select(v => v.VersionNumber).FirstOrDefault(),
                OriginalFileName = x.Versions.Where(v => v.IsCurrent).Select(v => v.OriginalFileName).FirstOrDefault() ?? "",
                UploadedAtUtc = x.Versions.Where(v => v.IsCurrent).Select(v => v.UploadedAtUtc).FirstOrDefault(),
                UploadedByName = x.Versions.Where(v => v.IsCurrent).Select(v => v.UploadedByName).FirstOrDefault() ?? ""
            })
            .ToListAsync();

        ViewBag.Types = await db.EngineeringDocumentTypes.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToListAsync();
        ViewBag.Nodes = await db.EngineeringDocumentNodes.AsNoTracking()
            .Where(x => x.IsActive && !x.Children.Any())
            .OrderBy(x => x.Title).ToListAsync();

        return View(new EngineeringDocumentListViewModel
        {
            Documents = rows,
            Search = q?.Trim() ?? "",
            NodeId = nodeId,
            TypeId = typeId,
            FromDate = fromDate ?? "",
            ToDate = toDate ?? "",
            Sort = sort,
            Descending = descending,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            PageCount = pageCount
        });
    }

    [HttpGet, Authorize(Policy = "EngineeringDocuments.View")]
    public async Task<IActionResult> ViewDocument(int id)
    {
        var document = await db.EngineeringDocuments.AsNoTracking()
            .Where(x => x.Id == id && x.IsActive && x.Node.IsActive &&
                !x.Node.Children.Any() && x.Versions.Any(v => v.IsCurrent))
            .Select(x => new
            {
                x.Title,
                x.DocumentNumber,
                FileName = x.Versions.Where(v => v.IsCurrent).OrderByDescending(v => v.VersionNumber)
                    .Select(v => v.StoredFileName).FirstOrDefault()
            })
            .FirstOrDefaultAsync();
        if (document is null || string.IsNullOrWhiteSpace(document.FileName))
            return NotFound();

        try
        {
            var ownerKey = ReportTempOwner.GetKey(User);
            var watermark = GetCurrentUserName() + " | " +
                (User.FindFirst("PersonnelNumber")?.Value ?? User.FindFirst("AdminUsername")?.Value ?? "کاربر سامانه");
            var images = await imageStore.CreateAsync(
                fileStore.GetPath(document.FileName), ownerKey, watermark, HttpContext.RequestAborted);
            var tokens = antiforgery.GetAndStoreTokens(HttpContext);
            SetPrivateHeaders();
            await audit.WriteAsync("مشاهده مستند مهندسی", nameof(EngineeringDocument), id.ToString(),
                "عنوان: " + document.Title + " | شماره: " + document.DocumentNumber);
            return View("~/Views/Shared/SecureReportViewer.cshtml", new SecureReportViewerVm
            {
                Token = images.Token,
                PageCount = images.PageCount,
                PageUrl = Url.Action(nameof(DocumentPage), "EngineeringDocuments")!,
                CloseUrl = Url.Action(nameof(CloseDocument), "EngineeringDocuments")!,
                AntiForgeryToken = tokens.RequestToken ?? "",
                Title = "مشاهده مستند مهندسی | " + document.Title
            });
        }
        catch (Exception exception)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Content("نمایش تصویری مستند انجام نشد. ابزار تبدیل PDF به تصویر باید روی سرور نصب و مسیر آن در تنظیمات EngineeringDocuments:PdfToPpmPath تعیین شود. جزئیات: " +
                System.Net.WebUtility.HtmlEncode(exception.Message), "text/plain; charset=utf-8");
        }
    }

    [HttpGet, Authorize(Policy = "EngineeringDocuments.View")]
    public IActionResult DocumentPage(string token, int page)
    {
        try
        {
            var path = imageStore.GetPagePath(ReportTempOwner.GetKey(User), token, page);
            if (!System.IO.File.Exists(path)) return NotFound();
            SetPrivateHeaders();
            Response.Headers.ContentDisposition = "inline";
            return PhysicalFile(path, "image/png");
        }
        catch (ArgumentException) { return NotFound(); }
    }

    [HttpPost, Authorize(Policy = "EngineeringDocuments.View"), ValidateAntiForgeryToken]
    public IActionResult CloseDocument(string token)
    {
        imageStore.Delete(ReportTempOwner.GetKey(User), token);
        return NoContent();
    }

    private IActionResult UploadError(int nodeId, string message)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Manage), new { nodeId });
    }

    private string GetCurrentUserName() =>
        User.FindFirst(ClaimTypes.Name)?.Value ??
        User.FindFirst("AdminUsername")?.Value ??
        User.Identity?.Name ??
        User.FindFirst("PersonnelNumber")?.Value ??
        "کاربر سامانه";

    private void SetPrivateHeaders()
    {
        Response.Headers["Cache-Control"] = "private, no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; img-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'self';";
    }
}