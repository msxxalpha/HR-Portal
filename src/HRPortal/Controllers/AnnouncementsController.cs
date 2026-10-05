using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRPortal.Controllers;

[Authorize(Policy = "Announcements.Manage")]
public class AnnouncementsController(AnnouncementService announcements, AuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? id = null)
    {
        return View(await announcements.GetManagementAsync(id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AnnouncementEditModel model)
    {
        try
        {
            await announcements.SaveAsync(model);
            await audit.WriteAsync(
                model.Id > 0 ? "ویرایش اطلاعیه" : "ایجاد اطلاعیه",
                "Announcement",
                model.Id.ToString(),
                model.Title);
            TempData["Success"] = model.Id > 0 ? "اطلاعیه با موفقیت ویرایش شد." : "اطلاعیه با موفقیت ثبت شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "ذخیره اطلاعیه انجام نشد: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        try
        {
            await announcements.SetActiveAsync(id, active);
            await audit.WriteAsync(active ? "فعال‌سازی اطلاعیه" : "غیرفعال‌سازی اطلاعیه", "Announcement", id.ToString(), id.ToString());
            TempData["Success"] = active ? "اطلاعیه فعال شد." : "اطلاعیه غیرفعال شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "تغییر وضعیت اطلاعیه انجام نشد: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await announcements.DeleteAsync(id);
            await audit.WriteAsync("حذف اطلاعیه", "Announcement", id.ToString(), id.ToString());
            TempData["Success"] = "اطلاعیه حذف شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "حذف اطلاعیه انجام نشد: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
