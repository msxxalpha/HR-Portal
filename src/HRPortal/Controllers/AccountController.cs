using HRPortal.Data;
using HRPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace HRPortal.Controllers;

public class AccountController(HRPortalDbContext db, OtpService otp, AuditService audit, AdminService admins, RoleService roleService) : Controller
{
    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null, string? mode = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect("/Home/Index");

        ViewBag.Mode = string.Equals(mode, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "employee";
        return View(new LoginVm { ReturnUrl = returnUrl, Mode = ViewBag.Mode });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> EmployeeLogin(LoginVm model)
    {
        model.Mode = "employee";
        if (string.IsNullOrWhiteSpace(model.PersonnelNumber))
        {
            ModelState.AddModelError("", "شماره پرسنلی الزامی است.");
            return View("Login", model);
        }

        var personnelNumber = model.PersonnelNumber.Trim();

        var employee = await db.Employees.SingleOrDefaultAsync(x =>
            x.PersonnelNumber == personnelNumber &&
            x.IsSystemUser &&
            x.Status == "فعال");

        if (employee is null)
        {
            await audit.WriteAsync("ورود ناموفق کارکنان", "Account", personnelNumber, "کارمند فعال یافت نشد.");
            ModelState.AddModelError("", "کارمند فعال با این شماره پرسنلی یافت نشد.");
            return View("Login", model);
        }

        if (string.IsNullOrWhiteSpace(employee.Mobile))
        {
            ModelState.AddModelError("", "شماره همراه کارمند ثبت نشده است.");
            return View("Login", model);
        }

        var result = await otp.IssueAsync(employee);
        HttpContext.Session.SetString("PendingPersonnel", employee.PersonnelNumber);
        HttpContext.Session.SetString("PendingReturnUrl", model.ReturnUrl ?? "");

        if (result.DebugCode is not null)
            TempData["DebugOtp"] = result.DebugCode;

        if (!result.Success && result.DebugCode is null)
        {
            HttpContext.Session.Remove("PendingPersonnel");
            HttpContext.Session.Remove("PendingReturnUrl");
            ModelState.AddModelError("", result.Message);
            return View("Login", model);
        }

        if (!result.Success)
            TempData["OtpError"] = result.Message;

        return RedirectToAction(nameof(Verify));
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> AdminLogin(LoginVm model)
    {
        model.Mode = "admin";

        if (string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError("", "نام کاربری و رمز عبور مدیر الزامی است.");
            return View("Login", model);
        }

        var admin = await admins.FindAsync(model.Username);

        if (admin is null || !admins.VerifyPassword(admin, model.Password))
        {
            await audit.WriteAsync("ورود ناموفق مدیر", "AdminUser", model.Username, "نام کاربری یا رمز عبور نادرست است.");
            ModelState.AddModelError("", "نام کاربری یا رمز عبور صحیح نیست.");
            return View("Login", model);
        }

        await admins.MarkLoginAsync(admin);

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.Name, admin.DisplayName),
            new("AdminId", admin.Id.ToString()),
            new("AdminUsername", admin.Username),
            new("IsAdmin", "1"),
            new("UserType", "Admin")
        };

        var identity = new System.Security.Claims.ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new System.Security.Claims.ClaimsPrincipal(identity));

        await audit.WriteAsync("ورود موفق مدیر", "AdminUser", admin.Id.ToString(), admin.Username);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Verify()
    {
        var personnel = HttpContext.Session.GetString("PendingPersonnel");
        if (string.IsNullOrWhiteSpace(personnel))
            return RedirectToAction(nameof(Login));

        ViewBag.PersonnelNumber = personnel;
        return View();
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(string code)
    {
        var personnelNumber = HttpContext.Session.GetString("PendingPersonnel");
        var returnUrl = HttpContext.Session.GetString("PendingReturnUrl");

        if (string.IsNullOrWhiteSpace(personnelNumber))
            return RedirectToAction(nameof(Login));

        if (!await otp.VerifyAsync(personnelNumber, code))
        {
            ModelState.AddModelError("", "کد واردشده نادرست، منقضی یا بیش از حد مجاز تلاش شده است.");
            ViewBag.PersonnelNumber = personnelNumber;
            return View();
        }

        var employee = await db.Employees.SingleAsync(x => x.PersonnelNumber == personnelNumber);

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.Name, $"{employee.FirstName} {employee.LastName}"),
            new("EmployeeId", employee.Id.ToString()),
            new("PersonnelNumber", employee.PersonnelNumber),
            new("IsAdmin", "0"),
            new("UserType", "Employee")
        };

        var permissions = await roleService.GetEmployeePermissionsAsync(employee.Id);
        foreach (var permission in permissions)
            claims.Add(new System.Security.Claims.Claim("Permission", permission));

        var identity = new System.Security.Claims.ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new System.Security.Claims.ClaimsPrincipal(identity));

        HttpContext.Session.Remove("PendingPersonnel");
        HttpContext.Session.Remove("PendingReturnUrl");

        await audit.WriteAsync("ورود موفق کارمند", "Account", employee.Id.ToString(), employee.PersonnelNumber, employee.Id);

        return Redirect(!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    [HttpGet, Authorize(Policy = "AdminOnly")]
    public IActionResult ChangePassword()
    {
        return View();
    }

    [HttpPost, Authorize(Policy = "AdminOnly"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm model)
    {
        if (string.IsNullOrWhiteSpace(model.NewPassword) ||
            model.NewPassword.Length < 8)
            ModelState.AddModelError(nameof(model.NewPassword), "رمز عبور جدید باید حداقل ۸ کاراکتر باشد.");

        if (model.NewPassword != model.ConfirmPassword)
            ModelState.AddModelError(nameof(model.ConfirmPassword), "تکرار رمز عبور با رمز جدید یکسان نیست.");

        if (!ModelState.IsValid)
            return View(model);

        if (!int.TryParse(User.FindFirst("AdminId")?.Value, out var adminId))
            return Forbid();

        var admin = await db.AdminUsers.FindAsync(adminId);
        if (admin is null || !admin.IsActive)
            return Forbid();

        if (!admins.VerifyPassword(admin, model.CurrentPassword ?? ""))
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "رمز عبور فعلی صحیح نیست.");
            return View(model);
        }

        await admins.ChangePasswordAsync(admin, model.NewPassword!);
        await audit.WriteAsync("تغییر رمز عبور مدیر", "AdminUser", admin.Id.ToString(), admin.Username);

        return RedirectToAction(nameof(Index), "Home");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Denied() => View();

    private static int GetRemainingSeconds(DateTime? expiresAt)
    {
        if (expiresAt is null)
            return 0;

        var seconds = (expiresAt.Value - DateTime.UtcNow).TotalSeconds;
        return seconds <= 0 ? 0 : (int)Math.Ceiling(seconds);
    }

    public sealed class LoginVm
    {
        public string? Mode { get; set; }
        public string? PersonnelNumber { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? ReturnUrl { get; set; }
    }

    public sealed class ChangePasswordVm
    {
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
        public string? ConfirmPassword { get; set; }
    }
}