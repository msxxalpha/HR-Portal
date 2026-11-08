using System.Security.Claims;
using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "Reports.ActivityPerformance.View")]
public class ReportsController(HRPortalDbContext db) : Controller
{
    private static readonly HashSet<string> ReportableRanks = new(StringComparer.OrdinalIgnoreCase)
    {
        "مدیرعامل", "معاونت", "معاون", "مدیریت", "مدیر", "ریاست", "رئیس"
    };

    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(ActivityPerformance));

    [HttpGet]
    public IActionResult ExecutiveWorkflow() => RedirectToAction(nameof(ActivityPerformance));

    [HttpGet]
    public async Task<IActionResult> ActivityPerformance(
        string? fromJalali, string? toJalali, string? userName, string? q,
        string? category, string? method, bool? success, int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(User.FindFirstValue("EmployeeId"), out var currentEmployeeId))
            return Forbid();

        var employee = await db.Employees.AsNoTracking()
            .Where(x => x.Id == currentEmployeeId && x.IsSystemUser && x.Status == "فعال")
            .Select(x => new { x.Id, x.OrganizationPositionId })
            .FirstOrDefaultAsync(cancellationToken);
        if (employee is null) return Forbid();

        var revisionId = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.FinalizedAt).ThenByDescending(x => x.Id)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (!revisionId.HasValue)
        {
            ViewBag.NoReportScope = true;
            return View(new ActivityPerformanceReportViewModel());
        }

        var nodes = await db.OrganizationNodes.AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == revisionId.Value && x.IsActive)
            .Select(x => new
            {
                x.Id, x.ParentId, x.Title, x.Code, x.RankType,
                x.PositionHolderEmployeeId, x.AssignedEmployeeIdsCsv
            }).ToListAsync(cancellationToken);

        static HashSet<int> ParseEmployeeIds(string? csv)
        {
            var result = new HashSet<int>();
            foreach (var raw in (csv ?? string.Empty).Split([',', '،', ';', '؛'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(raw, out var id) && id > 0) result.Add(id);
            return result;
        }

        var userNodes = nodes.Where(n =>
            n.PositionHolderEmployeeId == currentEmployeeId ||
            n.Id == employee.OrganizationPositionId ||
            ParseEmployeeIds(n.AssignedEmployeeIdsCsv).Contains(currentEmployeeId)).ToList();
        var eligibleNodes = userNodes.Where(n => ReportableRanks.Contains((n.RankType ?? string.Empty).Trim())).ToList();
        if (eligibleNodes.Count == 0) return Forbid();

        // Scope is derived only from the authenticated user's current position
        // in the finalized organization tree; request parameters cannot widen it.
        var childrenByParent = nodes.Where(n => n.ParentId.HasValue)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());
        var scopeNodeIds = new HashSet<int>();
        var pending = new Queue<int>(eligibleNodes.Select(n => n.Id));
        while (pending.Count > 0)
        {
            var nodeId = pending.Dequeue();
            if (!scopeNodeIds.Add(nodeId)) continue;
            if (childrenByParent.TryGetValue(nodeId, out var children))
                foreach (var childId in children) pending.Enqueue(childId);
        }

        var subordinateNodeIds = scopeNodeIds.Except(eligibleNodes.Select(n => n.Id)).ToHashSet();\n        var scopedEmployees = nodes.Where(n => subordinateNodeIds.Contains(n.Id))
            .SelectMany(n => ParseEmployeeIds(n.AssignedEmployeeIdsCsv)
                .Concat(n.PositionHolderEmployeeId.HasValue ? new[] { n.PositionHolderEmployeeId.Value } : Array.Empty<int>()))
            .ToHashSet();
        var positionedEmployees = await db.Employees.AsNoTracking()
            .Where(x => x.OrganizationPositionId.HasValue && subordinateNodeIds.Contains(x.OrganizationPositionId.Value))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        scopedEmployees.UnionWith(positionedEmployees);
        scopedEmployees.Remove(currentEmployeeId);

        var model = new ActivityPerformanceReportViewModel
        {
            FromJalali = fromJalali, ToJalali = toJalali, UserName = userName,
            Search = q, Category = category, Method = method, Success = success,
            ScopeTitle = eligibleNodes.Count == 1 ? eligibleNodes[0].Title :
                string.Join("، ", eligibleNodes.Select(x => x.Title).Distinct()),
            ScopedEmployeeCount = scopedEmployees.Count
        };

        DateTime? fromUtc = null, toUtcExclusive = null;
        if (!string.IsNullOrWhiteSpace(fromJalali))
        {
            if (PersianDateService.TryParse(fromJalali, out var parsed))
            {
                fromUtc = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
                model.FromDate = parsed.Date;
            }
            else ModelState.AddModelError(nameof(fromJalali), "تاریخ شروع شمسی معتبر نیست.");
        }
        if (!string.IsNullOrWhiteSpace(toJalali))
        {
            if (PersianDateService.TryParse(toJalali, out var parsed))
            {
                toUtcExclusive = DateTime.SpecifyKind(parsed.Date.AddDays(1), DateTimeKind.Utc);
                model.ToDate = parsed.Date;
            }
            else ModelState.AddModelError(nameof(toJalali), "تاریخ پایان شمسی معتبر نیست.");
        }
        if (fromUtc.HasValue && toUtcExclusive.HasValue && fromUtc >= toUtcExclusive)
        {
            ModelState.AddModelError(nameof(toJalali), "تاریخ پایان نباید قبل از تاریخ شروع باشد.");
            var originalFrom = fromUtc.Value;
            fromUtc = toUtcExclusive.Value.AddDays(-1);
            toUtcExclusive = originalFrom.AddDays(1);
        }

        var query = db.AuditLogs.AsNoTracking()
            .Where(x => x.EmployeeId.HasValue && scopedEmployees.Contains(x.EmployeeId.Value));
        if (fromUtc.HasValue) query = query.Where(x => x.CreatedAt >= fromUtc.Value);
        if (toUtcExclusive.HasValue) query = query.Where(x => x.CreatedAt < toUtcExclusive.Value);
        if (!string.IsNullOrWhiteSpace(userName))
            query = query.Where(x => x.UserName != null && x.UserName.Contains(userName.Trim()));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.Action.Contains(term) || x.EntityName.Contains(term) ||
                (x.Description != null && x.Description.Contains(term)) ||
                (x.RequestPath != null && x.RequestPath.Contains(term)) ||
                (x.EntityId != null && x.EntityId.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(method)) query = query.Where(x => x.HttpMethod == method);
        if (success.HasValue) query = query.Where(x => x.IsSuccess == success.Value);

        model.TotalCount = await query.CountAsync(cancellationToken);
        model.FailureCount = await query.CountAsync(x =>
            x.IsSuccess == false || (x.StatusCode.HasValue && x.StatusCode >= 400), cancellationToken);
        model.SuccessCount = model.TotalCount - model.FailureCount;
        model.UniqueUserCount = await query.Where(x => x.UserName != null)
            .Select(x => x.UserName!).Distinct().CountAsync(cancellationToken);
        model.Page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(model.TotalCount / 50d)));
        model.Items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((model.Page - 1) * 50).Take(50).ToListAsync(cancellationToken);

        ViewBag.NoReportScope = false;
        return View(model);
    }
}
