using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Services;
public class OrganizationService(HRPortalDbContext db,AuditService audit)
{
 public async Task<OrganizationStructureRevision?> CurrentAsync()=>await db.OrganizationStructureRevisions.Include(x=>x.Nodes).Where(x=>x.IsFinalized&&x.EffectiveDate<=DateTime.Today).OrderByDescending(x=>x.EffectiveDate).FirstOrDefaultAsync();
 public async Task<List<OrganizationStructureRevision>> RevisionsAsync()=>await db.OrganizationStructureRevisions.Include(x=>x.Changes).OrderByDescending(x=>x.EffectiveDate).ToListAsync();
 public async Task<OrganizationStructureRevision> CreateRevisionAsync(DateTime effectiveDate,string title,string? notes)
 {
  var current=await CurrentAsync();var r=new OrganizationStructureRevision{RevisionCode=$"ORG-{DateTime.Now:yyyyMMddHHmmssfff}",EffectiveDate=effectiveDate.Date,Title=title,Notes=notes};db.OrganizationStructureRevisions.Add(r);await db.SaveChangesAsync();
  if(current!=null){var nodes=await db.OrganizationNodes.Where(x=>x.OrganizationStructureRevisionId==current.Id).AsNoTracking().ToListAsync();foreach(var n in nodes.Where(x=>x.ParentId==null))await CloneTree(nodes,n,r.Id,null);}
  await audit.WriteAsync("ایجاد نسخه ساختار",nameof(OrganizationStructureRevision),r.Id.ToString(),r.Title);return r;
 }
 private async Task CloneTree(List<OrganizationNode> all,OrganizationNode old,int revisionId,int? parent){var c=new OrganizationNode{OrganizationStructureRevisionId=revisionId,ParentId=parent,Code=old.Code,Title=old.Title,RankType=old.RankType,SortOrder=old.SortOrder,IsActive=old.IsActive,Notes=old.Notes};db.OrganizationNodes.Add(c);await db.SaveChangesAsync();foreach(var child in all.Where(x=>x.ParentId==old.Id))await CloneTree(all,child,revisionId,c.Id);}
 public async Task<bool> CanDeleteAsync(int id)=>!await db.Employees.AnyAsync(e=>e.OrganizationUnitId==id||e.OrganizationDepartmentId==id||e.OrganizationSectionId==id)&&!await db.OrganizationNodes.AnyAsync(x=>x.ParentId==id);
 public async Task FinalizeAsync(int id){var r=await db.OrganizationStructureRevisions.Include(x=>x.Nodes).FirstOrDefaultAsync(x=>x.Id==id)??throw new InvalidOperationException("نسخه ساختار یافت نشد.");if(r.IsFinalized)return;if(await db.OrganizationStructureRevisions.AnyAsync(x=>x.Id!=id&&x.IsFinalized&&x.EffectiveDate==r.EffectiveDate))throw new InvalidOperationException("برای این تاریخ نسخه نهایی دیگری وجود دارد.");r.IsFinalized=true;r.FinalizedAt=DateTime.UtcNow;await db.SaveChangesAsync();await audit.WriteAsync("نهایی‌سازی ساختار",nameof(OrganizationStructureRevision),r.Id.ToString(),r.Title);}
}