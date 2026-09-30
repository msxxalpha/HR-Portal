namespace HRPortal.Models;

public class SystemSettingsViewModel
{
    public SystemSettings System { get; set; } = new(); public OtpSettings Otp { get; set; } = new(); public SmsSettings Sms { get; set; } = new(); public PayrollReportSettings Payroll { get; set; } = new();
}