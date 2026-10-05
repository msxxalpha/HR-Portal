using HRPortal.Models;
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
    AnnouncementService announcements) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        return View("Index", await service.GetEditAsync());
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

    [HttpPost("Save")]
    [Authorize(Policy = "Settings.Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] SystemSettingsEditModel model, [FromForm] string? command)
    {
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
            return View("Index", persisted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "System settings save failed.");
            TempData["Error"] = "ذخیره تنظیمات انجام نشد: " + ex.Message;

            // Do not replace the submitted values by an empty model on failure.
            return View("Index", model);
        }
    }
}
