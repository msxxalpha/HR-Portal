using HRPortal.Data;
using HRPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

public class AccountController(HRPortalDbContext db, OtpService otp, AuditService audit) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect("/");

        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm model)
    {
        if (string.IsNullOrWhiteSpace(model.PersonnelNumber))
        {
            ModelState.AddModelError("", "شماره پرسنلی الزامی است.");
            return View(model);
        }

        var personnelNumber = model.PersonnelNumber.Trim();

        var employee = await db.Employees
            .SingleOrDefaultAsync(x =>
                x.PersonnelNumber == personnelNumber &&
                x.IsSystemUser &&
                x.Status == "فعال");

        if (employee is null)
        {
            await audit.WriteAsync(
                "ورود ناموفق",
                "Account",
                personnelNumber,
                "کارمند فعال یافت نشد.");

            ModelState.AddModelError("", "کارمند فعال با این شماره پرسنلی یافت نشد.");
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(employee.Mobile))
        {
            ModelState.AddModelError("", "شماره همراه کارمند ثبت نشده است.");
            return View(model);
        }

        var result = await otp.IssueAsync(employee);

        HttpContext.Session.SetString("PendingPersonnel", employee.PersonnelNumber);

        if (result.DebugCode is not null)
            TempData["DebugOtp"] = result.DebugCode;

        if (!result.Success)
            TempData["OtpError"] = result.Message;

        return RedirectToAction(
            nameof(Verify),
            new { returnUrl = model.ReturnUrl });
    }

    [HttpGet]
    public IActionResult Verify(string? returnUrl = null)
    {
        var personnelNumber = HttpContext.Session.GetString("PendingPersonnel");

        if (string.IsNullOrWhiteSpace(personnelNumber))
            return RedirectToAction(nameof(Login));

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(string code, string? returnUrl)
    {
        var personnelNumber = HttpContext.Session.GetString("PendingPersonnel");

        if (string.IsNullOrWhiteSpace(personnelNumber))
            return RedirectToAction(nameof(Login));

        if (!await otp.VerifyAsync(personnelNumber, code))
        {
            ModelState.AddModelError(
                "",
                "کد واردشده نادرست، منقضی یا بیش از حد مجاز تلاش شده است.");

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        var employee = await db.Employees
            .SingleAsync(x => x.PersonnelNumber == personnelNumber);

        var claims = new List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.Name,
                $"{employee.FirstName} {employee.LastName}"),

            new System.Security.Claims.Claim(
                "EmployeeId",
                employee.Id.ToString()),

            new System.Security.Claims.Claim(
                "PersonnelNumber",
                employee.PersonnelNumber),

            new System.Security.Claims.Claim(
                "IsAdmin",
                employee.IsSystemAdministrator ? "1" : "0")
        };

        var identity = new System.Security.Claims.ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal);

        HttpContext.Session.Remove("PendingPersonnel");

        await audit.WriteAsync(
            "ورود موفق",
            "Account",
            employee.Id.ToString(),
            employee.PersonnelNumber,
            employee.Id);

        return Redirect(
            !string.IsNullOrWhiteSpace(returnUrl) &&
            Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Denied() => View();

    public sealed class LoginVm
    {
        public string? PersonnelNumber { get; set; }
        public string? ReturnUrl { get; set; }
    }
}