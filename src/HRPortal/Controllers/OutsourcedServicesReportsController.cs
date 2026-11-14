using System.Globalization;
using System.Text;
using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "OutsourcedServices.Reports")]
public sealed class OutsourcedServicesReportsController(HRPortalDbContext db) : Controller
{
    private static readonly Dictionary<string, string> PriorityLabels = new()
    {
        ["Normal"] = "عادی", ["Urgent"] = "فوری", ["Emergency"] = "اضطراری"
    };

    [HttpGet]
    public async Task<IActionResult> Index(string? from, string? to, string? priority, string? status, string? unit)
    {
        ViewBag.Priorities = PriorityLabels;
        ViewBag.Units = await db.OutsourcedServiceRequests.AsNoTracking()
            .Select(x => x.UnitOfMeasure).Distinct().OrderBy(x => x).ToListAsync();
        DateTime? fromDate = null, toDate = null;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!PersianDateService.TryParse(from, out var parsed))
                ModelState.AddModelError(nameof(from), "تاریخ شروع معتبر نیست.");
            else fromDate = parsed;
        }
        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!PersianDateService.TryParse(to, out var parsed))
                ModelState.AddModelError(nameof(to), "تاریخ پایان معتبر نیست.");
            else toDate = parsed;
        }
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
            ModelState.AddModelError("", "تاریخ شروع نمی‌تواند بعد از تاریخ پایان باشد.");

        var model = new OutsourcedServiceDashboardViewModel
        {
            FromJalali = from ?? "", ToJalali = to ?? "",
            Priority = string.IsNullOrWhiteSpace(priority) ? "all" : priority,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status,
            UnitOfMeasure = string.IsNullOrWhiteSpace(unit) ? "all" : unit
        };
        if (!ModelState.IsValid)
        {
            ViewBag.Error = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            model.Rows = [];
            return View(model);
        }

        var query = db.OutsourcedServiceRequests.AsNoTracking();
        if (fromDate.HasValue)
        {
            var startUtc = PersianDateService.ToUtcFromTehran(fromDate.Value.Date);
            query = query.Where(x => x.CreatedAtUtc >= startUtc);
        }
        if (toDate.HasValue)
        {
            var endUtc = PersianDateService.ToUtcFromTehran(toDate.Value.Date.AddDays(1));
            query = query.Where(x => x.CreatedAtUtc < endUtc);
        }
        if (model.Priority != "all" && PriorityLabels.ContainsKey(model.Priority))
            query = query.Where(x => x.Priority == model.Priority);
        if (model.UnitOfMeasure != "all")
            query = query.Where(x => x.UnitOfMeasure == model.UnitOfMeasure);

        var requests = await query.OrderByDescending(x => x.CreatedAtUtc).Take(5000).ToListAsync();
        var employeeIds = requests.Select(x => x.RequesterEmployeeId).Distinct().ToArray();
        var employees = await db.Employees.AsNoTracking().Where(x => employeeIds.Contains(x.Id))
            .Select(e => new
            {
                e.Id,
                Name = e.FirstName + " " + e.LastName,
                e.PersonnelNumber,
                Organization = e.OrganizationSection != null ? e.OrganizationSection.Title :
                    e.OrganizationDepartment != null ? e.OrganizationDepartment.Title :
                    e.OrganizationUnit != null ? e.OrganizationUnit.Title : "—"
            }).ToDictionaryAsync(x => x.Id);
        var requestIds = requests.Select(x => x.Id.ToString()).ToArray();
        var workflowRows = await db.WorkflowInstances.AsNoTracking()
            .Where(w => w.EntityType == "OutsourcedServiceRequest" && requestIds.Contains(w.EntityId))
            .OrderByDescending(w => w.StartedAt)
            .Select(w => new { w.EntityId, w.Status, Step = w.CurrentStep == null ? "" : w.CurrentStep.Title })
            .ToListAsync();
        var workflows = workflowRows.GroupBy(x => x.EntityId)
            .ToDictionary(x => x.Key, x => x.First());

        var rows = requests.Select(request =>
        {
            employees.TryGetValue(request.RequesterEmployeeId, out var employee);
            workflows.TryGetValue(request.Id.ToString(), out var workflow);
            return new OutsourcedServiceReportRow
            {
                Id = request.Id,
                RequestNumber = request.RequestNumber,
                ServiceDescription = request.ServiceDescription,
                UnitOfMeasure = request.UnitOfMeasure,
                Quantity = request.Quantity,
                NeedByDate = request.NeedByDate,
                Priority = request.Priority,
                Status = workflow?.Status ?? request.Status,
                RequesterName = employee?.Name ?? "—",
                PersonnelNumber = employee?.PersonnelNumber ?? "",
                Organization = employee?.Organization ?? "—",
                CreatedAtUtc = request.CreatedAtUtc,
                CurrentStepTitle = workflow?.Step ?? ""
            };
        }).ToList();

        if (model.Status != "all")
            rows = rows.Where(x => NormalizeStatus(x.Status) == model.Status).ToList();

        model.Rows = rows;
        model.TotalCount = rows.Count;
        model.PendingCount = rows.Count(x => NormalizeStatus(x.Status) == "InProgress");
        model.CompletedCount = rows.Count(x => NormalizeStatus(x.Status) == "Completed");
        model.RejectedCount = rows.Count(x => NormalizeStatus(x.Status) == "Rejected");
        model.ReturnedCount = rows.Count(x => NormalizeStatus(x.Status) == "Returned");
        model.UrgentCount = rows.Count(x => x.Priority is "Urgent" or "Emergency");
        model.PriorityBuckets = rows.GroupBy(x => x.Priority)
            .Select(g => new OutsourcedServiceBucket { Label = PriorityLabels.GetValueOrDefault(g.Key, g.Key), Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();
        model.StatusBuckets = rows.GroupBy(x => NormalizeStatus(x.Status))
            .Select(g => new OutsourcedServiceBucket { Label = StatusLabel(g.Key), Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();
        model.UnitBuckets = rows.GroupBy(x => x.UnitOfMeasure)
            .Select(g => new OutsourcedServiceBucket { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();
        model.MonthlyBuckets = BuildMonthlyBuckets(rows);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? from, string? to, string? priority, string? status, string? unit)
    {
        var redirect = await Index(from, to, priority, status, unit);
        if (redirect is not ViewResult view || view.Model is not OutsourcedServiceDashboardViewModel model)
            return redirect;
        var csv = new StringBuilder();
        csv.AppendLine("شماره درخواست,شرح خدمات,واحد سنجش,مقدار,تاریخ نیازمندی,اولویت,وضعیت گردش کار,درخواست‌کننده,شماره پرسنلی,واحد سازمانی,تاریخ ثبت,مرحله جاری");
        foreach (var row in model.Rows)
            csv.AppendLine(string.Join(",", new[]
            {
                Csv(row.RequestNumber), Csv(row.ServiceDescription), Csv(row.UnitOfMeasure),
                row.Quantity.ToString(CultureInfo.InvariantCulture), Csv(PersianDateService.Format(row.NeedByDate)),
                Csv(PriorityLabels.GetValueOrDefault(row.Priority, row.Priority)), Csv(StatusLabel(NormalizeStatus(row.Status))),
                Csv(row.RequesterName), Csv(row.PersonnelNumber), Csv(row.Organization),
                Csv(PersianDateService.Format(PersianDateService.ToTehran(row.CreatedAtUtc))), Csv(row.CurrentStepTitle)
            }));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"OutsourcedServices-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    private static List<OutsourcedServiceBucket> BuildMonthlyBuckets(List<OutsourcedServiceReportRow> rows)
    {
        var calendar = new PersianCalendar();
        var now = PersianDateService.ToTehran(DateTime.UtcNow);
        var currentMonth = calendar.ToDateTime(calendar.GetYear(now), calendar.GetMonth(now), 1, 0, 0, 0, 0);
        var result = new List<OutsourcedServiceBucket>();
        for (var i = 11; i >= 0; i--)
        {
            var start = calendar.AddMonths(currentMonth, -i);
            var end = calendar.AddMonths(start, 1);
            var label = $"{calendar.GetYear(start):0000}/{calendar.GetMonth(start):00}";
            var count = rows.Count(x =>
            {
                var created = PersianDateService.ToTehran(x.CreatedAtUtc);
                return created >= start && created < end;
            });
            result.Add(new OutsourcedServiceBucket { Label = label, Count = count });
        }
        return result;
    }

    private static string NormalizeStatus(string? status) => status switch
    {
        "InProgress" or "Pending" or "New" => "InProgress",
        "Completed" or "Approved" or "Done" => "Completed",
        "Rejected" or "Cancelled" => "Rejected",
        "Returned" => "Returned",
        _ => "NotStarted"
    };

    private static string StatusLabel(string status) => status switch
    {
        "InProgress" => "در حال گردش",
        "Completed" => "تأیید نهایی",
        "Rejected" => "رد یا لغو شده",
        "Returned" => "برگشت داده‌شده",
        _ => "بدون گردش کار"
    };

    private static string Csv(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}