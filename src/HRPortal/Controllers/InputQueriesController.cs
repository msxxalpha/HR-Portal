using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "InputQueries.View")]
public class InputQueriesController(
    HRPortalDbContext db,
    ReportCredentialProtector protector,
    AuditService audit) : Controller
{
    public async Task<IActionResult> Index()
        => View(await db.InputQueries.AsNoTracking().OrderBy(x => x.Title).ToListAsync());

    [Authorize(Policy = "InputQueries.Manage")]
    public IActionResult Create()
        => View("Form", new InputQuery { AuthenticationMode = "sql", TrustServerCertificate = true, CommandTimeoutSeconds = 30 });

    [Authorize(Policy = "InputQueries.Manage")]
    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.InputQueries.FindAsync(id);
        return entity is null ? NotFound() : View("Form", entity);
    }

    [HttpPost, Authorize(Policy = "InputQueries.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(InputQuery model, string? password)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            ModelState.AddModelError(nameof(model.Title), "عنوان کوئری الزامی است.");
        if (string.IsNullOrWhiteSpace(model.SqlText))
            ModelState.AddModelError(nameof(model.SqlText), "متن SQL الزامی است.");
        if (string.IsNullOrWhiteSpace(model.ServerInstance))
            ModelState.AddModelError(nameof(model.ServerInstance), "نام سرور/اینستنس الزامی است.");
        if (string.IsNullOrWhiteSpace(model.DatabaseName))
            ModelState.AddModelError(nameof(model.DatabaseName), "نام پایگاه داده الزامی است.");

        var duplicate = await db.InputQueries.AnyAsync(x => x.Id != model.Id && x.Title == model.Title.Trim());
        if (duplicate)
            ModelState.AddModelError(nameof(model.Title), "این عنوان قبلاً ثبت شده است.");

        if (!ModelState.IsValid)
            return View("Form", model);

        InputQuery entity;
        if (model.Id == 0)
        {
            entity = model;
            entity.CreatedAt = DateTime.UtcNow;
            db.InputQueries.Add(entity);
        }
        else
        {
            entity = await db.InputQueries.FindAsync(model.Id) ?? throw new InvalidOperationException("کوئری یافت نشد.");
            entity.Title = model.Title.Trim();
            entity.SqlText = model.SqlText;
            entity.ServerInstance = model.ServerInstance.Trim();
            entity.DatabaseName = model.DatabaseName.Trim();
            entity.AuthenticationMode = model.AuthenticationMode?.Trim().ToLowerInvariant() == "windows" ? "windows" : "sql";
            entity.Username = model.Username?.Trim() ?? "";
            entity.Encrypt = model.Encrypt;
            entity.TrustServerCertificate = model.TrustServerCertificate;
            entity.Enabled = model.Enabled;
            entity.CommandTimeoutSeconds = Math.Clamp(model.CommandTimeoutSeconds, 5, 300);
            if (!string.IsNullOrWhiteSpace(password))
                entity.PasswordProtected = protector.Protect(password);
            entity.UpdatedAt = DateTime.UtcNow;
        }

        if (model.Id == 0)
        {
            entity.Title = model.Title.Trim();
            entity.ServerInstance = model.ServerInstance.Trim();
            entity.DatabaseName = model.DatabaseName.Trim();
            entity.AuthenticationMode = model.AuthenticationMode?.Trim().ToLowerInvariant() == "windows" ? "windows" : "sql";
            entity.Username = model.Username?.Trim() ?? "";
            entity.Encrypt = model.Encrypt;
            entity.TrustServerCertificate = model.TrustServerCertificate;
            entity.Enabled = model.Enabled;
            entity.CommandTimeoutSeconds = Math.Clamp(model.CommandTimeoutSeconds, 5, 300);
            entity.PasswordProtected = string.IsNullOrWhiteSpace(password) ? "" : protector.Protect(password);
        }

        await db.SaveChangesAsync();
        await audit.WriteAsync(model.Id == 0 ? "ایجاد کوئری ورودی" : "ویرایش کوئری ورودی", "InputQuery", entity.Id.ToString(), entity.Title);
        TempData["Success"] = "کوئری با موفقیت ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = "InputQueries.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.InputQueries.FindAsync(id);
        if (entity is not null)
        {
            db.InputQueries.Remove(entity);
            await db.SaveChangesAsync();
            await audit.WriteAsync("حذف کوئری ورودی", "InputQuery", id.ToString(), entity.Title);
            TempData["Success"] = "کوئری حذف شد.";
        }
        return RedirectToAction(nameof(Index));
    }
}
