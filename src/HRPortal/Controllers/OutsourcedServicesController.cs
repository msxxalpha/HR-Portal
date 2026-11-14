using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public sealed class OutsourcedServicesController(
    HRPortalDbContext db,
    WorkflowFormSubmissionService workflowSubmission,
    WorkflowService workflow,
    AuditService audit) : Controller
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
    public async Task<IActionResult> Index()
    {
        if (!Has("OutsourcedServices.Create") && !Has("OutsourcedServices.ViewAll")) return Forbid();
        var query = db.OutsourcedServiceRequests.AsNoTracking()
            .Where(x => !x.IsDeleted || IsAdmin);
        if (!Has("OutsourcedServices.ViewAll"))
        {
            if (!TryEmployeeId(out var employeeId)) return Forbid();
            query = query.Where(x => x.RequesterEmployeeId == employeeId);
        }

        var requests = await query.OrderByDescending(x => x.CreatedAtUtc).Take(300).ToListAsync();
        var ids = requests.Select(x => x.Id.ToString()).ToArray();
        var workflows = await db.WorkflowInstances.AsNoTracking()
            .Where(x => x.EntityType == "OutsourcedServiceRequest" && ids.Contains(x.EntityId))
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new { x.EntityId, x.Status, StepTitle = x.CurrentStep == null ? "" : x.CurrentStep.Title })
            .ToListAsync();
        var latest = workflows.GroupBy(x => x.EntityId).ToDictionary(x => x.Key, x => x.First());
        var rows = requests.Select(request =>
        {
            latest.TryGetValue(request.Id.ToString(), out var workflow);
            return new OutsourcedServiceRequestListItem
            {
                Request = request,
                WorkflowStatus = workflow?.Status ?? request.Status,
                CurrentStepTitle = workflow?.StepTitle ?? ""
            };
        }).ToList();
        ViewBag.CanCreate = Has("OutsourcedServices.Create");
        return View(rows);
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OutsourcedServiceRequestFormVm model)
    {
        if (!Has("OutsourcedServices.Create")) return Forbid();
        PrepareFormView(isEdit: false);

        var hasRequester = TryEmployeeId(out var requesterId) &&
            await db.Employees.AnyAsync(x => x.Id == requesterId && x.Status == "فعال" && x.IsSystemUser);
        if (!hasRequester)
            ModelState.AddModelError("", "اطلاعات کاربر درخواست‌کننده معتبر نیست.");

        if (!ValidateForm(model, out var needByDate) || !ModelState.IsValid)
            return View("Create", model);

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
        db.OutsourcedServiceRequests.Add(item);
        await db.SaveChangesAsync();
        var jalaliYear = PersianDateService.Format(PersianDateService.ToTehran(DateTime.UtcNow))[..4];
        item.RequestNumber = $"OS-{jalaliYear}-{item.Id:000000}";
        await db.SaveChangesAsync();
        db.OutsourcedServiceRequestRevisions.Add(new OutsourcedServiceRequestRevision
        {
            RequestId = item.Id,
            Action = "Created",
            AfterJson = SnapshotJson(item),
            ActorEmployeeId = requesterId,
            ActorUserName = CurrentActorName()
        });
        await db.SaveChangesAsync();
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

        PrepareFormView(isEdit: true, returnTaskId);
        return View("Create", new OutsourcedServiceRequestFormVm
        {
            ServiceDescription = item.ServiceDescription,
            UnitOfMeasure = item.UnitOfMeasure,
            Quantity = item.Quantity,
            NeedByDateJalali = PersianDateService.Format(item.NeedByDate),
            Priority = item.Priority
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, OutsourcedServiceRequestFormVm model, int? returnTaskId)
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

        if (!ValidateForm(model, out var needByDate) || !ModelState.IsValid)
            return View("Create", model);

        var before = SnapshotJson(item);
        item.ServiceDescription = model.ServiceDescription.Trim();
        item.UnitOfMeasure = model.UnitOfMeasure;
        item.Quantity = model.Quantity;
        item.NeedByDate = needByDate.Date;
        item.Priority = model.Priority;
        item.UpdatedAtUtc = DateTime.UtcNow;

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
        return View(item);
    }

    private void PrepareFormView(bool isEdit, int? returnTaskId = null)
    {
        ViewBag.Units = Units;
        ViewBag.Priorities = Priorities;
        ViewBag.IsEdit = isEdit;
        ViewBag.ReturnTaskId = returnTaskId;
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