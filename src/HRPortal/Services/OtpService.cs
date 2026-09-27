using System.Security.Cryptography;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Services;
public class OtpService(HRPortalDbContext db,ISmsService sms,IWebHostEnvironment env)
{
 public async Task<(bool Success,string Message,string? DebugCode)> IssueAsync(Employee employee)
 {
  var s=await db.OtpSettings.AsNoTracking().FirstOrDefaultAsync() ?? new OtpSettings();
  var code=RandomNumberGenerator.GetInt32((int)Math.Pow(10,s.Length-1),(int)Math.Pow(10,s.Length));var text=code.ToString($"D{s.Length}");
  var c=new OtpChallenge{EmployeeId=employee.Id,PersonnelNumber=employee.PersonnelNumber,Mobile=employee.Mobile,CodeHash=PasswordHasher.Hash(text),ExpiresAt=DateTime.UtcNow.AddSeconds(s.ValiditySeconds)};
  db.OtpChallenges.Add(c);await db.SaveChangesAsync();
  var r=await sms.SendOtpAsync(employee.Mobile,text);c.SmsSent=r.Success;c.SmsResponse=r.Response.Length>2000?r.Response[..2000]:r.Response;await db.SaveChangesAsync();
  var debug=env.IsDevelopment()?text:null;
  return r.Success?(true,"کد یکبارمصرف ارسال شد.",debug):(false,"ارسال پیامک ناموفق بود: "+r.Response,debug);
 }
 public async Task<bool> VerifyAsync(string personnelNumber,string code)
 {
  var s=await db.OtpSettings.AsNoTracking().FirstOrDefaultAsync() ?? new OtpSettings();
  var c=await db.OtpChallenges.Where(x=>x.PersonnelNumber==personnelNumber&&!x.IsConsumed).OrderByDescending(x=>x.CreatedAt).FirstOrDefaultAsync();
  if(c is null||c.ExpiresAt<DateTime.UtcNow||c.AttemptCount>=s.MaxAttempts)return false;c.AttemptCount++;var ok=PasswordHasher.Verify(code,c.CodeHash);if(ok)c.IsConsumed=true;await db.SaveChangesAsync();return ok;
 }
}