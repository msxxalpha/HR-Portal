using ClosedXML.Excel;
using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "Settings.View")]
public class OrganizationPositionsController(HRPortalDbContext db, AuditService audit) : Controller
{
    private static readonly string[] Headers =
        ["کد پست", "عنوان پست", "گروه شغلی", "رده/گرید", "توضیحات", "ترتیب نمایش", "وضعیت"];

    public async Task<IActionResult> Index(string? q, string? status, string? jobFamily,
        string sort = "Code", string dir = "asc", int page = 1, int pageSize = 25, int? editId = null)
    {
        pageSize = pageSize is 25 or 50 or 75 or 100 ? pageSize : 25;
        page = Math.Max(1, page);
        var query = ApplyFilters(db.OrganizationPositionDefinitions.AsNoTracking(), q, status, jobFamily);
        query = (sort, dir == "desc") switch
        {
            ("Title", true) => query.OrderByDescending(x => x.Title),
            ("Title", false) => query.OrderBy(x => x.Title),
            ("JobFamily", true) => query.OrderByDescending(x => x.JobFamily).ThenBy(x => x.Title),
            ("JobFamily", false) => query.OrderBy(x => x.JobFamily).ThenBy(x => x.Title),
            ("IsActive", true) => query.OrderByDescending(x => x.IsActive).ThenBy(x => x.Title),
            ("IsActive", false) => query.OrderBy(x => x.IsActive).ThenBy(x => x.Title),
            _ when dir == "desc" => query.OrderByDescending(x => x.Code),
            _ => query.OrderBy(x => x.Code)
        };

        var count = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        page = Math.Min(page, pages);
        ViewBag.Q = q ?? "";
        ViewBag.Status = status ?? "";
        ViewBag.JobFamily = jobFamily ?? "";
        ViewBag.Sort = sort;
        ViewBag.Dir = dir;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = count;
        ViewBag.TotalPages = pages;
        ViewBag.EditPosition = editId.HasValue
            ? await db.OrganizationPositionDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == editId.Value)
            : null;
        ViewBag.JobFamilies = await db.OrganizationPositionDefinitions.AsNoTracking()
            .Where(x => x.JobFamily != null && x.JobFamily != "")
            .Select(x => x.JobFamily!).Distinct().OrderBy(x => x).ToListAsync();
        return View(await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
    }

