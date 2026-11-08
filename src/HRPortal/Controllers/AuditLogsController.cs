using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("AuditLogs")]
public class AuditLogsController(HRPortalDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        DateTime? from, DateTime? to, string? fromJalali, string? toJalali, string? q, string? userName,
        string? category, string? method, bool? success, int page = 1)
    {
        if (!string.IsNullOrWhiteSpace(fromJalali))
        {
            if (PersianDateService.TryParse(fromJalali, out var parsedFrom)) from = parsedFrom;
            else ModelState.AddModelError(nameof(fromJalali), "تاریخ شروع شمسی معتبر نیست.");
        }
        if (!string.IsNullOrWhiteSpace(toJalali))
        {
            if (PersianDateService.TryParse(toJalali, out var parsedTo)) to = parsedTo;
            else ModelState.AddModelError(nameof(toJalali), "تاریخ پایان شمسی معتبر نیست.");
        }

        var todayTehran = PersianDateService.ToTehran(DateTime.UtcNow).Date;
        var fromLocal = from?.Date ?? todayTehran.AddDays(-7);
        var toLocalExclusive = to?.Date.AddDays(1) ?? todayTehran.AddDays(1);
        if (toLocalExclusive <= fromLocal) toLocalExclusive = fromLocal.AddDays(1);
        var fromUtc = PersianDateService.ToUtcFromTehran(fromLocal);
        var toUtcExclusive = PersianDateService.ToUtcFromTehran(toLocalExclusive);

        var query = db.AuditLogs.AsNoTracking()
            .Where(x => x.CreatedAt >= fromUtc && x.CreatedAt < toUtcExclusive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x =>
                (x.RequestPath != null && x.RequestPath.Contains(term)) ||
                x.EntityName.Contains(term) ||
                x.Action.Contains(term) ||
                (x.Description != null && x.Description.Contains(term)) ||
                (x.EntityId != null && x.EntityId.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(userName))
            query = query.Where(x => x.UserName != null && x.UserName.Contains(userName.Trim()));
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(method))
            query = query.Where(x => x.HttpMethod == method);
        if (success.HasValue)
            query = query.Where(x => x.IsSuccess == success.Value);

        var total = await query.CountAsync();
        var failures = await query.CountAsync(x => x.IsSuccess == false || (x.StatusCode.HasValue && x.StatusCode >= 400));
        var users = await query.Where(x => x.UserName != null).Select(x => x.UserName!).Distinct().CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 50d)));

        var model = new AuditLogReportViewModel
        {
            FromUtc = fromUtc,
            ToUtc = toUtcExclusive.AddDays(-1),
            Search = q,
            UserName = userName,
            Category = category,
            Method = method,
            Success = success,
            Page = page,
            TotalCount = total,
            FailureCount = failures,
            SuccessCount = total - failures,
            UniqueUserCount = users,
            Items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip((page - 1) * 50).Take(50).ToListAsync()
        };
        return View(model);
    }
}
