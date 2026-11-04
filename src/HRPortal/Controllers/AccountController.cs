using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace HRPortal.Controllers;

public class AccountController(HRPortalDbContext db, OtpService otp, AuditService audit, AdminService admins, RoleService roleService, TemporaryReportImageStore reportImageStore) : Controller
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

        if (!result.Success)
        {
            HttpContext.Session.Remove("PendingPersonnel");
            HttpContext.Session.Remove("PendingReturnUrl");
            ModelState.AddModelError("", result.Message);
            return View("Login", model);
        }

        return RedirectToAction(nameof(Verify));
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> EmployeePasswordLogin(LoginVm model)
    {
        model.Mode = "employee-password";

        if (string.IsNullOrWhiteSpace(model.PersonnelNumber) ||
            string.IsNullOrEmpty(model.Password))
        {
            ModelState.AddModelError("", "نام کاربری و رمز عبور شخصی الزامی است.");
            return View("Login", model);
        }

        var personnelNumber = model.PersonnelNumber.Trim();
        var employee = await db.Employees.SingleOrDefaultAsync(x =>
            x.PersonnelNumber == personnelNumber &&
            x.IsSystemUser &&
            x.Status == "فعال");

        if (employee is null)
        {
            await audit.WriteAsync("ورود ناموفق کارکنان با رمز شخصی", "Account", personnelNumber, "کارمند فعال یافت نشد.");
            ModelState.AddModelError("", "کارمند فعال با این نام کاربری یافت نشد.");
            return View("Login", model);
        }

        // Lockout state is persisted on Employees so it survives IIS recycles and
        // is shared by every application instance connected to the same SQL database.
        var now = DateTime.UtcNow;
        if (PasswordLoginLockoutPolicy.IsLocked(employee.PasswordLoginLockedUntilUtc, now))
        {
            var remaining = PasswordLoginLockoutPolicy.GetRemainingSeconds(employee.PasswordLoginLockedUntilUtc, now);
            ModelState.AddModelError("", $"ورود با رمز این حساب به دلیل پنج تلاش ناموفق قفل شده است. حدود {Math.Max(1, (int)Math.Ceiling(remaining / 60d))} دقیقه دیگر می‌توانید دوباره تلاش کنید؛ ورود با رمز یکبارمصرف همچنان فعال است.");
            return View("Login", model);
        }

        // The first attempt after an expired lockout starts a fresh 15-minute window.
        if (employee.PasswordLoginLockedUntilUtc.HasValue &&
            employee.PasswordLoginLockedUntilUtc.Value <= now)
        {
            employee.PasswordLoginFailedAttempts = 0;
            employee.PasswordLoginLockedUntilUtc = null;
            employee.PasswordLoginLastFailedAtUtc = null;
        }

        // Legacy employees with no personal password get a one-time national-ID initial
        // credential. Never change an existing personal password, and require a reset.
        var hasSavedPassword = !string.IsNullOrWhiteSpace(employee.PersonalPasswordHash);
        var bootstrap = !hasSavedPassword;
        var valid = hasSavedPassword
            ? PasswordHasher.Verify(model.Password, employee.PersonalPasswordHash!)
            : EqualFixedTime(model.Password, employee.NationalId);
        if (!valid)
        {
            await db.Employees
                .Where(x => x.Id == employee.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PasswordLoginFailedAttempts,
                        x => x.PasswordLoginLockedUntilUtc.HasValue && x.PasswordLoginLockedUntilUtc <= now
                            ? 1
                            : x.PasswordLoginFailedAttempts + 1)
                    .SetProperty(x => x.PasswordLoginLockedUntilUtc,
                        x => x.PasswordLoginLockedUntilUtc.HasValue && x.PasswordLoginLockedUntilUtc <= now
                            ? (DateTime?)null
                            : x.PasswordLoginLockedUntilUtc.HasValue && x.PasswordLoginLockedUntilUtc > now
                                ? x.PasswordLoginLockedUntilUtc
                                : x.PasswordLoginFailedAttempts >= PasswordLoginLockoutPolicy.MaximumFailedAttempts - 1
                                    ? now.AddMinutes(PasswordLoginLockoutPolicy.LockoutDuration.TotalMinutes)
                                    : null)
                    .SetProperty(x => x.PasswordLoginLastFailedAtUtc, now)
                    .SetProperty(x => x.UpdatedAt, now));

            await audit.WriteAsync("ورود ناموفق کارکنان با رمز شخصی", "Account", employee.Id.ToString(), "نام کاربری یا رمز عبور نادرست است.", employee.Id);
            var updatedAttempts = await db.Employees.AsNoTracking()
                .Where(x => x.Id == employee.Id)
                .Select(x => new { x.PasswordLoginFailedAttempts, x.PasswordLoginLockedUntilUtc })
                .SingleAsync();
            if (PasswordLoginLockoutPolicy.IsLocked(updatedAttempts.PasswordLoginLockedUntilUtc, now))
                ModelState.AddModelError("", "به دلیل پنج تلاش ناموفق، ورود با رمز این حساب برای ۱۵ دقیقه قفل شد. ورود با رمز یکبارمصرف همچنان فعال است.");
            else
                ModelState.AddModelError("", "نام کاربری یا رمز عبور صحیح نیست.");
            return View("Login", model);
        }

        await db.Employees.Where(x => x.Id == employee.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.PasswordLoginFailedAttempts, 0)
            .SetProperty(x => x.PasswordLoginLockedUntilUtc, (DateTime?)null)
            .SetProperty(x => x.PasswordLoginLastFailedAtUtc, (DateTime?)null)
            .SetProperty(x => x.UpdatedAt, now));
        employee.PasswordLoginFailedAttempts = 0;
        employee.PasswordLoginLockedUntilUtc = null;
        employee.PasswordLoginLastFailedAtUtc = null;
        if (bootstrap)
        {
            employee.PersonalPasswordHash = PasswordHasher.Hash(model.Password);
            employee.MustChangeInitialPassword = true;
            employee.UpdatedAt = now;
            await db.SaveChangesAsync();
        }

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.Name, $"{employee.FirstName} {employee.LastName}"),
            new("EmployeeId", employee.Id.ToString()),
            new("PersonnelNumber", employee.PersonnelNumber),
            new("IsAdmin", "0"),
            new("UserType", "Employee"),
            new("IsPerformanceEvaluator", employee.IsPerformanceEvaluator ? "1" : "0"),
            new("LoginMethod", "Password"),
            new("MustChangeInitialPassword", employee.MustChangeInitialPassword ? "1" : "0")
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

        await audit.WriteAsync("ورود موفق کارمند با رمز شخصی", "Account", employee.Id.ToString(), employee.PersonnelNumber, employee.Id);

        if (employee.MustChangeInitialPassword)
            return RedirectToAction(nameof(PersonalPassword));
        // Evaluators still enter their dashboard when no period is active, to see
        // the closed-period warning and their own historical evaluations.
        var canEvaluate = employee.IsPerformanceEvaluator && permissions.Contains("MonthlyPerformance.Evaluate");
        var landing = canEvaluate ? "/MonthlyPerformance/Dashboard" : "/MonthlyPerformance/Mine";
        return Redirect(!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : landing);
    }

    [HttpGet, AllowAnonymous]
    public IActionResult AdminLogin(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true && User.HasClaim("IsAdmin", "1"))
            return RedirectToAction("Index", "Home");

        return View(new LoginVm { ReturnUrl = returnUrl, Mode = "admin" });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> AdminLogin(LoginVm model)
    {
        model.Mode = "admin";

        if (string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError("", "نام کاربری و رمز عبور مدیر الزامی است.");
            return View("AdminLogin", model);
        }

        var admin = await admins.FindAsync(model.Username);

        if (admin is null || !admins.VerifyPassword(admin, model.Password))
        {
            await audit.WriteAsync("ورود ناموفق مدیر", "AdminUser", model.Username, "نام کاربری یا رمز عبور نادرست است.");
            ModelState.AddModelError("", "نام کاربری یا رمز عبور صحیح نیست.");
            return View("AdminLogin", model);
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
    public async Task<IActionResult> Verify()
    {
        var personnel = HttpContext.Session.GetString("PendingPersonnel");
        if (string.IsNullOrWhiteSpace(personnel))
            return RedirectToAction(nameof(Login));

        var challenge = await db.OtpChallenges.AsNoTracking()
            .Where(x => x.PersonnelNumber == personnel && !x.IsConsumed)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        var remaining = GetRemainingSeconds(challenge?.ExpiresAt);
        if (remaining <= 0)
        {
            HttpContext.Session.Remove("PendingPersonnel");
            HttpContext.Session.Remove("PendingReturnUrl");
            TempData["Error"] = "زمان اعتبار کد یکبارمصرف به پایان رسیده است. دوباره درخواست کد کنید.";
            return RedirectToAction(nameof(Login));
        }

        var otpSettings = await db.OtpSettings.AsNoTracking().FirstOrDefaultAsync() ?? new OtpSettings();
        ViewBag.PersonnelNumber = personnel;
        ViewBag.OtpRemainingSeconds = remaining;
        ViewBag.OtpLength = Math.Clamp(otpSettings.Length, 4, 9);
        return View();
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(string code)
    {
        var personnelNumber = HttpContext.Session.GetString("PendingPersonnel");
        var returnUrl = HttpContext.Session.GetString("PendingReturnUrl");

        if (string.IsNullOrWhiteSpace(personnelNumber))
            return RedirectToAction(nameof(Login));

        var challenge = await db.OtpChallenges.AsNoTracking()
            .Where(x => x.PersonnelNumber == personnelNumber && !x.IsConsumed)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (challenge is null || challenge.ExpiresAt <= DateTime.UtcNow)
        {
            HttpContext.Session.Remove("PendingPersonnel");
            HttpContext.Session.Remove("PendingReturnUrl");
            TempData["Error"] = "زمان اعتبار کد یکبارمصرف به پایان رسیده است. دوباره درخواست کد کنید.";
            return RedirectToAction(nameof(Login));
        }

        if (!await otp.VerifyAsync(personnelNumber, code))
        {
            ModelState.AddModelError("", "کد واردشده نادرست یا بیش از حد مجاز تلاش شده است.");
            var otpSettings = await db.OtpSettings.AsNoTracking().FirstOrDefaultAsync() ?? new OtpSettings();
            ViewBag.PersonnelNumber = personnelNumber;
            ViewBag.OtpRemainingSeconds = GetRemainingSeconds(challenge.ExpiresAt);
            ViewBag.OtpLength = Math.Clamp(otpSettings.Length, 4, 9);
            return View();
        }

        var employee = await db.Employees.SingleAsync(x => x.PersonnelNumber == personnelNumber);

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.Name, $"{employee.FirstName} {employee.LastName}"),
            new("EmployeeId", employee.Id.ToString()),
            new("PersonnelNumber", employee.PersonnelNumber),
            new("IsAdmin", "0"),
            new("UserType", "Employee"),
            new("IsPerformanceEvaluator", employee.IsPerformanceEvaluator ? "1" : "0"),
            new("LoginMethod", "Otp")
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
        // Evaluators still enter their dashboard when no period is active, to see
        // the closed-period warning and their own historical evaluations.
        var canEvaluate = employee.IsPerformanceEvaluator && permissions.Contains("MonthlyPerformance.Evaluate");
        var landing = canEvaluate ? "/MonthlyPerformance/Dashboard" : "/MonthlyPerformance/Mine";
        return Redirect(!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : landing);
    }

    [HttpGet, Authorize]
    public async Task<IActionResult> PersonalPassword()
    {
        var employee = await GetAuthenticatedEmployeeAsync();
        if (employee is null) return Forbid();

        var hasExistingPassword = !string.IsNullOrEmpty(employee.PersonalPasswordHash);
        var isOtpSession = string.Equals(
            User.FindFirst("LoginMethod")?.Value,
            "Otp",
            StringComparison.OrdinalIgnoreCase);

        return View(new PersonalPasswordVm
        {
            HasExistingPassword = hasExistingPassword,
            CurrentPasswordRequired = hasExistingPassword && !isOtpSession && !employee.MustChangeInitialPassword
        });
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> PersonalPassword(
        string? currentPassword,
        string? newPassword,
        string? confirmPassword)
    {
        var employee = await GetAuthenticatedEmployeeAsync();
        if (employee is null) return Forbid();

        var hasExistingPassword = !string.IsNullOrEmpty(employee.PersonalPasswordHash);
        var isOtpSession = string.Equals(
            User.FindFirst("LoginMethod")?.Value,
            "Otp",
            StringComparison.OrdinalIgnoreCase);

        // Password fields are intentionally handled as raw strings instead of
        // binding to a validation model. The only password-format rule is:
        // at least six characters; no composition, trimming, or normalization.
        ModelState.Clear();

        var suppliedNewPassword = newPassword ?? string.Empty;
        var suppliedConfirmation = confirmPassword ?? string.Empty;
        var suppliedCurrentPassword = currentPassword ?? string.Empty;

        var passwordValid = PasswordHasher.IsValidPersonalPassword(suppliedNewPassword);
        if (!passwordValid)
        {
            ModelState.AddModelError(
                "NewPassword",
                "رمز عبور شخصی باید حداقل ۶ کاراکتر داشته باشد.");
        }

        if (!string.Equals(suppliedNewPassword, suppliedConfirmation, StringComparison.Ordinal))
        {
            ModelState.AddModelError(
                "ConfirmPassword",
                "تکرار رمز عبور با رمز جدید یکسان نیست.");
        }

        // Only a password-login session must prove the current personal
        // password. An OTP-authenticated session is trusted for a password
        // set/change, so the current password is not requested.
        var currentPasswordValid = true;
        if (passwordValid &&
            string.Equals(suppliedNewPassword, suppliedConfirmation, StringComparison.Ordinal) &&
            hasExistingPassword &&
            !isOtpSession && !employee.MustChangeInitialPassword)
        {
            currentPasswordValid = PasswordHasher.Verify(
                suppliedCurrentPassword,
                employee.PersonalPasswordHash!);

            if (!currentPasswordValid)
            {
                ModelState.AddModelError(
                    "CurrentPassword",
                    "رمز عبور فعلی صحیح نیست. برای تغییر رمز می‌توانید با رمز یکبارمصرف وارد شوید.");
            }
        }

        var currentPasswordRequired = hasExistingPassword && !isOtpSession && !employee.MustChangeInitialPassword;
        if (!ModelState.IsValid)
        {
            // Never return password values back to the browser.
            return View(new PersonalPasswordVm
            {
                HasExistingPassword = hasExistingPassword,
                CurrentPasswordRequired = currentPasswordRequired
            });
        }

        if (string.Equals(suppliedNewPassword, employee.NationalId, StringComparison.Ordinal))
        {
            ModelState.AddModelError("NewPassword", "رمز عبور جدید نباید همان کد ملی شما باشد.");
            return View(new PersonalPasswordVm { HasExistingPassword = hasExistingPassword, CurrentPasswordRequired = currentPasswordRequired });
        }
        var hadInitialPassword = employee.MustChangeInitialPassword;
        employee.PersonalPasswordHash = PasswordHasher.Hash(suppliedNewPassword);
        employee.MustChangeInitialPassword = false;
        employee.PasswordLoginFailedAttempts = 0;
        employee.PasswordLoginLockedUntilUtc = null;
        employee.PasswordLoginLastFailedAtUtc = null;
        employee.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await audit.WriteAsync(
            hasExistingPassword
                ? "تغییر رمز عبور شخصی کارمند"
                : "تعیین رمز عبور شخصی کارمند",
            "Employee",
            employee.Id.ToString(),
            employee.PersonnelNumber,
            employee.Id);

        TempData["Success"] = hasExistingPassword
            ? "رمز عبور شخصی شما با موفقیت تغییر کرد."
            : "رمز عبور شخصی شما با موفقیت تعیین شد.";

        if (hadInitialPassword)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Success"] = "رمز شخصی شما ثبت شد. اکنون دوباره با رمز جدید وارد شوید.";
            return RedirectToAction(nameof(Login));
        }
        return RedirectToAction("Index", "Home");
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
        if (User.Identity?.IsAuthenticated == true)
        {
            try { reportImageStore.DeleteOwner(ReportTempOwner.GetKey(User)); }
            catch (InvalidOperationException) { }
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Denied() => View();

    private async Task<Employee?> GetAuthenticatedEmployeeAsync()
    {
        if (User.HasClaim("IsAdmin", "1") || !int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return null;
        return await db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId && x.IsSystemUser && x.Status == "فعال");
    }

    private static int GetRemainingSeconds(DateTime? expiresAt)
    {
        if (expiresAt is null)
            return 0;

        var seconds = (expiresAt.Value - DateTime.UtcNow).TotalSeconds;
        return seconds <= 0 ? 0 : (int)Math.Ceiling(seconds);
    }

    private static bool EqualFixedTime(string candidate, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(candidate));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public sealed class LoginVm
    {
        public string? Mode { get; set; }
        public string? PersonnelNumber { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? ReturnUrl { get; set; }
    }

    public sealed class PersonalPasswordVm
    {
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
        public string? ConfirmPassword { get; set; }
        public bool HasExistingPassword { get; set; }
        public bool CurrentPasswordRequired { get; set; }
    }

    public sealed class ChangePasswordVm
    {
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
        public string? ConfirmPassword { get; set; }
    }
}