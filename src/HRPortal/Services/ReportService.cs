using HRPortal.Data;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Services;
public class ReportService(HRPortalDbContext db)
{
 public async Task<string?> BuildUrlAsync(string yearMonth,string personnelNo)
 {
  var s=await db.PayrollReportSettings.AsNoTracking().FirstOrDefaultAsync();
  if(s is null||!s.Enabled||string.IsNullOrWhiteSpace(s.ReportServerUrl)||string.IsNullOrWhiteSpace(s.ReportPath))return null;
  var baseUrl=s.ReportServerUrl.TrimEnd('/');var path=s.ReportPath.StartsWith("/")?s.ReportPath:"/"+s.ReportPath;
  return $"{baseUrl}?{Uri.EscapeDataString(s.YearParameter)}={Uri.EscapeDataString(yearMonth)}&{Uri.EscapeDataString(s.PersonnelParameter)}={Uri.EscapeDataString(personnelNo)}&rs:Command=Render&rs:Format={Uri.EscapeDataString(s.ReportFormat)}";
 }
}