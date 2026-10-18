using HRPortal.Models;
using HRPortal.Data;
using Microsoft.EntityFrameworkCore;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRPortal.Controllers;

[Authorize(Policy = "Settings.View")]
[Route("SystemSettings")]
public class SystemSettingsController(
    EnvironmentSettingsService service,
    ISmsService sms,
    ILogger<SystemSettingsController> logger,
    AnnouncementService announcements,
    HRPortalDbContext db,
    AuditService audit) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        return View("Index", await PreparePageAsync(await service.GetEditAsync()));
    }


    [HttpPost("SaveAnnouncementCategory")]
    [Authorize(Policy = "Settings.Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAnnouncementCategory(
        int id, string? title, string? description, int sortOrder = 0, bool isActive = true)
    {
        try
        {
            await announcements.SaveCategoryAsync(id, title, description, sortOrder, isActive);
            TempData["Success"] = "دسته‌بندی اطلاعیه با موفقیت ذخیره شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "ذخیره دسته‌بندی انجام نشد: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("SaveOrganizationRank")]
    [Authorize(Policy = "Settings.Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveOrganizationRank(int id, string? title, int sortOrder = 10)
    {
        title = title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 100)
        {
            TempData["Error"] = "عنوان رده سازمانی الزامی است و حداکثر ۱۰۰ نویسه دارد.";
            return Redirect("/SystemSettings#organization-ranks");
        }

        var existing = id > 0
            ? await db.OrganizationRankDefinitions.FirstOrDefaultAsync(x => x.Id == id)
            : null;
        if (id > 0 && existing is null) return NotFound();
        if (await db.OrganizationRankDefinitions.AnyAsync(x => x.Title == title && x.Id != id))
        {
            TempData["Error"] = "رده‌ای با این عنوان از قبل ثبت شده است.";
            return Redirect("/SystemSettings#organization-ranks");
        }

        if (existing is null)
        {
            existing = new OrganizationRankDefinition { Title = title, SortOrder = sortOrder };
            db.OrganizationRankDefinitions.Add(existing);
        }
        else
        {
            var oldTitle = existing.Title;
            if (oldTitle == "مدیرعامل" && oldTitle != title)
            {
                TempData["Error"] = "عنوان ویژه «مدیرعامل» قابل تغییر نیست؛ این عنوان برای تشخیص مرحله نهایی گردش کار استفاده می‌شود.";
                return Redirect("/SystemSettings#organization-ranks");
            }
            if (oldTitle != title)
            {
                var nodes = await db.OrganizationNodes.Where(x => x.RankType == oldTitle).ToListAsync();
                foreach (var node in nodes) node.RankType = title;
                var steps = await db.WorkflowSteps.Where(x => x.HierarchyStopRankType == oldTitle).ToListAsync();
                foreach (var step in steps) step.HierarchyStopRankType = title;
            }
            existing.Title = title;
            existing.SortOrder = sortOrder;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        await audit.WriteAsync(id == 0 ? "ایجاد رده سازمانی" : "ویرایش رده سازمانی",
            nameof(OrganizationRankDefinition), existing.Id.ToString(), existing.Title);
        TempData["Success"] = id == 0 ? "رده سازمانی ایجاد شد." : "رده سازمانی و ارجاع‌های آن به‌روزرسانی شد.";
        return Redirect("/SystemSettings#organization-ranks");
    }

    [HttpPost("DeleteOrganizationRank")]
    [Authorize(Policy = "Settings.Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOrganizationRank(int id)
    {
        var rank = await db.OrganizationRankDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (rank is null) return NotFound();
        if (rank.Title == "مدیرعامل" ||
            await db.OrganizationNodes.AnyAsync(x => x.RankType == rank.Title) ||
            await db.WorkflowSteps.AnyAsync(x => x.HierarchyStopRankType == rank.Title))
        {
            TempData["Error"] = "این رده در ساختار سازمانی یا گردش‌های کاری استفاده شده است و برای حفظ سوابق قابل حذف نیست.";
            return Redirect("/SystemSettings#organization-ranks");
        }

        db.OrganizationRankDefinitions.Remove(rank);
        await db.SaveChangesAsync();
        await audit.WriteAsync("حذف رده سازمانی", nameof(OrganizationRankDefinition), id.ToString(), rank.Title);
        TempData["Success"] = "رده سازمانی حذف شد.";
        return Redirect("/SystemSettings#organization-ranks");
    }

    private async Task<SystemSettingsEditModel> PreparePageAsync(SystemSettingsEditModel model)
    {
        ViewBag.OrganizationRanks = await db.OrganizationRankDefinitions.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToListAsync();
        return model;
    }

    [HttpPost("Save")]
    [Authorize(Policy = "Settings.Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] SystemSettingsEditModel model, [FromForm] string? command)
    {
        if (model.PayrollMinimumYear < 1300 || model.PayrollMinimumYear > 1600)
        {
            TempData["Error"] = "سال شروع مشاهده فیش حقوقی باید یک سال شمسی چهاررقمی بین ۱۳۰۰ تا ۱۶۰۰ باشد.";
            return Redirect("/SystemSettings#general");
        }

        try
        {
            await service.SaveAsync(model);

            if (string.Equals(command, "save-test-sms", StringComparison.OrdinalIgnoreCase))
            {
                var recipient = model.SmsTestRecipient?.Trim() ?? "";
                if (recipient.Length == 0)
                {
                    TempData["Error"] = "شماره مقصد آزمایشی وارد نشده است.";
                }
                else
                {
                    var result = await sms.SendOtpAsync(recipient, "12345");
                    TempData[result.Success ? "Success" : "Error"] =
                        result.Success
                            ? "همه تنظیمات ذخیره شد و پیامک آزمایشی ارسال شد."
                            : "همه تنظیمات ذخیره شد، اما ارسال پیامک آزمایشی ناموفق بود: " + result.Response;
                }
            }
            else
            {
                TempData["Success"] = "همه تنظیمات سامانه با موفقیت ذخیره شد.";
            }

            // Re-read the committed data to guarantee that the displayed values
            // are exactly what is in SQL Server.
            var persisted = await service.GetEditAsync();
            return View("Index", await PreparePageAsync(persisted));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "System settings save failed.");
            TempData["Error"] = "ذخیره تنظیمات انجام نشد: " + ex.Message;

            // Do not replace the submitted values by an empty model on failure.
            return View("Index", await PreparePageAsync(model));
        }
    }
}
