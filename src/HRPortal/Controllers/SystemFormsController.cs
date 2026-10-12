using HRPortal.Data;
using HRPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "Settings.Edit")]
public class SystemFormsController(HRPortalDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var forms = await db.SystemFormDefinitions.AsNoTracking()
            .OrderBy(x => x.Title).ToListAsync();
        return View(forms);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string code, string title, string implementationType, bool isActive, string? description)
    {
        try
        {
            code = (code ?? "").Trim();
            title = (title ?? "").Trim();
            implementationType = (implementationType ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException("کد سیستمی و عنوان فرم الزامی است.");
            if (code.Length > 100 || title.Length > 200)
                throw new InvalidOperationException("طول کد یا عنوان فرم بیش از حد مجاز است.");
            if (implementationType is not ("Custom" or "GenericWorkflow" or "Standalone"))
                throw new InvalidOperationException("نوع پیاده‌سازی فرم معتبر نیست.");

            var entity = id == 0 ? null : await db.SystemFormDefinitions.FirstOrDefaultAsync(x => x.Id == id);
            if (id != 0 && entity is null) throw new InvalidOperationException("فرم موردنظر یافت نشد.");
            if (await db.SystemFormDefinitions.AnyAsync(x => x.Code == code && x.Id != id))
                throw new InvalidOperationException("کد سیستمی فرم تکراری است.");

            if (entity is null)
            {
                entity = new SystemFormDefinition { Code = code, CreatedAt = DateTime.UtcNow };
                db.SystemFormDefinitions.Add(entity);
            }
            else if (entity.ImplementationType == "Custom" && implementationType != "Custom")
            {
                throw new InvalidOperationException("نوع پیاده‌سازی فرم اختصاصی قابل تغییر نیست؛ این فرم باید مستقل از موتور عمومی باقی بماند.");
            }

            if (entity.ImplementationType == "Custom") implementationType = "Custom";
            if (implementationType != "GenericWorkflow" &&
                await db.WorkflowDefinitions.AnyAsync(x => x.FormCode == code && x.IsActive && !x.IsRuntimeInstance))
                throw new InvalidOperationException("ابتدا گردش کار فعال این فرم را به فرم دیگری منتقل یا غیرفعال کنید.");

            entity.Code = code;
            entity.Title = title;
            entity.ImplementationType = implementationType;
            entity.IsActive = isActive;
            entity.Description = description?.Trim();
            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            TempData["Success"] = "مشخصات فرم ذخیره شد.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
