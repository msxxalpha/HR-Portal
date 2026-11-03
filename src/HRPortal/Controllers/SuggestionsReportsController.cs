using System.Globalization;
using System.Text;
using System.Text.Json;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "Suggestions.Reports")]
public class SuggestionsReportsController(HRPortalDbContext db) : Controller
{
    private static readonly string[] AcceptedStatuses = ["Accepted", "Approved", "تأیید شده", "تأیید نهایی", "پذیرفته شده", "مصوب"];
    private static readonly string[] RejectedStatuses = ["Rejected", "رد شده", "عدم پذیرش"];

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? category, string? status)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
        {
            TempData["Error"] = "تاریخ شروع نمی‌تواند بعد از تاریخ پایان باشد.";
            return RedirectToAction(nameof(Index), new { category, status });
        }

        var rows = await BuildRowsAsync(from, to, category, status);
        var nowUtc = DateTime.UtcNow;
        var accepted = rows.Where(x => AcceptedStatuses.Contains(x.Status, StringComparer.OrdinalIgnoreCase)).ToList();
        var rejected = rows.Where(x => RejectedStatuses.Contains(x.Status, StringComparer.OrdinalIgnoreCase)).ToList();
        var drafts = rows.Count(x => x.Status == "Draft");
        var submitted = rows.Count(x => x.Status == "Submitted");
        var inEvaluation = rows.Count - drafts - submitted - accepted.Count - rejected.Count;
        var persian = new PersianCalendar();
        var localNow = DateTime.Now;
        var currentJalaliMonthStart = persian.ToDateTime(
            persian.GetYear(localNow), persian.GetMonth(localNow), 1, 0, 0, 0, 0);
        var monthly = new List<SuggestionDashboardBucket>();
        for (var i = 11; i >= 0; i--)
        {
            var monthStart = persian.AddMonths(currentJalaliMonthStart, -i);
            var nextMonthStart = persian.AddMonths(monthStart, 1);
            var keyYear = persian.GetYear(monthStart);
            var keyMonth = persian.GetMonth(monthStart);
            var label = $"{keyYear}/{keyMonth:00}";
            var count = rows.Count(x =>
            {
                if (!x.SubmittedAtUtc.HasValue) return false;
                var date = x.SubmittedAtUtc.Value.ToLocalTime();
                return date >= monthStart && date < nextMonthStart;
            });
            monthly.Add(new SuggestionDashboardBucket { Label = label, Count = count });
        }

        var statusBuckets = rows.GroupBy(x => x.Status)
            .Select(g => new SuggestionDashboardBucket { Label = StatusLabel(g.Key), Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();
        var categoryBuckets = rows.GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "بدون دسته‌بندی" : x.Category)
            .Select(g => new SuggestionDashboardBucket { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();
        var benefitBuckets = rows.SelectMany(x => ParseList(x.BenefitsJson))
            .GroupBy(x => x).Select(g => new SuggestionDashboardBucket { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();
        var resourceBuckets = rows.SelectMany(x => ParseList(x.ResourceTypesJson))
            .GroupBy(x => x).Select(g => new SuggestionDashboardBucket { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();

        var model = new SuggestionsDashboardViewModel
        {
            From = from,
            To = to,
            Category = string.IsNullOrWhiteSpace(category) ? "all" : category,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status,
            TotalCount = rows.Count,
            DraftCount = drafts,
            SubmittedCount = submitted,
            InEvaluationCount = Math.Max(0, inEvaluation),
            AcceptedCount = accepted.Count,
            RejectedCount = rejected.Count,
            UniqueProposers = rows.Select(x => x.ProposerEmployeeId).Distinct().Count(),
            CollaborativeCount = rows.Count(x => x.MemberCount > 0),
            AttachmentCount = rows.Sum(x => x.AttachmentCount),
            SubmittedLast30Days = rows.Count(x => x.SubmittedAtUtc.HasValue && x.SubmittedAtUtc.Value >= nowUtc.AddDays(-30)),
            StatusBuckets = statusBuckets,
            CategoryBuckets = categoryBuckets,
            MonthlyBuckets = monthly,
            BenefitBuckets = benefitBuckets,
            ResourceBuckets = resourceBuckets,
            Rows = rows
        };
        return View(model);
    }

    [HttpGet, Authorize(Policy = "Suggestions.Export")]
    public async Task<IActionResult> Export(DateTime? from, DateTime? to, string? category, string? status)
    {
        var rows = await BuildRowsAsync(from, to, category, status);
        var csv = new StringBuilder();
        csv.AppendLine("شماره پیگیری,عنوان پیشنهاد,دسته‌بندی,وضعیت,پیشنهاددهنده,شماره پرسنلی,واحد,تاریخ ثبت,تاریخ ارسال,تعداد همکاران,تعداد پیوست‌ها");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                Csv(row.SuggestionNumber), Csv(row.Title), Csv(row.Category), Csv(StatusLabel(row.Status)),
                Csv(row.ProposerName), Csv(row.PersonnelNumber), Csv(row.Organization),
                Csv(row.CreatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm")),
                Csv(row.SubmittedAtUtc?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? ""),
                row.MemberCount.ToString(CultureInfo.InvariantCulture),
                row.AttachmentCount.ToString(CultureInfo.InvariantCulture)
            }));
        }
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"Suggestions-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    private async Task<List<SuggestionDashboardRow>> BuildRowsAsync(DateTime? from, DateTime? to, string? category, string? status)
    {
        IQueryable<Suggestion> query = db.Suggestions.AsNoTracking();
        if (from.HasValue)
        {
            var start = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAtUtc >= start);
        }
        if (to.HasValue)
        {
            var endExclusive = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAtUtc < endExclusive);
        }
        if (!string.IsNullOrWhiteSpace(category) && category != "all")
            query = query.Where(x => x.Category == category);

        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            if (status == "Draft") query = query.Where(x => x.Status == "Draft");
            else if (status == "Submitted") query = query.Where(x => x.Status == "Submitted");
            else if (status == "InEvaluation") query = query.Where(x => x.Status != "Draft" && x.Status != "Submitted" &&
                !AcceptedStatuses.Contains(x.Status) && !RejectedStatuses.Contains(x.Status));
            else if (status == "Accepted") query = query.Where(x => AcceptedStatuses.Contains(x.Status));
            else if (status == "Rejected") query = query.Where(x => RejectedStatuses.Contains(x.Status));
        }

        return await (
            from suggestion in query
            join employee in db.Employees.AsNoTracking() on suggestion.ProposerEmployeeId equals employee.Id into employeeJoin
            from employee in employeeJoin.DefaultIfEmpty()
            orderby suggestion.CreatedAtUtc descending, suggestion.Id descending
            select new SuggestionDashboardRow
            {
                Id = suggestion.Id,
                SuggestionNumber = suggestion.SuggestionNumber,
                Title = suggestion.Title,
                Category = suggestion.Category,
                Status = suggestion.Status,
                ProposerEmployeeId = suggestion.ProposerEmployeeId,
                ProposerName = employee == null ? "—" : employee.FirstName + " " + employee.LastName,
                PersonnelNumber = employee == null ? "" : employee.PersonnelNumber,
                Organization = employee == null ? "—" :
                    employee.OrganizationSection != null ? employee.OrganizationSection.Title :
                    employee.OrganizationDepartment != null ? employee.OrganizationDepartment.Title :
                    employee.OrganizationUnit != null ? employee.OrganizationUnit.Title : "—",
                CreatedAtUtc = suggestion.CreatedAtUtc,
                SubmittedAtUtc = suggestion.SubmittedAtUtc,
                MemberCount = suggestion.Members.Count(),
                AttachmentCount = suggestion.Attachments.Count(),
                BenefitsJson = suggestion.BenefitsJson,
                ResourceTypesJson = suggestion.ResourceTypesJson
            }).ToListAsync();
    }

    private static List<string> ParseList(string? json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }

    private static string StatusLabel(string status) => status switch
    {
        "Draft" => "پیش‌نویس",
        "Submitted" => "ثبت‌شده و در انتظار بررسی",
        "InEvaluation" or "Evaluating" => "در حال ارزیابی",
        "Accepted" or "Approved" or "تأیید شده" or "تأیید نهایی" or "پذیرفته شده" or "مصوب" => "پذیرفته‌شده",
        "Rejected" or "رد شده" or "عدم پذیرش" => "ردشده",
        _ => status
    };

    private static string Csv(string? value)
    {
        var text = value ?? "";
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
