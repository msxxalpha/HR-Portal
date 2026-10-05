using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class AnnouncementService(HRPortalDbContext db)
{
    private static readonly string[] ValidPriorities = ["normal", "important", "urgent"];

    public async Task<AnnouncementManagementViewModel> GetManagementAsync(int? id = null)
    {
        var categories = await db.AnnouncementCategories
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .ToListAsync();

        var announcements = await db.Announcements
            .AsNoTracking()
            .Include(x => x.Category)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.StartAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var selected = id.HasValue
            ? announcements.FirstOrDefault(x => x.Id == id.Value)
            : null;

        var timeZone = await GetTimeZoneAsync();
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

        var model = new AnnouncementManagementViewModel
        {
            Announcements = announcements,
            Categories = categories,
            CurrentSystemDateTime = PersianDateService.FormatDateTime(nowLocal),
            Form = selected is null
                ? CreateDefault(categories, nowLocal)
                : ToEditModel(selected, timeZone)
        };

        return model;
    }

    public async Task<List<Announcement>> GetActiveAsync()
    {
        var now = DateTime.UtcNow;
        var items = await db.Announcements
            .AsNoTracking()
            .Include(x => x.Category)
            .Where(x => x.IsActive &&
                        x.Category != null &&
                        x.Category.IsActive &&
                        x.StartAtUtc <= now &&
                        x.EndAtUtc >= now)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.StartAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        // PriorityRank is a CLR method and cannot be translated to SQL by EF Core.
        // Apply this final ranking only after the small active-announcements set
        // has been materialized.
        return items
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => PriorityRank(x.Priority))
            .ThenByDescending(x => x.StartAtUtc)
            .ThenByDescending(x => x.Id)
            .ToList();
    }

    public async Task SaveAsync(AnnouncementEditModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var title = model.Title?.Trim() ?? "";
        var body = model.Body?.Trim() ?? "";
        if (title.Length == 0)
            throw new InvalidOperationException("عنوان اطلاعیه الزامی است.");
        if (body.Length == 0)
            throw new InvalidOperationException("متن اطلاعیه الزامی است.");

        var category = await db.AnnouncementCategories.FirstOrDefaultAsync(x => x.Id == model.CategoryId);
        if (category is null)
            throw new InvalidOperationException("دسته‌بندی اطلاعیه انتخاب‌شده یافت نشد.");
        if (!category.IsActive)
            throw new InvalidOperationException("دسته‌بندی انتخاب‌شده غیرفعال است.");

        if (!PersianDateService.TryParseDateTime(model.StartAt, out var startLocal) ||
            !PersianDateService.TryParseDateTime(model.EndAt, out var endLocal))
            throw new InvalidOperationException("تاریخ و زمان شروع و پایان را با قالب ۱۴۰۵/۰۷/۰۵ 08:30 وارد کنید.");

        if (endLocal <= startLocal)
            throw new InvalidOperationException("زمان پایان باید بعد از زمان شروع باشد.");

        var timeZone = await GetTimeZoneAsync();
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), timeZone);

        var priority = ValidPriorities.Contains(model.Priority, StringComparer.OrdinalIgnoreCase)
            ? model.Priority.Trim().ToLowerInvariant()
            : "normal";

        Announcement entity;
        if (model.Id > 0)
        {
            entity = await db.Announcements.FirstOrDefaultAsync(x => x.Id == model.Id)
                ?? throw new InvalidOperationException("اطلاعیه موردنظر یافت نشد.");
        }
        else
        {
            entity = new Announcement();
            db.Announcements.Add(entity);
        }

        entity.Title = title;
        entity.Summary = model.Summary?.Trim() ?? "";
        entity.Body = body;
        entity.CategoryId = category.Id;
        entity.StartAtUtc = startUtc;
        entity.EndAtUtc = endUtc;
        entity.Priority = priority;
        entity.IsPinned = model.IsPinned;
        entity.IsActive = model.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int id, bool active)
    {
        var item = await db.Announcements.FindAsync(id)
            ?? throw new InvalidOperationException("اطلاعیه موردنظر یافت نشد.");

        item.IsActive = active;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var item = await db.Announcements.FindAsync(id)
            ?? throw new InvalidOperationException("اطلاعیه موردنظر یافت نشد.");

        db.Announcements.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task SaveCategoryAsync(int id, string? title, string? description, int sortOrder, bool isActive)
    {
        var normalizedTitle = title?.Trim() ?? "";
        if (normalizedTitle.Length == 0)
            throw new InvalidOperationException("عنوان دسته‌بندی الزامی است.");

        AnnouncementCategory entity;
        if (id > 0)
        {
            entity = await db.AnnouncementCategories.FindAsync(id)
                ?? throw new InvalidOperationException("دسته‌بندی موردنظر یافت نشد.");
        }
        else
        {
            entity = new AnnouncementCategory();
            db.AnnouncementCategories.Add(entity);
        }

        var duplicate = await db.AnnouncementCategories.AnyAsync(x =>
            x.Id != id && x.Title == normalizedTitle);
        if (duplicate)
            throw new InvalidOperationException("عنوان دسته‌بندی تکراری است.");

        entity.Title = normalizedTitle;
        entity.Description = description?.Trim() ?? "";
        entity.SortOrder = sortOrder;
        entity.IsActive = isActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task SetCategoryActiveAsync(int id, bool active)
    {
        var category = await db.AnnouncementCategories.FindAsync(id)
            ?? throw new InvalidOperationException("دسته‌بندی موردنظر یافت نشد.");

        if (!active)
        {
            var used = await db.Announcements.AnyAsync(x => x.CategoryId == id && x.IsActive);
            if (used)
                throw new InvalidOperationException("این دسته‌بندی در اطلاعیه‌های فعال استفاده شده است. ابتدا اطلاعیه‌های مربوط را غیرفعال یا ویرایش کنید.");
        }

        category.IsActive = active;
        category.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<string> FormatLocal(DateTime utcValue)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcValue, DateTimeKind.Utc),
            await GetTimeZoneAsync());
        return PersianDateService.FormatDateTime(local);
    }

    private static AnnouncementEditModel CreateDefault(List<AnnouncementCategory> categories, DateTime now)
    {
        var start = now;
        var end = now.AddMonths(1);
        return new AnnouncementEditModel
        {
            CategoryId = categories.FirstOrDefault(x => x.IsActive)?.Id ?? 0,
            StartAt = PersianDateService.FormatDateTime(start),
            EndAt = PersianDateService.FormatDateTime(end),
            Priority = "normal",
            IsPinned = true,
            IsActive = true
        };
    }

    private static AnnouncementEditModel ToEditModel(Announcement entity, TimeZoneInfo timeZone)
    {
        var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(entity.StartAtUtc, DateTimeKind.Utc), timeZone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(entity.EndAtUtc, DateTimeKind.Utc), timeZone);

        return new AnnouncementEditModel
        {
            Id = entity.Id,
            Title = entity.Title,
            Summary = entity.Summary,
            Body = entity.Body,
            CategoryId = entity.CategoryId,
            StartAt = PersianDateService.FormatDateTime(start),
            EndAt = PersianDateService.FormatDateTime(end),
            Priority = entity.Priority,
            IsPinned = entity.IsPinned,
            IsActive = entity.IsActive
        };
    }

    private async Task<DateTime> GetLocalNowAsync() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, await GetTimeZoneAsync());

    private async Task<TimeZoneInfo> GetTimeZoneAsync()
    {
        var id = await db.SystemSettings
            .AsNoTracking()
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync();

        id = string.IsNullOrWhiteSpace(id) ? "Asia/Tehran" : id.Trim();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            var mapped = id switch
            {
                "Asia/Tehran" => "Iran Standard Time",
                "Asia/Baku" => "Azerbaijan Standard Time",
                "Europe/Istanbul" => "Turkey Standard Time",
                "Asia/Dubai" => "Arabian Standard Time",
                "UTC" => "UTC",
                _ => "Iran Standard Time"
            };

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(mapped);
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }

    private static int PriorityRank(string? priority) =>
        string.Equals(priority, "urgent", StringComparison.OrdinalIgnoreCase) ? 3 :
        string.Equals(priority, "important", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
}
