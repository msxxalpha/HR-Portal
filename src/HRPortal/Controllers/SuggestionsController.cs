using System.Text.Json;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public class SuggestionsController(HRPortalDbContext db, IWebHostEnvironment env) : Controller
{
    private static readonly string[] Categories = ["بهبود فرایند و روش انجام کار","کاهش هزینه و صرفه‌جویی","افزایش بهره‌وری و کاهش زمان","ارتقای کیفیت محصول یا خدمت","ایمنی، بهداشت و محیط زیست","نگهداری و تعمیرات","فناوری اطلاعات و تحول دیجیتال","منابع انسانی و امور اداری","انرژی و منابع","سایر"];
    private static readonly string[] Benefits = ["کاهش هزینه","افزایش بهره‌وری","کاهش زمان","ارتقای کیفیت","کاهش ضایعات","افزایش ایمنی","رضایت کارکنان","رضایت مشتری","شفافیت و نظم اطلاعات","حفاظت از محیط زیست","سایر"];
    private static readonly string[] Resources = ["بدون هزینه یا منبع اضافی","نیروی انسانی","تجهیزات و ماشین‌آلات","نرم‌افزار و فناوری اطلاعات","مواد و ملزومات","بودجه مالی","آموزش","سایر"];
    private bool IsAdmin => User.HasClaim("IsAdmin", "1");
    private bool Has(string code) => IsAdmin || User.HasClaim("Permission", code);
    private bool CanManage => Has("Suggestions.Manage") || Has("Suggestions.ViewAll");
    private bool CanSubmit => Has("Suggestions.Submit") || CanManage;
    private bool TryEmployeeId(out int id) => int.TryParse(User.FindFirst("EmployeeId")?.Value, out id);

    private void SetLists()
    {
        ViewBag.Categories = Categories; ViewBag.Benefits = Benefits; ViewBag.Resources = Resources;
        ViewBag.CanManageSuggestions = CanManage;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!CanSubmit) return Forbid();
        IQueryable<Suggestion> query = db.Suggestions.AsNoTracking();
        if (!CanManage)
        {
            if (!TryEmployeeId(out var employeeId)) return Forbid();
            query = query.Where(x => x.ProposerEmployeeId == employeeId);
        }
        return View(await query.OrderByDescending(x => x.UpdatedAtUtc).Take(300).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? id, int step = 1)
    {
        if (!CanSubmit) return Forbid();
        var model = new SuggestionFormVm();
        if (id.HasValue)
        {
            var query = db.Suggestions.Include(x => x.Members).Include(x => x.Attachments).AsQueryable();
            if (!CanManage)
            {
                if (!TryEmployeeId(out var owner)) return Forbid();
                query = query.Where(x => x.ProposerEmployeeId == owner);
            }
            var item = await query.FirstOrDefaultAsync(x => x.Id == id.Value);
            if (item is null) return NotFound();
            if (item.Status != "Draft") return RedirectToAction(nameof(Details), new { id });
            model = new SuggestionFormVm {
                Id=item.Id, ProposerEmployeeId=item.ProposerEmployeeId, Title=item.Title, Category=item.Category,
                RelatedArea=item.RelatedArea, ShortDescription=item.ShortDescription, ProblemDescription=item.ProblemDescription,
                ProposedSolution=item.ProposedSolution, Benefits=JsonSerializer.Deserialize<List<string>>(item.BenefitsJson) ?? [],
                BenefitDetails=item.BenefitDetails, ImplementationScope=item.ImplementationScope, ImplementationMethod=item.ImplementationMethod,
                ResourceTypes=JsonSerializer.Deserialize<List<string>>(item.ResourceTypesJson) ?? [], ResourceDetails=item.ResourceDetails,
                SelectedEmployeeIds=item.Members.Select(z=>z.EmployeeId).ToList(), Attachments=item.Attachments.ToList()
            };
        }
        else if (!CanManage && TryEmployeeId(out var employeeId))
            model.ProposerEmployeeId = employeeId;

        model.CurrentStep = Math.Clamp(step, 1, 2);
        model.EmployeeOptions = await Options(model.SelectedEmployeeIds, model.ProposerEmployeeId);
        await SetProposerViewAsync(model.ProposerEmployeeId);
        SetLists();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> SearchEmployees(string? term)
    {
        if (!CanSubmit) return Forbid();
        var query = db.Employees.AsNoTracking().Where(x => x.Status == "فعال" && x.IsSystemUser);
        if (!CanManage && TryEmployeeId(out var actor)) query = query.Where(x => x.Id != actor);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var value = term.Trim();
            query = query.Where(x => x.FirstName.Contains(value) || x.LastName.Contains(value) ||
                x.PersonnelNumber.Contains(value) || (x.FirstName + " " + x.LastName).Contains(value));
        }
        return Json(await query.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).Take(20)
            .Select(x => new { id=x.Id, name=x.FirstName+" "+x.LastName, personnelNumber=x.PersonnelNumber,
                organization=x.OrganizationUnit != null ? x.OrganizationUnit.Title :
                    x.OrganizationDepartment != null ? x.OrganizationDepartment.Title : "—" }).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var query = db.Suggestions.AsNoTracking().Include(x=>x.Members).Include(x=>x.Attachments).AsQueryable();
        if (!CanManage)
        {
            if (!TryEmployeeId(out var owner)) return Forbid();
            query = query.Where(x=>x.ProposerEmployeeId==owner);
        }
        var item=await query.FirstOrDefaultAsync(x=>x.Id==id);
        return item is null ? NotFound() : View(item);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> Save(SuggestionFormVm model, bool submit, List<IFormFile>? files)
    {
        if (!CanSubmit) return Forbid();
        var elevated=CanManage;
        var proposerId = elevated ? model.ProposerEmployeeId :
            (TryEmployeeId(out var employeeId) ? employeeId : 0);
        var proposerIsActive=await db.Employees.AnyAsync(x=>x.Id==proposerId && x.Status=="فعال" && x.IsSystemUser);
        if(!proposerIsActive) ModelState.AddModelError(nameof(model.ProposerEmployeeId),"پیشنهاددهنده باید از کارکنان فعال سامانه انتخاب شود.");

        Suggestion? item=null;
        if(model.Id.HasValue)
        {
            var existingQuery=db.Suggestions.Include(x=>x.Members).Include(x=>x.Attachments).AsQueryable();
            if(!elevated) existingQuery=existingQuery.Where(x=>x.ProposerEmployeeId==proposerId);
            item=await existingQuery.FirstOrDefaultAsync(x=>x.Id==model.Id.Value);
            if(item is null) return NotFound();
            if(item.Status!="Draft") return Forbid();
            proposerId=item.ProposerEmployeeId;
        }
        else
        {
            item=new Suggestion { ProposerEmployeeId=proposerId, SuggestionNumber="D-"+Guid.NewGuid().ToString("N")[..20] };
            db.Suggestions.Add(item);
        }

        var requested=(model.SelectedEmployeeIds??[]).Where(x=>x>0 && x!=proposerId).Distinct().ToList();
        if(requested.Count>4) ModelState.AddModelError(nameof(model.SelectedEmployeeIds),"حداکثر چهار همکار علاوه بر پیشنهاددهنده قابل انتخاب است.");
        var members=await db.Employees.AsNoTracking().Where(x=>requested.Contains(x.Id) && x.Status=="فعال" && x.IsSystemUser)
            .Select(x=>new{x.Id,x.FirstName,x.LastName,x.PersonnelNumber}).ToListAsync();
        if(members.Count!=requested.Count) ModelState.AddModelError(nameof(model.SelectedEmployeeIds),"فقط کارکنان فعال قابل انتخاب هستند.");

        if(submit)
        {
            if(string.IsNullOrWhiteSpace(model.Title)||model.Title.Trim().Length>200) ModelState.AddModelError(nameof(model.Title),"عنوان الزامی و حداکثر ۲۰۰ نویسه است.");
            if(!Categories.Contains(model.Category)) ModelState.AddModelError(nameof(model.Category),"دسته‌بندی معتبر انتخاب کنید.");
            if(string.IsNullOrWhiteSpace(model.ShortDescription)||model.ShortDescription.Length>500) ModelState.AddModelError(nameof(model.ShortDescription),"شرح کوتاه الزامی و حداکثر ۵۰۰ نویسه است.");
            if(string.IsNullOrWhiteSpace(model.ProblemDescription)) ModelState.AddModelError(nameof(model.ProblemDescription),"شرح مسئله الزامی است.");
            if(string.IsNullOrWhiteSpace(model.ProposedSolution)) ModelState.AddModelError(nameof(model.ProposedSolution),"راه‌حل پیشنهادی الزامی است.");
        }

        if(!ModelState.IsValid)
        {
            model.ProposerEmployeeId=proposerId; model.CurrentStep=submit?2:1;
            model.EmployeeOptions=await Options(requested,proposerId);
            await SetProposerViewAsync(proposerId); SetLists();
            return View("Create",model);
        }

        item.ProposerEmployeeId=proposerId; item.Title=(model.Title??"").Trim(); item.Category=model.Category??"";
        item.RelatedArea=model.RelatedArea?.Trim(); item.ShortDescription=model.ShortDescription??"";
        item.ProblemDescription=model.ProblemDescription??""; item.ProposedSolution=model.ProposedSolution??"";
        item.BenefitsJson=JsonSerializer.Serialize((model.Benefits??[]).Where(Benefits.Contains).Distinct());
        item.BenefitDetails=model.BenefitDetails; item.ImplementationScope=model.ImplementationScope;
        item.ImplementationMethod=model.ImplementationMethod;
        item.ResourceTypesJson=JsonSerializer.Serialize((model.ResourceTypes??[]).Where(Resources.Contains).Distinct());
        item.ResourceDetails=model.ResourceDetails; item.UpdatedAtUtc=DateTime.UtcNow;

        if(item.Id==0)
        {
            await db.SaveChangesAsync();
            item.SuggestionNumber=$"PISH-{DateTime.UtcNow:yyyy}-{item.Id:000000}";
        }
        db.SuggestionMembers.RemoveRange(item.Members);
        foreach(var p in members) item.Members.Add(new SuggestionMember { EmployeeId=p.Id, EmployeeNameSnapshot=p.FirstName+" "+p.LastName, PersonnelNumberSnapshot=p.PersonnelNumber });

        if(files is not null)
        {
            var extensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase){".pdf",".doc",".docx",".xls",".xlsx",".png",".jpg",".jpeg"};
            var folder=Path.Combine(env.WebRootPath??Path.Combine(env.ContentRootPath,"wwwroot"),"uploads","suggestions",item.Id.ToString());
            Directory.CreateDirectory(folder);
            foreach(var file in files.Where(x=>x.Length>0))
            {
                var ext=Path.GetExtension(file.FileName);
                if(!extensions.Contains(ext)||file.Length>10*1024*1024) continue;
                var stored=Guid.NewGuid().ToString("N")+ext.ToLowerInvariant();
                await using var stream=System.IO.File.Create(Path.Combine(folder,stored));
                await file.CopyToAsync(stream);
                item.Attachments.Add(new SuggestionAttachment { OriginalFileName=Path.GetFileName(file.FileName),StoredFileName=stored,ContentType=file.ContentType??"application/octet-stream",FileSize=file.Length });
            }
        }
        if(submit){item.Status="Submitted";item.SubmittedAtUtc=DateTime.UtcNow;}
        await db.SaveChangesAsync();
        TempData["Success"]=submit?"پیشنهاد ثبت شد. شماره پیگیری: "+item.SuggestionNumber:"پیش‌نویس ذخیره شد.";
        return RedirectToAction(submit?nameof(Details):nameof(Create),new{id=item.Id});
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id)
    {
        var query=db.SuggestionAttachments.Include(x=>x.Suggestion).AsQueryable();
        if(!CanManage)
        {
            if(!TryEmployeeId(out var owner)) return Forbid();
            query=query.Where(x=>x.Suggestion!.ProposerEmployeeId==owner);
        }
        var attachment=await query.FirstOrDefaultAsync(x=>x.Id==id);
        if(attachment is null) return NotFound();
        var path=Path.Combine(env.WebRootPath??Path.Combine(env.ContentRootPath,"wwwroot"),"uploads","suggestions",attachment.SuggestionId.ToString(),attachment.StoredFileName);
        return System.IO.File.Exists(path)?PhysicalFile(path,attachment.ContentType,attachment.OriginalFileName):NotFound();
    }

    private async Task<List<SuggestionEmployeeOption>> Options(IEnumerable<int> ids,int proposerId)
    {
        var selected=ids.Distinct().Where(x=>x!=proposerId).ToArray();
        return await db.Employees.AsNoTracking().Where(x=>selected.Contains(x.Id)&&x.Status=="فعال"&&x.IsSystemUser)
            .Select(x=>new SuggestionEmployeeOption { Id=x.Id,Name=x.FirstName+" "+x.LastName,PersonnelNumber=x.PersonnelNumber,
                Organization=x.OrganizationUnit!=null?x.OrganizationUnit.Title:x.OrganizationDepartment!=null?x.OrganizationDepartment.Title:"—" }).ToListAsync();
    }

    private async Task SetProposerViewAsync(int id)
    {
        ViewBag.Proposer=id>0?await db.Employees.AsNoTracking().Where(x=>x.Id==id&&x.Status=="فعال"&&x.IsSystemUser)
            .Select(x=>new SuggestionEmployeeOption { Id=x.Id,Name=x.FirstName+" "+x.LastName,PersonnelNumber=x.PersonnelNumber,
                Organization=x.OrganizationUnit!=null?x.OrganizationUnit.Title:x.OrganizationDepartment!=null?x.OrganizationDepartment.Title:"—" }).FirstOrDefaultAsync():null;
    }
}
