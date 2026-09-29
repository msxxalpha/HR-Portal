using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class EnvironmentSettingsService(HRPortalDbContext db, ReportCredentialProtector credentialProtector)
{
    // Backward-compatible entity view used by the shared layout and older controllers.
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

    public async Task<SystemSettingsEditModel> GetEditAsync()
    {
        var system = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync()
                      ?? new SystemSettings();

        var otp = await db.OtpSettings.AsNoTracking().FirstOrDefaultAsync()
                  ?? new OtpSettings();

        var sms = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync()
                  ?? new SmsSettings();

        var payroll = await db.PayrollReportSettings.AsNoTracking().FirstOrDefaultAsync()
                      ?? new PayrollReportSettings();

        return MapToEditModel(system, otp, sms, payroll);
    }

    public async Task SaveAsync(SystemSettingsEditModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        await using var transaction = await db.Database.BeginTransactionAsync();

        var system = await db.SystemSettings.FirstOrDefaultAsync();
        if (system is null)
        {
            system = new SystemSettings();
            db.SystemSettings.Add(system);
        }
        ApplySystem(system, model);
        system.UpdatedAt = DateTime.UtcNow;

        var otp = await db.OtpSettings.FirstOrDefaultAsync();
        if (otp is null)
        {
            otp = new OtpSettings();
            db.OtpSettings.Add(otp);
        }
        ApplyOtp(otp, model);

        var sms = await db.SmsSettings.FirstOrDefaultAsync();
        if (sms is null)
        {
            sms = new SmsSettings();
            db.SmsSettings.Add(sms);
        }
        ApplySms(sms, model);

        var payroll = await db.PayrollReportSettings.FirstOrDefaultAsync();
        if (payroll is null)
        {
            payroll = new PayrollReportSettings();
            db.PayrollReportSettings.Add(payroll);
        }
        ApplyPayroll(payroll, model);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        db.ChangeTracker.Clear();
    }

    private static SystemSettingsEditModel MapToEditModel(
        SystemSettings system,
        OtpSettings otp,
        SmsSettings sms,
        PayrollReportSettings payroll)
    {
        return new SystemSettingsEditModel
        {
            ApplicationName = system.ApplicationName ?? "",
            OrganizationName = system.OrganizationName ?? "",
            ShortName = system.ShortName ?? "",
            Slogan = system.Slogan ?? "",
            FooterText = system.FooterText ?? "",
            Website = system.Website ?? "",
            Phone = system.Phone ?? "",
            Email = system.Email ?? "",
            EconomicCode = system.EconomicCode ?? "",
            NationalId = system.NationalId ?? "",
            DefaultLanguage = system.DefaultLanguage ?? "",
            Calendar = system.Calendar ?? "",
            TimeZone = system.TimeZone ?? "",
            Theme = system.Theme ?? "",
            PrimaryColor = system.PrimaryColor ?? "#17324D",
            SecondaryColor = system.SecondaryColor ?? "#6C757D",
            LogoUrl = system.LogoUrl ?? "",
            FaviconUrl = system.FaviconUrl ?? "",
            ItemsPerPage = system.ItemsPerPage,

            OtpLength = otp.Length,
            OtpValiditySeconds = otp.ValiditySeconds,
            OtpMaxAttempts = otp.MaxAttempts,
            OtpEnabled = otp.Enabled,
            AllowUserSelfService = system.AllowUserSelfService,
            EnableAuditLog = system.EnableAuditLog,

            SmsEnabled = sms.Enabled,
            SmsEndpoint = sms.Endpoint ?? "",
            SmsMethod = sms.Method ?? "",
            SmsFormat = sms.Format ?? "",
            SmsAuthMode = sms.AuthMode ?? "",
            SmsApiKeyName = sms.ApiKeyName ?? "",
            SmsApiKey = "",
            SmsSenderField = sms.SenderField ?? "",
            SmsSender = sms.Sender ?? "",
            SmsRecipientField = sms.RecipientField ?? "",
            SmsRecipientMode = sms.RecipientMode ?? "",
            SmsMessageField = sms.MessageField ?? "",
            SmsCodeField = sms.CodeField ?? "code",
            SmsOtpParameterField = sms.OtpParameterField ?? "code",
            SmsPatternCode = sms.PatternCode ?? "",
            SmsNumberFormatField = sms.NumberFormatField ?? "",
            SmsNumberFormat = sms.NumberFormat ?? "",
            SmsStaticParams = sms.StaticParams ?? "",
            SmsSuccessCodes = sms.SuccessCodes ?? "",
            SmsTemplate = sms.Template ?? "",
            SmsTestRecipient = sms.TestRecipient ?? "",

            PayrollEnabled = payroll.Enabled,
            PayrollReportUrl = payroll.ReportUrl ?? "",
            PayrollReportServerUrl = payroll.ReportServerUrl ?? "",
            PayrollReportPath = payroll.ReportPath ?? "",
            PayrollYearParameter = payroll.YearParameter ?? "",
            PayrollPersonnelParameter = payroll.PersonnelParameter ?? "",
            PayrollReportFormat = payroll.ReportFormat ?? "",
            PayrollReportAuthentication = string.IsNullOrWhiteSpace(payroll.ReportAuthentication)
                ? (payroll.UseIntegratedSecurity ? "windows" : "none")
                : payroll.ReportAuthentication,
            PayrollReportUsername = payroll.ReportUsername ?? "",
            PayrollReportDomain = payroll.ReportDomain ?? "",
            PayrollReportPassword = "",
            PayrollUseIntegratedSecurity = payroll.UseIntegratedSecurity,

            MaintenanceMode = system.MaintenanceMode,
            MaintenanceMessage = system.MaintenanceMessage ?? ""
        };
    }

    private static void ApplySystem(SystemSettings entity, SystemSettingsEditModel model)
    {
        entity.ApplicationName = model.ApplicationName?.Trim() ?? "";
        entity.OrganizationName = model.OrganizationName?.Trim() ?? "";
        entity.ShortName = model.ShortName?.Trim() ?? "";
        entity.Slogan = model.Slogan?.Trim() ?? "";
        entity.FooterText = model.FooterText?.Trim() ?? "";
        entity.Website = model.Website?.Trim() ?? "";
        entity.Phone = model.Phone?.Trim() ?? "";
        entity.Email = model.Email?.Trim() ?? "";
        entity.EconomicCode = model.EconomicCode?.Trim() ?? "";
        entity.NationalId = model.NationalId?.Trim() ?? "";
        entity.DefaultLanguage = model.DefaultLanguage?.Trim() ?? "";
        entity.Calendar = model.Calendar?.Trim() ?? "";
        entity.TimeZone = model.TimeZone?.Trim() ?? "";
        entity.Theme = model.Theme?.Trim() ?? "";
        entity.PrimaryColor = model.PrimaryColor?.Trim() ?? "";
        entity.SecondaryColor = model.SecondaryColor?.Trim() ?? "";
        entity.LogoUrl = model.LogoUrl?.Trim() ?? "";
        entity.FaviconUrl = model.FaviconUrl?.Trim() ?? "";
        entity.ItemsPerPage = model.ItemsPerPage;
        entity.EnableAuditLog = model.EnableAuditLog;
        entity.MaintenanceMode = model.MaintenanceMode;
        entity.MaintenanceMessage = model.MaintenanceMessage?.Trim() ?? "";
        entity.AllowUserSelfService = model.AllowUserSelfService;
    }

    private static void ApplyOtp(OtpSettings entity, SystemSettingsEditModel model)
    {
        entity.Length = model.OtpLength;
        entity.ValiditySeconds = model.OtpValiditySeconds;
        entity.MaxAttempts = model.OtpMaxAttempts;
        entity.Enabled = model.OtpEnabled;
    }

    private static void ApplySms(SmsSettings entity, SystemSettingsEditModel model)
    {
        entity.Enabled = model.SmsEnabled;
        entity.Endpoint = model.SmsEndpoint?.Trim() ?? "";
        entity.Method = model.SmsMethod?.Trim() ?? "";
        entity.Format = model.SmsFormat?.Trim() ?? "";
        entity.AuthMode = model.SmsAuthMode?.Trim() ?? "";
        entity.ApiKeyName = model.SmsApiKeyName?.Trim() ?? "";
        entity.SenderField = model.SmsSenderField?.Trim() ?? "";
        entity.Sender = model.SmsSender?.Trim() ?? "";
        entity.RecipientField = model.SmsRecipientField?.Trim() ?? "";
        entity.RecipientMode = model.SmsRecipientMode?.Trim() ?? "";
        entity.MessageField = model.SmsMessageField?.Trim() ?? "";
        entity.CodeField = model.SmsCodeField?.Trim() ?? "code";
        entity.OtpParameterField = model.SmsOtpParameterField?.Trim() ?? "code";
        entity.PatternCode = model.SmsPatternCode?.Trim() ?? "";
        entity.NumberFormatField = model.SmsNumberFormatField?.Trim() ?? "";
        entity.NumberFormat = model.SmsNumberFormat?.Trim() ?? "";
        entity.StaticParams = model.SmsStaticParams ?? "";
        entity.SuccessCodes = model.SmsSuccessCodes?.Trim() ?? "";
        entity.Template = model.SmsTemplate?.Trim() ?? "";
        entity.TestRecipient = model.SmsTestRecipient?.Trim() ?? "";

        // A blank password/API key means "keep the existing secret".
        if (!string.IsNullOrWhiteSpace(model.SmsApiKey))
            entity.ApiKey = model.SmsApiKey;
    }

    private static void ApplyPayroll(PayrollReportSettings entity, SystemSettingsEditModel model)
    {
        entity.Enabled = model.PayrollEnabled;
        entity.ReportUrl = model.PayrollReportUrl?.Trim() ?? "";
        entity.ReportServerUrl = model.PayrollReportServerUrl?.Trim() ?? "";
        entity.ReportPath = model.PayrollReportPath?.Trim() ?? "";
        entity.YearParameter = model.PayrollYearParameter?.Trim() ?? "";
        entity.PersonnelParameter = model.PayrollPersonnelParameter?.Trim() ?? "";
        entity.ReportFormat = model.PayrollReportFormat?.Trim() ?? "";
        entity.ReportAuthentication = string.IsNullOrWhiteSpace(model.PayrollReportAuthentication)
            ? (model.PayrollUseIntegratedSecurity ? "windows" : "none")
            : model.PayrollReportAuthentication.Trim().ToLowerInvariant();
        entity.ReportUsername = model.PayrollReportUsername?.Trim() ?? "";
        entity.ReportDomain = model.PayrollReportDomain?.Trim() ?? "";
        entity.UseIntegratedSecurity = string.Equals(entity.ReportAuthentication, "windows", StringComparison.OrdinalIgnoreCase);

        // Password is never returned to the browser. A blank form value means
        // keep the encrypted credential already stored in SQL Server.
        if (!string.IsNullOrWhiteSpace(model.PayrollReportPassword))
            entity.ReportPasswordProtected = credentialProtector.Protect(model.PayrollReportPassword);
    }
}
