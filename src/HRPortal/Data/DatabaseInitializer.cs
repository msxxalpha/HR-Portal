using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Data;
public static class DatabaseInitializer
{
 public static async Task InitializeAsync(HRPortalDbContext db)
 {
  await db.Database.EnsureCreatedAsync();
  if(!await db.SystemSettings.AnyAsync())db.SystemSettings.Add(new SystemSettings{OrganizationName="شرکت کمک فنرسازی ایندامین سایپا"});
  if(!await db.OtpSettings.AnyAsync())db.OtpSettings.Add(new OtpSettings());
  if(!await db.SmsSettings.AnyAsync())db.SmsSettings.Add(new SmsSettings());
  if(!await db.PayrollReportSettings.AnyAsync())db.PayrollReportSettings.Add(new PayrollReportSettings());
  if(!await db.OrganizationStructureRevisions.AnyAsync()){db.OrganizationStructureRevisions.Add(new OrganizationStructureRevision{RevisionCode="ORG-001",Title="نسخه اولیه ساختار سازمانی",EffectiveDate=DateTime.Today,IsFinalized=true,FinalizedAt=DateTime.UtcNow});}
  await db.SaveChangesAsync();
 }
}