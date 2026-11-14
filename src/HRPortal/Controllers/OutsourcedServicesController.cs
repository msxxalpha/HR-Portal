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
    WorkflowFormSubmissionService workflowSubmission) : Controller
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
        var query = db.OutsourcedServiceRequests.AsNoTracking();
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
        ViewBag.Units = Units;
        ViewBag.Priorities = Priorities;
        return View(new OutsourcedServiceRequestFormVm
        {
            NeedByDateJalali = PersianDateService.Format(PersianDateService.ToTehran(DateTime.UtcNow))
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OutsourcedServiceRequestFormVm model)
    {
        if (!Has("OutsourcedServices.Create")) return Forbid();
        ViewBag.Units = Units;
        ViewBag.Priorities = Priorities;

        var hasRequester = TryEmployeeId(out var requesterId) &&
            await db.Employees.AnyAsync(x => x.Id == requesterId && x.Status == "فعال" && x.IsSystemUser);
        if (!hasRequester)
            ModelState.AddModelError("", "اطلاعات کاربر درخواست‌کننده معتبر نیست.");

        if (string.IsNullOrWhiteSpace(model.ServiceDescription) || model.ServiceDescription.Trim().Length > 3000)
            ModelState.AddModelError(nameof(model.ServiceDescription), "شرح خدمات الزامی و حداکثر ۳۰۰۰ نویسه است.");
        if (!Units.Contains(model.UnitOfMeasure))
            ModelState.AddModelError(nameof(model.UnitOfMeasure), "واحد سنجش را از فهرست انتخاب کنید.");
        if (model.Quantity <= 0 || model.Quantity > 999999999 || decimal.Round(model.Quantity, 3) != model.Quantity)
            ModelState.AddModelError(nameof(model.Quantity), "مقدار باید بزرگ‌تر از صفر و حداکثر دارای سه رقم اعشار باشد.");
        if (string.IsNullOrWhiteSpace(model.Priority) || !Priorities.ContainsKey(model.Priority))
            ModelState.AddModelError(nameof(model.Priority), "اولویت انجام کار معتبر نیست.");
        if (!PersianDateService.TryParse(model.NeedByDateJalali, out var needByDate))
            ModelState.AddModelError(nameof(model.NeedByDateJalali), "تاریخ نیازمندی را با تقویم شمسی و به‌صورت معتبر وارد کنید.");

        if (!ModelState.IsValid) return View(model);

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
    public async Task<IActionResult> Details(int id)
    {
        if (!Has("OutsourcedServices.Create") && !Has("OutsourcedServices.ViewAll") && !Has("OutsourcedServices.Reports")) return Forbid();
        var query = db.OutsourcedServiceRequests.AsNoTracking().AsQueryable();
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
        return View(item);
    }
}