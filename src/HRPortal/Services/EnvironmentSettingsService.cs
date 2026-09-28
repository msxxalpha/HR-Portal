using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class EnvironmentSettingsService(HRPortalDbContext db)
{
    public async Task<SystemSettingsViewModel> GetAsync()
    {
        var system = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync()
            ?? new SystemSettings();

        var otp = await db.OtpSettings.AsNoTracking().FirstOrDefaultAsync()
            ?? new OtpSettings();

        var sms = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync()
            ?? new SmsSettings();

        var payroll = await db.PayrollReportSettings.AsNoTracking().FirstOrDefaultAsync()
            ?? new PayrollReportSettings();

        return new SystemSettingsViewModel
        {
            System = system,
            Otp = otp,
            Sms = sms,
            Payroll = payroll
        };
    }

    public async Task SaveAsync(SystemSettingsViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        await using var transaction = await db.Database.BeginTransactionAsync();

        var system = await db.SystemSettings.FirstOrDefaultAsync();
        if (system is null)
        {
            system = model.System ?? new SystemSettings();
            system.UpdatedAt = DateTime.UtcNow;
            db.SystemSettings.Add(system);
        }
        else
        {
            CopySystem(system, model.System ?? new SystemSettings());
            system.UpdatedAt = DateTime.UtcNow;
        }

        var otp = await db.OtpSettings.FirstOrDefaultAsync();
        if (otp is null)
            db.OtpSettings.Add(model.Otp ?? new OtpSettings());
        else
            CopyOtp(otp, model.Otp ?? new OtpSettings());

        var sms = await db.SmsSettings.FirstOrDefaultAsync();
        if (sms is null)
            db.SmsSettings.Add(model.Sms ?? new SmsSettings());
        else
        {
            var incoming = model.Sms ?? new SmsSettings();
            var existingApiKey = sms.ApiKey;
            CopySms(sms, incoming);

            // An empty API key means "keep the existing secret", not "erase it".
            if (string.IsNullOrWhiteSpace(incoming.ApiKey))
                sms.ApiKey = existingApiKey;
        }

        var payroll = await db.PayrollReportSettings.FirstOrDefaultAsync();
        if (payroll is null)
            db.PayrollReportSettings.Add(model.Payroll ?? new PayrollReportSettings());
        else
            CopyPayroll(payroll, model.Payroll ?? new PayrollReportSettings());

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void CopySystem(SystemSettings target, SystemSettings source)
    {
        target.ApplicationName = source.ApplicationName?.Trim() ?? "";
        target.OrganizationName = source.OrganizationName?.Trim() ?? "";
        target.ShortName = source.ShortName?.Trim() ?? "";
        target.Slogan = source.Slogan?.Trim() ?? "";
        target.FooterText = source.FooterText?.Trim() ?? "";
        target.Website = source.Website?.Trim() ?? "";
        target.Phone = source.Phone?.Trim() ?? "";
        target.Email = source.Email?.Trim() ?? "";
        target.EconomicCode = source.EconomicCode?.Trim() ?? "";
        target.NationalId = source.NationalId?.Trim() ?? "";
        target.DefaultLanguage = source.DefaultLanguage?.Trim() ?? "fa-IR";
        target.Calendar = source.Calendar?.Trim() ?? "Persian";
        target.TimeZone = source.TimeZone?.Trim() ?? "Asia/Tehran";
        target.Theme = source.Theme?.Trim() ?? "Indamin";
        target.PrimaryColor = string.IsNullOrWhiteSpace(source.PrimaryColor) ? "#17324D" : source.PrimaryColor.Trim();
        target.SecondaryColor = string.IsNullOrWhiteSpace(source.SecondaryColor) ? "#6C757D" : source.SecondaryColor.Trim();
        target.LogoUrl = source.LogoUrl?.Trim() ?? "";
        target.FaviconUrl = source.FaviconUrl?.Trim() ?? "";
        target.ItemsPerPage = source.ItemsPerPage <= 0 ? 20 : source.ItemsPerPage;
        target.EnableAuditLog = source.EnableAuditLog;
        target.MaintenanceMode = source.MaintenanceMode;
        target.MaintenanceMessage = source.MaintenanceMessage?.Trim() ?? "";
        target.AllowUserSelfService = source.AllowUserSelfService;
    }

    private static void CopyOtp(OtpSettings target, OtpSettings source)
    {
        target.Length = source.Length is >= 4 and <= 8 ? source.Length : 5;
        target.ValiditySeconds = source.ValiditySeconds is >= 30 and <= 900 ? source.ValiditySeconds : 120;
        target.MaxAttempts = source.MaxAttempts is >= 1 and <= 20 ? source.MaxAttempts : 5;
        target.Enabled = source.Enabled;
    }

    private static void CopySms(SmsSettings target, SmsSettings source)
    {
        target.Enabled = source.Enabled;
        target.Endpoint = source.Endpoint?.Trim() ?? "";
        target.Method = source.Method?.Trim() ?? "POST";
        target.Format = source.Format?.Trim() ?? "json";
        target.AuthMode = source.AuthMode?.Trim() ?? "header";
        target.ApiKeyName = source.ApiKeyName?.Trim() ?? "Api-Key";
        target.SenderField = source.SenderField?.Trim() ?? "sender";
        target.Sender = source.Sender?.Trim() ?? "";
        target.RecipientField = source.RecipientField?.Trim() ?? "recipient";
        target.RecipientMode = source.RecipientMode?.Trim() ?? "scalar";
        target.MessageField = source.MessageField?.Trim() ?? "message";
        target.NumberFormatField = source.NumberFormatField?.Trim() ?? "";
        target.NumberFormat = source.NumberFormat?.Trim() ?? "";
        target.StaticParams = source.StaticParams ?? "";
        target.SuccessCodes = source.SuccessCodes?.Trim() ?? "200-299";
        target.Template = string.IsNullOrWhiteSpace(source.Template)
            ? "کاربر محترم، کد ورود شما: {code}"
            : source.Template.Trim();
        target.TestRecipient = source.TestRecipient?.Trim() ?? "";
    }

    private static void CopyPayroll(PayrollReportSettings target, PayrollReportSettings source)
    {
        target.Enabled = source.Enabled;
        target.ReportUrl = source.ReportUrl?.Trim() ?? "";
        target.ReportServerUrl = source.ReportServerUrl?.Trim() ?? "";
        target.ReportPath = source.ReportPath?.Trim() ?? "";
        target.YearParameter = source.YearParameter?.Trim() ?? "YearMonth";
        target.PersonnelParameter = source.PersonnelParameter?.Trim() ?? "PersonnelNo";
        target.ReportFormat = source.ReportFormat?.Trim() ?? "HTML4.0";
        target.UseIntegratedSecurity = source.UseIntegratedSecurity;
    }
}
