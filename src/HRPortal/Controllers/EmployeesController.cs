using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Controllers;
public class EmployeesController(HRPortalDbContext db,EmployeeExcelService excel,AuditService audit):Controller
{
 public async Task<IActionResult> Index(string? q,string? status,string sort="PersonnelNumber",string dir="asc")
 {
  var query=db.Employees.Include(x=>x.OrganizationUnit).Include(x=>x.OrganizationDepartment).Include(x=>x.OrganizationSection).AsQueryable();
  if(!string.IsNullOrWhiteSpace(q))query=query.Where(x=>x.PersonnelNumber.Contains(q)||x.NationalId.Contains(q)||x.FirstName.Contains(q)||x.LastName.Contains(q)||x.Mobile.Contains(q));
  if(!string.IsNullOrWhiteSpace(status))query=query.Where(x=>x.Status==status);
  query=sort switch{"LastName"=>(dir=="desc"?query.OrderByDescending(x=>x.LastName):query.OrderBy(x=>x.LastName)),"Status"=>(dir=="desc"?query.OrderByDescending(x=>x.Status):query.OrderBy(x=>x.Status)),_=>dir=="desc"?query.OrderByDescending(x=>x.PersonnelNumber):query.OrderBy(x=>x.PersonnelNumber)};
  ViewBag.Q=q;ViewBag.Status=status;ViewBag.Sort=sort;ViewBag.Dir=dir;return View(await query.ToListAsync());
 }
 public async Task<IActionResult> Create()=>View("Form",await FormVm(new Employee()));
 [HttpPost][ValidateAntiForgeryToken]public async Task<IActionResult> Create(FormVm m){if(!ModelState.IsValid)return View("Form",await FormVm(m.Employee));m.Employee.IsSystemUser=true;db.Employees.Add(m.Employee);await db.SaveChangesAsync();await audit.WriteAsync("ایجاد کارمند","Employee",m.Employee.Id.ToString(),m.Employee.PersonnelNumber);return RedirectToAction(nameof(Index));}
 public async Task<IActionResult> Edit(int id){var e=await db.Employees.FindAsync(id);return e==null?NotFound():View("Form",await FormVm(e));}
 [HttpPost][ValidateAntiForgeryToken]public async Task<IActionResult> Edit(FormVm m){if(!ModelState.IsValid)return View("Form",await FormVm(m.Employee));var e=await db.Employees.FindAsync(m.Employee.Id);if(e==null)return NotFound();db.Entry(e).CurrentValues.SetValues(m.Employee);e.IsSystemUser=true;e.UpdatedAt=DateTime.UtcNow;await db.SaveChangesAsync();await audit.WriteAsync("ویرایش کارمند","Employee",e.Id.ToString(),e.PersonnelNumber);return RedirectToAction(nameof(Index));}
 [HttpPost][ValidateAntiForgeryToken]public async Task<IActionResult> Deactivate(int id){var e=await db.Employees.FindAsync(id);if(e!=null){e.Status="غیرفعال";e.IsSystemUser=false;await db.SaveChangesAsync();await audit.WriteAsync("غیرفعال‌سازی کارمند","Employee",id.ToString());}return RedirectToAction(nameof(Index));}
 [HttpPost][ValidateAntiForgeryToken]public async Task<IActionResult> Delete(int id){var e=await db.Employees.FindAsync(id);if(e!=null){if(await db.OtpChallenges.AnyAsync(x=>x.EmployeeId==id)||await db.AuditLogs.AnyAsync(x=>x.EmployeeId==id)){TempData["Error"]="این کارمند دارای سوابق سامانه است و حذف مستقیم مجاز نیست؛ او را غیرفعال کنید.";}else{db.Employees.Remove(e);await db.SaveChangesAsync();}}return RedirectToAction(nameof(Index));}
 [HttpGet]public async Task<IActionResult> Export(string? q,string? status){var query=db.Employees.AsQueryable();if(!string.IsNullOrWhiteSpace(q))query=query.Where(x=>x.PersonnelNumber.Contains(q)||x.NationalId.Contains(q)||x.FirstName.Contains(q)||x.LastName.Contains(q)||x.Mobile.Contains(q));if(!string.IsNullOrWhiteSpace(status))query=query.Where(x=>x.Status==status);var bytes=await excel.ExportAsync(query);return File(bytes,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"Employees-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");}
 [HttpGet]public IActionResult Import()=>View();
 [HttpPost][ValidateAntiForgeryToken]public async Task<IActionResult> Import(IFormFile file){if(file==null||file.Length==0){ModelState.AddModelError("","فایل اکسل را انتخاب کنید.");return View();}try{var errors=await excel.ImportAsync(file.OpenReadStream());ViewBag.Errors=errors;ViewBag.Success=errors.Count==0?"اطلاعات با موفقیت وارد شد.":"فایل پردازش شد و موارد خطادار گزارش شده‌اند.";}catch(Exception ex){ViewBag.Errors=new List<string>{"خطای فایل: "+ex.Message};}return View();}
 private async Task<FormVm> FormVm(Employee e){var r=await db.OrganizationStructureRevisions.Include(x=>x.Nodes).Where(x=>x.IsFinalized&&x.EffectiveDate<=DateTime.Today).OrderByDescending(x=>x.EffectiveDate).FirstOrDefaultAsync();var nodes=r?.Nodes.Where(x=>x.IsActive).OrderBy(x=>x.SortOrder).ThenBy(x=>x.Title).ToList()??new();return new FormVm{Employee=e,Units=nodes.Where(x=>x.RankType=="مدیریت").ToList(),Departments=nodes.Where(x=>x.RankType=="ریاست").ToList(),Sections=nodes.Where(x=>x.RankType=="سرپرستی").ToList()};}
 public class FormVm{public Employee Employee{get;set;}=new();public List<OrganizationNode> Units{get;set;}=[];public List<OrganizationNode> Departments{get;set;}=[];public List<OrganizationNode> Sections{get;set;}=[];}
}