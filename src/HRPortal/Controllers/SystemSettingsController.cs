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
    public async Task<IActionResult> Index()
    {
        return View("Index", await service.GetAsync());
    }

    [HttpPost("Save")]
    [Authorize(Policy = "Settings.Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] SystemSettingsViewModel model, [FromForm] string? command)
    {
        // Settings are optional in the first release. The service applies safe defaults
        // and stores the complete settings set transactionally.
        await service.SaveAsync(model);

        if (string.Equals(command, "save-test-sms", StringComparison.OrdinalIgnoreCase))
        {
            var recipient = model.Sms?.TestRecipient?.Trim() ?? "";
            if (recipient.Length == 0)
            {
                TempData["Error"] = "شماره مقصد آزمایشی را وارد کنید.";
            }
            else
            {
                var result = await sms.SendOtpAsync(recipient, "12345");
                TempData[result.Success ? "Success" : "Error"] =
                    result.Success
                        ? "تنظیمات ذخیره شد و پیامک آزمایشی با موفقیت ارسال شد."
                        : "تنظیمات ذخیره شد؛ ارسال پیامک آزمایشی ناموفق بود: " + result.Response;
            }
        }
        else
        {
            TempData["Success"] = "همه تنظیمات سامانه با موفقیت ذخیره شد.";
        }

        // Always reload the actual persisted values, so the page reflects the database,
        // not the transient POST model.
        return RedirectToAction(nameof(Index));
    }
}
