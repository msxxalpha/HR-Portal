using System.Security.Claims;
using HRPortal.Data;
using HRPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Controllers;
public class AccountController(HRPortalDbContext db,OtpService otp,AuditService audit):Controller
{
 [HttpGet]public IActionResult Login(string? returnUrl=null)=>User.Identity?.IsAuthenticated==true?Redirect("/"):View(new LoginVm{returnUrl=returnUrl});
 [HttpPost][ValidateAntiForgeryToken]public async Task<IActionResult> Login(LoginVm m)
 {
  if(string.IsNullOrWhiteSpace(m.PersonnelNumber)){ModelState.AddModelError("","شماره پرسنلی الزامی است.");return View(m);}
  var e=await db.Employees.SingleOrDefaultAsync(x=>x.PersonnelNumber==m.PersonnelNumber.Trim()&&x.IsSystemUser&&x.Status=="فعال");
  if(e==null){await audit.WriteAsync("ورود ناموفق","Account",m.PersonnelNumber,"کارمند فعال یافت نشد.");ModelState.AddModelError("","کارمند فعال با این شماره پرسنلی یافت نشد.");return View(m);}
  if(string.IsNullOrWhiteSpace(e.Mobile)){ModelState.AddModelError("","شماره همراه کارمند ثبت نشده است.");return View(m);}
  var result=await otp.IssueAsync(e);HttpContext.Session.SetString("PendingPersonnel",e.PersonnelNumber);if(result.DebugCode!=null)TempData["DebugOtp"]=result.DebugCode;if(!result.Success)TempData["OtpError"]=result.Message;return RedirectToAction(nameof(Verify),new{returnUrl=m.returnUrl});
 }
 [HttpGet]public IActionResult Verify(string? returnUrl=null){var p=HttpContext.Session.GetString("PendingPersonnel");if(string.IsNullOrWhiteSpace(p))return RedirectToAction(nameof(Login));ViewBag.ReturnUrl=returnUrl;return View();}
 [HttpPost][ValidateAntiForgeryToken]public async Task<IActionResult> Verify(string code,string? returnUrl)
 {
  var p=HttpContext.Session.GetString("PendingPersonnel");if(string.IsNullOrWhiteSpace(p))return RedirectToAction(nameof(Login));
  if(!await otp.VerifyAsync(p,code)){ModelState.AddModelError("","کد واردشده نادرست، منقضی یا بیش از حد مجاز تلاش شده است.");ViewBag.ReturnUrl=returnUrl;return View();}
  var e=await db.Employees.SingleAsync(x=>x.PersonnelNumber==p);var claims=new List<Claim>{new(ClaimTypes.Name,$"{e.FirstName} {e.LastName}"),new("EmployeeId",e.Id.ToString()),new("PersonnelNumber",e.PersonnelNumber),new("IsAdmin",e.IsSystemAdministrator?"1":"0")};
  await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme)));
  HttpContext.Session.Remove("PendingPersonnel");await audit.WriteAsync("ورود موفق","Account",e.Id.ToString(),e.PersonnelNumber,e.Id);return Redirect(!string.IsNullOrWhiteSpace(returnUrl)&&Url.IsLocalUrl(returnUrl)?returnUrl:"/");
 }
 [HttpPost][ValidateAntiForgeryToken]public async Task<IActionResult> Logout(){await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);return RedirectToAction(nameof(Login));}
 public IActionResult Denied()=>View();
 public class LoginVm{public string? PersonnelNumber{get;set;}public string? returnUrl{get;set;}}
}