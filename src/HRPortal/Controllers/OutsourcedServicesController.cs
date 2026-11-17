using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public sealed class OutsourcedServicesController(
    HRPortalDbContext db,
    WorkflowFormSubmissionService workflowSubmission,
    WorkflowService workflow,
    AuditService audit,
    IWebHostEnvironment environment) : Controller
{
    private static readonly string[] Units =
    [
        "عدد", "ساعت", "نفر-ساعت", "روز", "متر", "مترمربع", "مترمکعب",
        "کیلوگرم", "تن", "دستگاه", "سرویس", "پروژه", "فقره", "بسته", "سایر"
    ];

    private static readonly Dictionary<string, string> Priorities = new()
    {
        ["Normal"] = "عادی", ["Urgent"] = "فوری", ["Emergency"] = "اضطراری"
    };

    private bool IsAdmin => User.HasClaim("IsAdmin", "1");
    private bool Has(string permission) => IsAdmin || User.HasClaim("Permission", permission);
    private bool TryEmployeeId(out int id) => int.TryParse(User.FindFirst("EmployeeId")?.Value, out id);

    [HttpGet]
    public async Task<IActionResult> Index(
        string? q,
        string? status,
        string? priority,
        string? fromJalali,
        string? toJalali,
        string? archived,
        string? sortBy,
        string? sortDirection,
        int page = 1,
        int pageSize = 20)
    {
        if (!Has("OutsourcedServices.Create") && !Has("OutsourcedServices.ViewAll")) return Forbid();

        var model = new OutsourcedServiceRequestListViewModel
        {
            Search = q?.Trim() ?? "",
            Status = status ?? "all",
            Priority = priority ?? "all",
            FromJalali = fromJalali?.Trim() ?? "",
            ToJalali = toJalali?.Trim() ?? "",
            ArchiveFilter = archived ?? "active",
            SortBy = sortBy ?? "CreatedAtUtc",
            SortDirection = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc",
            Page = Math.Max(1, page),
            PageSize = new[] { 10, 20, 50, 100 }.Contains(pageSize) ? pageSize : 20
        };

        IQueryable<OutsourcedServiceRequest> requestQuery = db.OutsourcedServiceRequests.AsNoTracking();
        if (!Has("OutsourcedServices.ViewAll"))
        {
            if (!TryEmployeeId(out var employeeId)) return Forbid();
            requestQuery = requestQuery.Where(x => x.RequesterEmployeeId == employeeId && !x.IsDeleted);
            model.ArchiveFilter = "active";
        }
        else
        {
            requestQuery = model.ArchiveFilter switch
            {
                "archived" => requestQuery.Where(x => x.IsDeleted),
                "all" => requestQuery,
                _ => requestQuery.Where(x => !x.IsDeleted)
            };
            if (model.ArchiveFilter is not ("active" or "archived" or "all"))
                model.ArchiveFilter = "active";
        }

        IQueryable<OutsourcedServiceRequestListItem> rowsQuery = requestQuery.Select(request => new OutsourcedServiceRequestListItem
        {
            Request = request,
            WorkflowStatus = db.WorkflowInstances.AsNoTracking()
                .Where(x => x.EntityType == "OutsourcedServiceRequest" && x.EntityId == request.Id.ToString())
                .OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id)
                .Select(x => x.Status).FirstOrDefault() ?? request.Status,
            CurrentStepTitle = db.WorkflowInstances.AsNoTracking()
                .Where(x => x.EntityType == "OutsourcedServiceRequest" && x.EntityId == request.Id.ToString())
                .OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id)
                .Select(x => x.CurrentStep == null ? "" : x.CurrentStep.Title).FirstOrDefault() ?? ""
        });

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            var term = model.Search;
            rowsQuery = rowsQuery.Where(x =>
                x.Request.RequestNumber.Contains(term) ||
                x.Request.ServiceDescription.Contains(term) ||
                x.Request.UnitOfMeasure.Contains(term) ||
                db.Employees.AsNoTracking().Any(e =>
                    e.Id == x.Request.RequesterEmployeeId &&
                    (e.FirstName.Contains(term) || e.LastName.Contains(term) ||
                     e.PersonnelNumber.Contains(term))));
        }

        if (model.Priority is "Normal" or "Urgent" or "Emergency")
            rowsQuery = rowsQuery.Where(x => x.Request.Priority == model.Priority);
        else
            model.Priority = "all";

        var allowedStatuses = new[] { "Submitted", "InProgress", "Completed", "Approved", "Rejected", "Returned", "Cancelled" };
        if (allowedStatuses.Contains(model.Status, StringComparer.Ordinal))
            rowsQuery = rowsQuery.Where(x => x.WorkflowStatus == model.Status);
        else
            model.Status = "all";

        DateTime? fromDate = null;
        DateTime? toDate = null;
        if (!string.IsNullOrWhiteSpace(model.FromJalali))
        {
            if (PersianDateService.TryParse(model.FromJalali, out var parsed))
                fromDate = parsed.Date;
            else
                ModelState.AddModelError(nameof(model.FromJalali), "تاریخ شروع شمسی معتبر نیست.");
        }
        if (!string.IsNullOrWhiteSpace(model.ToJalali))
        {
            if (PersianDateService.TryParse(model.ToJalali, out var parsed))
                toDate = parsed.Date;
            else
                ModelState.AddModelError(nameof(model.ToJalali), "تاریخ پایان شمسی معتبر نیست.");
        }
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            ModelState.AddModelError(nameof(model.ToJalali), "تاریخ پایان نباید پیش از تاریخ شروع باشد.");
        if (fromDate.HasValue)
            rowsQuery = rowsQuery.Where(x => x.Request.NeedByDate >= fromDate.Value);
        if (toDate.HasValue)
            rowsQuery = rowsQuery.Where(x => x.Request.NeedByDate <= toDate.Value);

        model.TotalCount = await rowsQuery.CountAsync();
        model.TotalPages = Math.Max(1, (int)Math.Ceiling(model.TotalCount / (double)model.PageSize));
        model.Page = Math.Min(model.Page, model.TotalPages);

        var descending = model.SortDirection == "desc";
        model.SortBy = model.SortBy is "RequestNumber" or "ServiceDescription" or "Quantity" or
            "NeedByDate" or "Priority" or "WorkflowStatus" or "CreatedAtUtc"
            ? model.SortBy : "CreatedAtUtc";

        IOrderedQueryable<OutsourcedServiceRequestListItem> ordered = model.SortBy switch
        {
            "RequestNumber" => descending ? rowsQuery.OrderByDescending(x => x.Request.RequestNumber) : rowsQuery.OrderBy(x => x.Request.RequestNumber),
            "ServiceDescription" => descending ? rowsQuery.OrderByDescending(x => x.Request.ServiceDescription) : rowsQuery.OrderBy(x => x.Request.ServiceDescription),
            "Quantity" => descending ? rowsQuery.OrderByDescending(x => x.Request.Quantity) : rowsQuery.OrderBy(x => x.Request.Quantity),
            "NeedByDate" => descending ? rowsQuery.OrderByDescending(x => x.Request.NeedByDate) : rowsQuery.OrderBy(x => x.Request.NeedByDate),
            "Priority" => descending ? rowsQuery.OrderByDescending(x => x.Request.Priority) : rowsQuery.OrderBy(x => x.Request.Priority),
            "WorkflowStatus" => descending ? rowsQuery.OrderByDescending(x => x.WorkflowStatus) : rowsQuery.OrderBy(x => x.WorkflowStatus),
            _ => descending ? rowsQuery.OrderByDescending(x => x.Request.CreatedAtUtc) : rowsQuery.OrderBy(x => x.Request.CreatedAtUtc)
        };
        model.Rows = await ordered.ThenByDescending(x => x.Request.Id)
            .Skip((model.Page - 1) * model.PageSize)
            .Take(model.PageSize)
            .ToListAsync();

        ViewBag.CanCreate = Has("OutsourcedServices.Create");
        ViewBag.CanViewAll = Has("OutsourcedServices.ViewAll");
        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        if (!Has("OutsourcedServices.Create")) return Forbid();
        PrepareFormView(isEdit: false);
        return View(new OutsourcedServiceRequestFormVm
        {
            NeedByDateJalali = PersianDateService.Format(PersianDateService.ToTehran(DateTime.UtcNow))
        });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(28_000_000)]
    public async Task<IActionResult> Create(OutsourcedServiceRequestFormVm model, List<IFormFile>? files)
    {
        if (!Has("OutsourcedServices.Create")) return Forbid();
        PrepareFormView(isEdit: false);

        var hasRequester = TryEmployeeId(out var requesterId) &&
            await db.Employees.AnyAsync(x => x.Id == requesterId && x.Status == "فعال" && x.IsSystemUser);
        if (!hasRequester)
            ModelState.AddModelError("", "اطلاعات کاربر درخواست‌کننده معتبر نیست.");

        if (!ValidateForm(model, out var needByDate))
        {
            ViewBag.ExistingAttachments = new List<OutsourcedServiceRequestAttachment>();
            return View("Create", model);
        }
        ValidateAttachments(files, existingCount: 0, existingBytes: 0);
        if (!ModelState.IsValid)
        {
            ViewBag.ExistingAttachments = new List<OutsourcedServiceRequestAttachment>();
            return View("Create", model);
        }

        var item = new OutsourcedServiceRequest
        {
            RequestNumber = "TEMP-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
            ServiceDescription = model.ServiceDescription.Trim(),
            UnitOfMeasure = model.UnitOfMeasure,
            Quantity = model.Quantity,
            NeedByDate = needByDate.Date,
            Priority = model.Priority,
            Status = "Submitted",
            RequesterEmployeeId = requesterId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            try
            {
                db.OutsourcedServiceRequests.Add(item);
                await db.SaveChangesAsync();
                var jalaliYear = PersianDateService.Format(PersianDateService.ToTehran(DateTime.UtcNow))[..4];
                item.RequestNumber = $"OS-{jalaliYear}-{item.Id:000000}";
                await SaveAttachmentsAsync(item, files);
                db.OutsourcedServiceRequestRevisions.Add(new OutsourcedServiceRequestRevision
                {
                    RequestId = item.Id,
                    Action = "Created",
                    AfterJson = SnapshotJson(item),
                    ActorEmployeeId = requesterId,
                    ActorUserName = CurrentActorName()
                });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (IOException)
            {
                await transaction.RollbackAsync();
                TryDeleteAttachmentFolder(item.Id);
                ModelState.AddModelError("Attachments", "ذخیره یکی از فایل‌های پیوست با خطا مواجه شد. لطفاً دوباره تلاش کنید.");
                ViewBag.ExistingAttachments = new List<OutsourcedServiceRequestAttachment>();
                return View("Create", model);
            }
            catch (UnauthorizedAccessException)
            {
                await transaction.RollbackAsync();
                TryDeleteAttachmentFolder(item.Id);
                ModelState.AddModelError("Attachments", "سامانه مجوز ذخیره فایل پیوست را ندارد. با مدیر سامانه تماس بگیرید.");
                ViewBag.ExistingAttachments = new List<OutsourcedServiceRequestAttachment>();
                return View("Create", model);
            }
        }
        await audit.WriteAsync("ثبت سفارش خدمات برونسپاری", nameof(OutsourcedServiceRequest),
            item.Id.ToString(), $"ثبت درخواست {item.RequestNumber}", requesterId);

        try
        {
            var instance = await workflowSubmission.StartForFormAsync(
                "OutsourcedServices", "OutsourcedServiceRequest", item.Id.ToString(), requesterId,
                subjectEmployeeId: requesterId);
            if (instance is null)
                TempData["Warning"] = "درخواست ثبت شد؛ برای آغاز ارجاع، ابتدا گردش کار «سفارش خدمات برونسپاری» را در مدیریت گردش کار تعریف و فعال کنید.";
            else
                TempData["Success"] = "درخواست با موفقیت ثبت و برای طی مراحل تأیید به گردش کار ارسال شد.";
        }
        catch (Exception ex)
        {
            TempData["Warning"] = "درخواست ثبت شد، اما شروع گردش کار با مشکل مواجه شد: " + ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = item.Id });
    }


    [HttpGet]
    public async Task<IActionResult> Edit(int id, int? returnTaskId)
    {
        var item = await db.OutsourcedServiceRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (item is null) return NotFound();
        if (returnTaskId.HasValue)
        {
            if (!TryEmployeeId(out var editorId) ||
                !await IsValidReturnedWorkflowTaskAsync(item.Id, returnTaskId.Value, editorId))
                return Forbid();
        }
        else if (!await CanManageRequestAsync(item))
        {
            return Forbid();
        }

        var existingAttachments = await db.OutsourcedServiceRequestAttachments.AsNoTracking()
            .Where(x => x.RequestId == item.Id)
            .OrderByDescending(x => x.UploadedAtUtc).ToListAsync();
        PrepareFormView(isEdit: true, returnTaskId, existingAttachments);
        return View("Create", new OutsourcedServiceRequestFormVm
        {
            ServiceDescription = item.ServiceDescription,
            UnitOfMeasure = item.UnitOfMeasure,
            Quantity = item.Quantity,
            NeedByDateJalali = PersianDateService.Format(item.NeedByDate),
            Priority = item.Priority,
            ExistingAttachments = existingAttachments
        });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(28_000_000)]
    public async Task<IActionResult> Edit(int id, OutsourcedServiceRequestFormVm model, int? returnTaskId, List<IFormFile>? files)
    {
        PrepareFormView(isEdit: true, returnTaskId);

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var item = await db.OutsourcedServiceRequests
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (item is null) return NotFound();
        if (returnTaskId.HasValue)
        {
            if (!TryEmployeeId(out var editorId) ||
                !await IsValidReturnedWorkflowTaskAsync(item.Id, returnTaskId.Value, editorId))
                return Forbid();
        }
        else if (!await CanManageRequestAsync(item))
        {
            return Forbid();
        }

        var existingAttachments = await db.OutsourcedServiceRequestAttachments
            .Where(x => x.RequestId == item.Id).OrderByDescending(x => x.UploadedAtUtc).ToListAsync();
        PrepareFormView(isEdit: true, returnTaskId, existingAttachments);
        if (!ValidateForm(model, out var needByDate))
        {
            ViewBag.ExistingAttachments = existingAttachments;
            return View("Create", model);
        }
        ValidateAttachments(files, existingAttachments.Count, existingAttachments.Sum(x => x.FileSize));
        if (!ModelState.IsValid)
        {
            ViewBag.ExistingAttachments = existingAttachments;
            return View("Create", model);
        }

        var before = SnapshotJson(item);
        item.ServiceDescription = model.ServiceDescription.Trim();
        item.UnitOfMeasure = model.UnitOfMeasure;
        item.Quantity = model.Quantity;
        item.NeedByDate = needByDate.Date;
        item.Priority = model.Priority;
        item.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await SaveAttachmentsAsync(item, files);
            db.OutsourcedServiceRequestRevisions.Add(new OutsourcedServiceRequestRevision
            {
                RequestId = item.Id,
                Action = "Updated",
                BeforeJson = before,
                AfterJson = SnapshotJson(item),
                ActorEmployeeId = TryEmployeeId(out var revisionEditorId) ? revisionEditorId : null,
                ActorUserName = CurrentActorName()
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (IOException)
        {
            await transaction.RollbackAsync();
            TryDeleteAttachmentFolder(item.Id, deleteAll: false, retainedFileNames: existingAttachments.Select(x => x.StoredFileName));
            ModelState.AddModelError("Attachments", "ذخیره یکی از فایل‌های پیوست با خطا مواجه شد. لطفاً دوباره تلاش کنید.");
            ViewBag.ExistingAttachments = existingAttachments;
            return View("Create", model);
        }
        catch (UnauthorizedAccessException)
        {
            await transaction.RollbackAsync();
            TryDeleteAttachmentFolder(item.Id, deleteAll: false);
            ModelState.AddModelError("Attachments", "سامانه مجوز ذخیره فایل پیوست را ندارد. با مدیر سامانه تماس بگیرید.");
            ViewBag.ExistingAttachments = existingAttachments;
            return View("Create", model);
        }

        await audit.WriteAsync("ویرایش سفارش خدمات برونسپاری", nameof(OutsourcedServiceRequest),
            item.Id.ToString(), $"ویرایش درخواست {item.RequestNumber}",
            TryEmployeeId(out var actorId) ? actorId : null);
        TempData["Success"] = "اطلاعات درخواست با حفظ نسخه قبلی با موفقیت ویرایش شد.";
        return returnTaskId.HasValue
            ? RedirectToAction("Task", "Workflow", new { id = returnTaskId.Value })
            : RedirectToAction(nameof(Details), new { id = item.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!IsAdmin && !Has("OutsourcedServices.Create")) return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var item = await db.OutsourcedServiceRequests
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (item is null) return NotFound();
        if (!await CanManageRequestAsync(item)) return Forbid();

        var now = DateTime.UtcNow;
        var before = SnapshotJson(item);
        var actorId = TryEmployeeId(out var currentEmployeeId) ? currentEmployeeId : (int?)null;
        item.IsDeleted = true;
        item.DeletedAtUtc = now;
        item.DeletedByEmployeeId = actorId;
        item.UpdatedAtUtc = now;

        var instance = await db.WorkflowInstances
            .Where(x => x.EntityType == "OutsourcedServiceRequest" && x.EntityId == item.Id.ToString())
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync();
        if (instance is not null)
        {
            var previousStatus = instance.Status;
            var previousStepId = instance.CurrentStepId;
            if (instance.Status == "InProgress")
            {
                var pendingTasks = await db.WorkflowTasks
                    .Where(x => x.WorkflowInstanceId == instance.Id && x.Status == "Pending")
                    .ToListAsync();
                foreach (var task in pendingTasks)
                {
                    task.Status = "Cancelled";
                    task.Action = "RequestArchived";
                    task.CompletedAt = now;
                }
                instance.Status = "Cancelled";
                instance.CurrentStepId = null;
                instance.CompletedAt = now;
            }

            db.WorkflowHistory.Add(new WorkflowHistory
            {
                WorkflowInstanceId = instance.Id,
                WorkflowStepId = previousStepId,
                FromStatus = previousStatus,
                ToStatus = instance.Status,
                Action = "RequestArchived",
                ActorEmployeeId = actorId,
                Comment = "درخواست توسط کاربر مجاز بایگانی نرم شد؛ سوابق گردش کار و مقادیر فرم حفظ شده‌اند."
            });
        }

        db.OutsourcedServiceRequestRevisions.Add(new OutsourcedServiceRequestRevision
        {
            RequestId = item.Id,
            Action = "Deleted",
            BeforeJson = before,
            AfterJson = SnapshotJson(item),
            ActorEmployeeId = actorId,
            ActorUserName = CurrentActorName()
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        await audit.WriteAsync("حذف نرم سفارش خدمات برونسپاری", nameof(OutsourcedServiceRequest),
            item.Id.ToString(), $"بایگانی درخواست {item.RequestNumber} با حفظ سوابق گردش کار", actorId);
        TempData["Success"] = "درخواست بایگانی شد؛ سوابق درخواست و گردش کار حذف نشده‌اند.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(int id)
    {
        var attachment = await db.OutsourcedServiceRequestAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (attachment is null) return NotFound();

        var request = await db.OutsourcedServiceRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachment.RequestId);
        if (request is null || request.IsDeleted && !IsAdmin)
            return NotFound();

        var canDownload = await CanViewRequestAsync(request);
        if (!canDownload && TryEmployeeId(out var employeeId))
        {
            var workflowInstanceId = await db.WorkflowInstances.AsNoTracking()
                .Where(x => x.EntityType == "OutsourcedServiceRequest" && x.EntityId == request.Id.ToString())
                .OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync();
            if (workflowInstanceId.HasValue)
                canDownload = await workflow.GetInstanceAsync(
                    workflowInstanceId.Value, employeeId, allowUpcomingPreview: true) is not null;
        }
        if (!canDownload) return NotFound();

        var path = Path.Combine(GetAttachmentFolder(request.Id), attachment.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        var downloadName = Path.GetFileName(attachment.OriginalFileName);
        return PhysicalFile(path, "application/octet-stream", downloadName, enableRangeProcessing: true);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!Has("OutsourcedServices.Create") && !Has("OutsourcedServices.ViewAll") && !Has("OutsourcedServices.Reports")) return Forbid();
        var query = db.OutsourcedServiceRequests.AsNoTracking()
            .Where(x => !x.IsDeleted || IsAdmin).AsQueryable();
        if (!Has("OutsourcedServices.ViewAll") && !Has("OutsourcedServices.Reports"))
        {
            if (!TryEmployeeId(out var employeeId)) return Forbid();
            query = query.Where(x => x.RequesterEmployeeId == employeeId);
        }

        var item = await query.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return NotFound();
        var requester = await db.Employees.AsNoTracking()
            .Where(x => x.Id == item.RequesterEmployeeId)
            .Select(x => new { Name = x.FirstName + " " + x.LastName, x.PersonnelNumber })
            .FirstOrDefaultAsync();
        var workflow = await db.WorkflowInstances.AsNoTracking()
            .Where(x => x.EntityType == "OutsourcedServiceRequest" && x.EntityId == id.ToString())
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new { x.Status, StepTitle = x.CurrentStep == null ? "" : x.CurrentStep.Title, x.StartedAt })
            .FirstOrDefaultAsync();
        ViewBag.Requester = requester;
        ViewBag.Workflow = workflow;
        ViewBag.PriorityTitle = Priorities.GetValueOrDefault(item.Priority, item.Priority);
        ViewBag.IsAdmin = IsAdmin;
        ViewBag.CanEdit = !item.IsDeleted && await CanManageRequestAsync(item);
        ViewBag.CanDelete = !item.IsDeleted && await CanManageRequestAsync(item);
        ViewBag.Revisions = await db.OutsourcedServiceRequestRevisions.AsNoTracking()
            .Where(x => x.RequestId == item.Id)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync();
        ViewBag.Attachments = await db.OutsourcedServiceRequestAttachments.AsNoTracking()
            .Where(x => x.RequestId == item.Id)
            .OrderByDescending(x => x.UploadedAtUtc).ToListAsync();
        return View(item);
    }

    private void PrepareFormView(
        bool isEdit,
        int? returnTaskId = null,
        List<OutsourcedServiceRequestAttachment>? existingAttachments = null)
    {
        ViewBag.Units = Units;
        ViewBag.Priorities = Priorities;
        ViewBag.IsEdit = isEdit;
        ViewBag.ReturnTaskId = returnTaskId;
        ViewBag.ExistingAttachments = existingAttachments ?? new List<OutsourcedServiceRequestAttachment>();
    }

    private const long MaxAttachmentBytes = 10 * 1024 * 1024;
    private const long MaxTotalAttachmentBytes = 25 * 1024 * 1024;
    private const int MaxAttachmentCount = 10;
    private static readonly HashSet<string> AllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".png", ".jpg", ".jpeg", ".gif", ".txt", ".csv"
    };

    private void ValidateAttachments(List<IFormFile>? files, int existingCount, long existingBytes)
    {
        var uploads = files?.Where(x => x is not null && x.Length > 0).ToList() ?? [];
        if (existingCount + uploads.Count > MaxAttachmentCount)
            ModelState.AddModelError("Attachments", $"حداکثر {MaxAttachmentCount} فایل برای هر سفارش قابل نگهداری است.");

        long totalBytes = existingBytes;
        foreach (var file in uploads)
        {
            var extension = Path.GetExtension(file.FileName);
            if (!AllowedAttachmentExtensions.Contains(extension))
                ModelState.AddModelError("Attachments", $"قالب فایل «{Path.GetFileName(file.FileName)}» مجاز نیست.");
            if (file.Length > MaxAttachmentBytes)
                ModelState.AddModelError("Attachments", $"حجم هر فایل نباید بیشتر از ۱۰ مگابایت باشد.");
            totalBytes += file.Length;
        }

        if (totalBytes > MaxTotalAttachmentBytes)
            ModelState.AddModelError("Attachments", "مجموع حجم پیوست‌های هر سفارش نباید بیشتر از ۵۰ مگابایت باشد.");
    }

    private async Task SaveAttachmentsAsync(OutsourcedServiceRequest item, List<IFormFile>? files)
    {
        var uploads = files?.Where(x => x is not null && x.Length > 0).ToList() ?? [];
        if (uploads.Count == 0) return;

        var folder = GetAttachmentFolder(item.Id);
        Directory.CreateDirectory(folder);
        foreach (var file in uploads)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var storedName = Guid.NewGuid().ToString("N") + extension;
            var path = Path.Combine(folder, storedName);
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await file.CopyToAsync(stream);

            var originalName = Path.GetFileName(file.FileName.Replace('\\', '/'));
            originalName = new string(originalName.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(originalName)) originalName = "attachment" + extension;
            if (originalName.Length > 255) originalName = originalName[^255..];

            db.OutsourcedServiceRequestAttachments.Add(new OutsourcedServiceRequestAttachment
            {
                RequestId = item.Id,
                OriginalFileName = originalName,
                StoredFileName = storedName,
                ContentType = ContentTypeForExtension(extension),
                FileSize = file.Length,
                UploadedAtUtc = DateTime.UtcNow
            });
        }
    }

    private static string ContentTypeForExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        _ => "application/octet-stream"
    };

    private string GetAttachmentFolder(int requestId) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", "OutsourcedServices", requestId.ToString());

    private void TryDeleteAttachmentFolder(
        int requestId,
        bool deleteAll = true,
        IEnumerable<string>? retainedFileNames = null)
    {
        if (requestId <= 0) return;
        var folder = GetAttachmentFolder(requestId);
        try
        {
            if (!Directory.Exists(folder)) return;
            if (deleteAll)
            {
                Directory.Delete(folder, recursive: true);
                return;
            }

            var retainedNames = (retainedFileNames ?? Enumerable.Empty<string>())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(folder))
                if (!retainedNames.Contains(Path.GetFileName(path)))
                    System.IO.File.Delete(path);
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch
        {
            // Orphan cleanup is non-blocking; stored metadata remains the source of truth.
        }
    }

    private async Task<bool> CanViewRequestAsync(OutsourcedServiceRequest item)
    {
        if (item.IsDeleted && !IsAdmin) return false;
        if (!Has("OutsourcedServices.Create") && !Has("OutsourcedServices.ViewAll") &&
            !Has("OutsourcedServices.Reports")) return false;
        if (Has("OutsourcedServices.ViewAll") || Has("OutsourcedServices.Reports")) return true;
        return TryEmployeeId(out var employeeId) && employeeId == item.RequesterEmployeeId;
    }

    private bool ValidateForm(OutsourcedServiceRequestFormVm model, out DateTime needByDate)
    {
        needByDate = default;
        if (string.IsNullOrWhiteSpace(model.ServiceDescription) ||
            model.ServiceDescription.Trim().Length > 3000)
            ModelState.AddModelError(nameof(model.ServiceDescription), "شرح خدمات الزامی و حداکثر ۳۰۰۰ نویسه است.");
        if (!Units.Contains(model.UnitOfMeasure))
            ModelState.AddModelError(nameof(model.UnitOfMeasure), "واحد سنجش را از فهرست انتخاب کنید.");
        if (model.Quantity <= 0 || model.Quantity > 999999999 ||
            decimal.Round(model.Quantity, 3) != model.Quantity)
            ModelState.AddModelError(nameof(model.Quantity), "مقدار باید بزرگ‌تر از صفر و حداکثر دارای سه رقم اعشار باشد.");
        if (string.IsNullOrWhiteSpace(model.Priority) || !Priorities.ContainsKey(model.Priority))
            ModelState.AddModelError(nameof(model.Priority), "اولویت انجام کار معتبر نیست.");
        if (!PersianDateService.TryParse(model.NeedByDateJalali, out needByDate))
            ModelState.AddModelError(nameof(model.NeedByDateJalali), "تاریخ نیازمندی را با تقویم شمسی و به‌صورت معتبر وارد کنید.");
        return ModelState.IsValid;
    }

    private async Task<bool> CanManageRequestAsync(OutsourcedServiceRequest item)
    {
        if (item.IsDeleted) return false;
        if (IsAdmin) return true;
        if (!TryEmployeeId(out var employeeId) || employeeId != item.RequesterEmployeeId)
            return false;

        var instance = await db.WorkflowInstances.AsNoTracking()
            .Where(x => x.EntityType == "OutsourcedServiceRequest" && x.EntityId == item.Id.ToString())
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync();
        if (instance is null) return Has("OutsourcedServices.Create");
        if (instance.Status != "InProgress" || !instance.CurrentStepId.HasValue) return false;

        // A pending Requester-assigned stage is an explicit authorization to edit
        // this returned request even after other workflow actors have acted.
        var hasRequesterCorrectionTask = await db.WorkflowTasks.AsNoTracking().AnyAsync(task =>
            task.WorkflowInstanceId == instance.Id &&
            task.WorkflowStepId == instance.CurrentStepId.Value &&
            task.Status == "Pending" &&
            task.AssignedEmployeeId == employeeId &&
            (task.Action == "ReturnedForEdit" || task.WorkflowStep.AssignmentType == "Requester"));
        if (hasRequesterCorrectionTask) return true;

        if (!Has("OutsourcedServices.Create")) return false;
        var firstStepId = await db.WorkflowSteps.AsNoTracking()
            .Where(x => x.WorkflowDefinitionId == instance.WorkflowDefinitionId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync();
        if (!firstStepId.HasValue || instance.CurrentStepId.Value != firstStepId.Value) return false;

        var otherActorHasActed = await db.WorkflowHistory.AsNoTracking().AnyAsync(x =>
            x.WorkflowInstanceId == instance.Id &&
            x.ActorEmployeeId.HasValue && x.ActorEmployeeId.Value != item.RequesterEmployeeId &&
            x.Action != "Created" && x.Action != "Submitted" && x.Action != "Submit");
        return !otherActorHasActed;
    }

    private async Task<bool> IsValidReturnedWorkflowTaskAsync(int requestId, int taskId, int employeeId)
    {
        // GetTaskAsync validates the current assignee (including position membership)
        // and returns only a pending task that belongs to this employee.
        var task = await workflow.GetTaskAsync(taskId, employeeId);
        return task is not null &&
            (task.Action == "ReturnedForEdit" ||
             (task.WorkflowStep.AssignmentType == "Requester" &&
              task.WorkflowInstance.RequesterEmployeeId == employeeId)) &&
            task.WorkflowInstance.EntityType == "OutsourcedServiceRequest" &&
            task.WorkflowInstance.EntityId == requestId.ToString() &&
            task.WorkflowInstance.Status == "InProgress" &&
            task.WorkflowInstance.CurrentStepId == task.WorkflowStepId;
    }

    private static string SnapshotJson(OutsourcedServiceRequest item) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            item.RequestNumber,
            item.ServiceDescription,
            item.UnitOfMeasure,
            item.Quantity,
            NeedByDate = item.NeedByDate.ToString("yyyy-MM-dd"),
            item.Priority,
            item.Status,
            item.RequesterEmployeeId,
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            item.IsDeleted,
            item.DeletedAtUtc,
            item.DeletedByEmployeeId
        });

    private string? CurrentActorName() =>
        User.FindFirst("AdminUsername")?.Value ?? User.Identity?.Name;
}