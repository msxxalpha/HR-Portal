using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Mvc;

namespace HRPortal.Controllers;

public class SystemSettingsController(HRPortalDbContext db, SystemSettingsService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await service.GetAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SystemSettings model)
    {
        if (!ModelState.IsValid) return View("Index", model);
        await service.SaveAsync(model);
        TempData["Success"] = "تنظیمات محیط سامانه با موفقیت ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }
}