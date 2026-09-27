namespace HRPortal.Models;
public class OtpChallenge
{
 public int Id{get;set;} public int? EmployeeId{get;set;} public string PersonnelNumber{get;set;}=""; public string Mobile{get;set;}=""; public string CodeHash{get;set;}=""; public DateTime CreatedAt{get;set;}=DateTime.UtcNow; public DateTime ExpiresAt{get;set;} public int AttemptCount{get;set;} public bool IsConsumed{get;set;} public bool SmsSent{get;set;} public string? SmsResponse{get;set;}
}