    private static IQueryable<OrganizationPositionDefinition> ApplyFilters(
        IQueryable<OrganizationPositionDefinition> query, string? q, string? status, string? jobFamily)
    {
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.Code.Contains(term) || x.Title.Contains(term) ||
                (x.JobFamily != null && x.JobFamily.Contains(term)) ||
                (x.Grade != null && x.Grade.Contains(term)) ||
                (x.Description != null && x.Description.Contains(term)));
        }
        if (status == "active") query = query.Where(x => x.IsActive);
        else if (status == "inactive") query = query.Where(x => !x.IsActive);
        if (!string.IsNullOrWhiteSpace(jobFamily))
            query = query.Where(x => x.JobFamily == jobFamily);
        return query;
    }

    [HttpPost, Authorize(Policy = "Settings.Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string? code, string? title, string? jobFamily, string? grade,
        string? description, int sortOrder = 10, bool isActive = true)
    {
        code = code?.Trim();
        title = title?.Trim();
        jobFamily = Normalize(jobFamily);
        grade = Normalize(grade);
        description = Normalize(description);

        if (string.IsNullOrWhiteSpace(code) || code.Length > 50 ||
            string.IsNullOrWhiteSpace(title) || title.Length > 150)
        {
            TempData["Error"] = "کد پست و عنوان آن الزامی است؛ حداکثر طول کد ۵۰ و عنوان ۱۵۰ نویسه است.";
            return RedirectToAction(nameof(Index), new { editId = id > 0 ? id : (int?)null });
        }

        var existing = id > 0 ? await db.OrganizationPositionDefinitions.FirstOrDefaultAsync(x => x.Id == id) : null;
        if (id > 0 && existing is null) return NotFound();
        if (await db.OrganizationPositionDefinitions.AnyAsync(x => x.Id != id && x.Code == code))
        {
            TempData["Error"] = "کد پست سازمانی تکراری است.";
            return RedirectToAction(nameof(Index), new { editId = id > 0 ? id : (int?)null });
        }
        if (await db.OrganizationPositionDefinitions.AnyAsync(x => x.Id != id && x.Title == title))
        {
            TempData["Error"] = "عنوان پست سازمانی تکراری است.";
            return RedirectToAction(nameof(Index), new { editId = id > 0 ? id : (int?)null });
        }

        var wasActive = existing?.IsActive ?? false;
        var oldTitle = existing?.Title;
        if (existing is null)
        {
            existing = new OrganizationPositionDefinition { CreatedAt = DateTime.UtcNow };
            db.OrganizationPositionDefinitions.Add(existing);
        }
        existing.Code = code;
        existing.Title = title;
        existing.JobFamily = jobFamily;
        existing.Grade = grade;
        existing.Description = description;
        existing.SortOrder = sortOrder;
        existing.IsActive = isActive;
        existing.UpdatedAt = DateTime.UtcNow;

        // Keep the existing employee data consistent if an assigned position is renamed.
        if (!string.IsNullOrWhiteSpace(oldTitle) && oldTitle != title)
        {
            var assignedEmployees = await db.Employees.Where(x => x.PositionTitle == oldTitle).ToListAsync();
            foreach (var employee in assignedEmployees)
            {
                employee.PositionTitle = title;
                employee.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();
        await audit.WriteAsync(id == 0 ? "ایجاد پست سازمانی" : "ویرایش پست سازمانی",
            nameof(OrganizationPositionDefinition), existing.Id.ToString(), existing.Code + " - " + existing.Title);
        TempData["Success"] = id == 0 ? "پست سازمانی ایجاد شد." : "تغییرات پست سازمانی ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = "Settings.Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var position = await db.OrganizationPositionDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (position is null) return NotFound();
        position.IsActive = !position.IsActive;
        position.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync(position.IsActive ? "فعال‌سازی پست سازمانی" : "غیرفعال‌سازی پست سازمانی",
            nameof(OrganizationPositionDefinition), position.Id.ToString(), position.Code + " - " + position.Title);
        TempData["Success"] = position.IsActive ? "پست سازمانی فعال شد." : "پست سازمانی غیرفعال شد؛ سوابق حفظ شده‌اند.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = "Settings.Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var position = await db.OrganizationPositionDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (position is null) return NotFound();
        if (await db.Employees.AnyAsync(x => x.PositionTitle == position.Title))
        {
            TempData["Error"] = "این پست به کارکنان اختصاص داده شده است و قابل حذف نیست؛ برای حفظ سوابق آن را غیرفعال کنید.";
            return RedirectToAction(nameof(Index));
        }
        db.OrganizationPositionDefinitions.Remove(position);
        await db.SaveChangesAsync();
        await audit.WriteAsync("حذف پست سازمانی", nameof(OrganizationPositionDefinition), id.ToString(), position.Code + " - " + position.Title);
        TempData["Success"] = "پست سازمانی حذف شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, Authorize(Policy = "Settings.View")]
    public async Task<IActionResult> Export(string? q, string? status, string? jobFamily)
    {
        var rows = await ApplyFilters(db.OrganizationPositionDefinitions.AsNoTracking(), q, status, jobFamily)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToListAsync();
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("پست‌های سازمانی");
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        for (var i = 0; i < rows.Count; i++) WriteExcelRow(ws, i + 2, rows[i]);
        StyleSheet(ws);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"OrganizationPositions-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    [HttpGet, Authorize(Policy = "Settings.View")]
    public IActionResult Template()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("پست‌های سازمانی");
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Cell(2, 1).Value = "POS-001";
        ws.Cell(2, 2).Value = "کارشناس منابع انسانی";
        ws.Cell(2, 3).Value = "منابع انسانی";
        ws.Cell(2, 4).Value = "کارشناسی";
        ws.Cell(2, 5).Value = "نمونه؛ این ردیف را پیش از ورود واقعی حذف کنید";
        ws.Cell(2, 6).Value = 10;
        ws.Cell(2, 7).Value = "فعال";
        StyleSheet(ws);

        var guide = workbook.Worksheets.Add("راهنمای ستون‌ها");
        string[,] rows =
        {
            { "ستون", "اجباری", "شرح / مقادیر مجاز" },
            { "کد پست", "بله", "شناسه یکتای پست؛ تا ۵۰ نویسه. در ورود مجدد، کد موجود برای به‌روزرسانی استفاده می‌شود." },
            { "عنوان پست", "بله", "عنوان یکتای قابل انتخاب در فرم کارکنان؛ تا ۱۵۰ نویسه." },
            { "گروه شغلی", "خیر", "برای دسته‌بندی و فیلتر مانند منابع انسانی، مالی، فنی." },
            { "رده/گرید", "خیر", "درجه، پایه یا سطح شغلی." },
            { "توضیحات", "خیر", "شرح مسئولیت یا توضیح پست." },
            { "ترتیب نمایش", "خیر", "عدد؛ مقدار پیش‌فرض ۱۰." },
            { "وضعیت", "خیر", "فعال یا غیرفعال؛ در صورت خالی بودن، فعال در نظر گرفته می‌شود." }
        };
        for (var r = 0; r < rows.GetLength(0); r++)
            for (var col = 0; col < rows.GetLength(1); col++) guide.Cell(r + 1, col + 1).Value = rows[r, col];
        StyleSheet(guide);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "OrganizationPositions-Template.xlsx");
    }

    [HttpGet, Authorize(Policy = "Settings.View")]
    public IActionResult Import() => View();

    [HttpPost, Authorize(Policy = "Settings.Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file)
    {
        var errors = new List<string>();
        if (file is null || file.Length == 0)
        {
            ViewBag.Errors = new List<string> { "فایل Excel را انتخاب کنید." };
            return View();
        }

        try
        {
            using var workbook = new XLWorkbook(file.OpenReadStream());
            var ws = workbook.Worksheet("پست‌های سازمانی");
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var byCode = await db.OrganizationPositionDefinitions.ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var row = 2; row <= lastRow; row++)
            {
                string Cell(int col) => ws.Cell(row, col).GetString().Trim();
                var code = Cell(1);
                var title = Cell(2);
                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(title)) continue;
                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title))
                {
                    errors.Add($"ردیف {row}: کد و عنوان پست الزامی هستند.");
                    continue;
                }
                if (code.Length > 50 || title.Length > 150)
                {
                    errors.Add($"ردیف {row}: طول کد حداکثر ۵۰ و عنوان حداکثر ۱۵۰ نویسه است.");
                    continue;
                }
                if (!seenCodes.Add(code))
                {
                    errors.Add($"ردیف {row}: کد «{code}» در فایل تکراری است.");
                    continue;
                }
                byCode.TryGetValue(code, out var position);
                if (await db.OrganizationPositionDefinitions.AnyAsync(x => x.Title == title && (position == null || x.Id != position.Id)))
                {
                    errors.Add($"ردیف {row}: عنوان «{title}» قبلاً برای پست دیگری استفاده شده است.");
                    continue;
                }

                var activeText = Cell(7);
                bool active;
                if (string.IsNullOrWhiteSpace(activeText) || activeText is "فعال" or "Active" or "1" or "بله")
                    active = true;
                else if (activeText is "غیرفعال" or "غیر فعال" or "Inactive" or "0" or "خیر")
                    active = false;
                else
                {
                    errors.Add($"ردیف {row}: وضعیت باید «فعال» یا «غیرفعال» باشد.");
                    continue;
                }

                var previousTitle = position?.Title;
                if (position is null)
                {
                    position = new OrganizationPositionDefinition { Code = code, Title = title };
                    db.OrganizationPositionDefinitions.Add(position);
                    byCode[code] = position;
                }
                else if (!string.Equals(previousTitle, title, StringComparison.Ordinal))
                {
                    var affectedEmployees = await db.Employees.Where(x => x.PositionTitle == previousTitle).ToListAsync();
                    foreach (var employee in affectedEmployees)
                    {
                        employee.PositionTitle = title;
                        employee.UpdatedAt = DateTime.UtcNow;
                    }
                }
                position.Code = code;
                position.Title = title;
                position.JobFamily = Normalize(Cell(3));
                position.Grade = Normalize(Cell(4));
                position.Description = Normalize(Cell(5));
                position.SortOrder = int.TryParse(Cell(6), out var order) ? order : 10;
                position.IsActive = active;
                position.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            await audit.WriteAsync("ورود اکسل پست‌های سازمانی", nameof(OrganizationPositionDefinition), "bulk",
                $"تعداد خطا: {errors.Count}");
            ViewBag.Success = errors.Count == 0 ? "اطلاعات پست‌ها با موفقیت وارد یا به‌روزرسانی شد." :
                "فایل پردازش شد؛ ردیف‌های معتبر ذخیره شدند و خطاهای باقی‌مانده را بررسی کنید.";
        }
        catch (Exception ex)
        {
            errors.Add("خطای پردازش فایل: " + ex.Message);
        }
        ViewBag.Errors = errors;
        return View();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void WriteExcelRow(IXLWorksheet ws, int row, OrganizationPositionDefinition p)
    {
        object?[] values = [p.Code, p.Title, p.JobFamily, p.Grade, p.Description, p.SortOrder, p.IsActive ? "فعال" : "غیرفعال"];
        for (var i = 0; i < values.Length; i++) ws.Cell(row, i + 1).Value = values[i]?.ToString() ?? "";
    }

    private static void StyleSheet(IXLWorksheet ws)
    {
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }
}
