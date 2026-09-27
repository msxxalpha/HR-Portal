using System.Text;
using System.Text.Json;
using HRPortal.Data;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Services;
public interface ISmsService{Task<(bool Success,string Response)> SendOtpAsync(string mobile,string code);}
public class ConfigurableSmsService(HRPortalDbContext db,IHttpClientFactory clients):ISmsService
{
 public async Task<(bool Success,string Response)> SendOtpAsync(string mobile,string code)
 {
  var s=await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync();
  if(s is null||!s.Enabled)return(false,"سرویس پیامک فعال نیست.");
  if(string.IsNullOrWhiteSpace(s.ServiceUrl))return(false,"آدرس سرویس پیامک تنظیم نشده است.");
  var message=s.OtpTemplate.Replace("{code}",code).Replace("{mobile}",mobile);
  try
  {
   var client=clients.CreateClient();using var req=new HttpRequestMessage(HttpMethod.Post,s.ServiceUrl);
   var payload=new Dictionary<string,string>{{"mobile",mobile},{"message",message},{"sender",s.SenderNumber},{"username",s.Username},{"secret",s.Password},{"apiKey",s.ApiKey}};
   req.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
   var res=await client.SendAsync(req);var body=await res.Content.ReadAsStringAsync();return(res.IsSuccessStatusCode,body);
  }catch(Exception ex){return(false,ex.Message);}
 }
}