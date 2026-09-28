using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRPortal.Controllers;

[Authorize(Policy = "Settings.View")]
[Route("SystemSettings")]
public class SystemSettingsController(EnvironmentSettingsService service, ISmsService sms) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index() =>
        View(await service.GetAsync());

    [HttpPost("Save")]
    [Authorize(Policy = "Settings.Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SystemSettingsViewModel model, string? command)
    {
        if (!ModelState.IsValid)
            return View("Index", model);

        await service.SaveAsync(model);

        if (string.Equals(command, "save-test-sms", StringComparison.OrdinalIgnoreCase))
        {
            var recipient = model.Sms.TestRecipient?.Trim() ?? "";
            if (recipient.Length == 0)
            {
                TempData["Error"] = "شماره مقصد آزمایشی را وارد کنید.";
            }
            else
            {
                var result = await sms.SendOtpAsync(recipient, "12345");
                TempData[result.Success ? "Success" : "Error"] =
                    result.Success
                        ? "تنظیمات ذخیره شد و پیامک آزمایشی با موفقیت به سرویس‌دهنده ارسال شد."
                        : "تنظیمات ذخیره شد؛ ارسال پیامک آزمایشی ناموفق بود: " + result.Response;
            }
        }
        else
        {
            TempData["Success"] = "تنظیمات سامانه با موفقیت ذخیره شد.";
        }

        return RedirectToAction(nameof(Index));
    }
}
