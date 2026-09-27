using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRPortal.Controllers;

[Authorize(Policy = "AdminOnly")]
public class SystemSettingsController(EnvironmentSettingsService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() =>
        View(await service.GetAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SystemSettingsViewModel model)
    {
        if (!ModelState.IsValid)
            return View("Index", model);

        await service.SaveAsync(model);
        TempData["Success"] = "تنظیمات محیط سامانه با موفقیت ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }
}