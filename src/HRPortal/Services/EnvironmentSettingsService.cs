using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Services;
public class EnvironmentSettingsService(HRPortalDbContext db)
{
 public async Task<SystemSettingsViewModel> GetAsync()=>new(){System=await One(db.SystemSettings,new SystemSettings()),Otp=await One(db.OtpSettings,new OtpSettings()),Sms=await One(db.SmsSettings,new SmsSettings()),Payroll=await One(db.PayrollReportSettings,new PayrollReportSettings())};
 private static async Task<T> One<T>(DbSet<T> set,T fallback) where T:class=>await set.AsNoTracking().FirstOrDefaultAsync()??fallback;
 public async Task SaveAsync(SystemSettingsViewModel m){var s=await db.SystemSettings.FirstOrDefaultAsync();if(s==null)db.SystemSettings.Add(m.System);else{m.System.Id=s.Id;db.Entry(s).CurrentValues.SetValues(m.System);s.UpdatedAt=DateTime.UtcNow;}var o=await db.OtpSettings.FirstOrDefaultAsync();if(o==null)db.OtpSettings.Add(m.Otp);else{m.Otp.Id=o.Id;db.Entry(o).CurrentValues.SetValues(m.Otp);}var sms=await db.SmsSettings.FirstOrDefaultAsync();if(sms==null)db.SmsSettings.Add(m.Sms);else{m.Sms.Id=sms.Id;var existingApiKey=sms.ApiKey;db.Entry(sms).CurrentValues.SetValues(m.Sms);if(string.IsNullOrWhiteSpace(m.Sms.ApiKey))sms.ApiKey=existingApiKey;}var p=await db.PayrollReportSettings.FirstOrDefaultAsync();if(p==null)db.PayrollReportSettings.Add(m.Payroll);else{m.Payroll.Id=p.Id;db.Entry(p).CurrentValues.SetValues(m.Payroll);}await db.SaveChangesAsync();}
}