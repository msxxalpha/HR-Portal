namespace HRPortal.Models;

public class SystemSettingsEditModel
{
    // General
    public string? ApplicationName { get; set; }
    public string? OrganizationName { get; set; }
    public string? ShortName { get; set; }
    public string? Slogan { get; set; }
    public string? FooterText { get; set; }
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? EconomicCode { get; set; }
    public string? NationalId { get; set; }
    public string? DefaultLanguage { get; set; }
    public string? Calendar { get; set; }
    public string? TimeZone { get; set; }
    public string? Theme { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public int ItemsPerPage { get; set; }

    // Security / OTP
    public int OtpLength { get; set; }
    public int OtpValiditySeconds { get; set; }
    public int OtpMaxAttempts { get; set; }
    public bool OtpEnabled { get; set; }
    public bool AllowUserSelfService { get; set; }
    public bool EnableAuditLog { get; set; }

    // SMS
    public bool SmsEnabled { get; set; }
    public string? SmsEndpoint { get; set; }
    public string? SmsMethod { get; set; }
    public string? SmsFormat { get; set; }
    public string? SmsAuthMode { get; set; }
    public string? SmsApiKeyName { get; set; }
    public string? SmsApiKey { get; set; }
    public string? SmsSenderField { get; set; }
    public string? SmsSender { get; set; }
    public string? SmsRecipientField { get; set; }
    public string? SmsRecipientMode { get; set; }
    public string? SmsMessageField { get; set; }
    public string? SmsCodeField { get; set; }
    public string? SmsOtpParameterField { get; set; }
    public string? SmsPatternCode { get; set; }
    public string? SmsNumberFormatField { get; set; }
    public string? SmsNumberFormat { get; set; }
    public string? SmsStaticParams { get; set; }
    public string? SmsSuccessCodes { get; set; }
    public string? SmsTemplate { get; set; }
    public string? SmsTestRecipient { get; set; }

    // Payroll
    public bool PayrollEnabled { get; set; }
    public string? PayrollReportUrl { get; set; }
    public string? PayrollReportServerUrl { get; set; }
    public string? PayrollReportPath { get; set; }
    public string? PayrollYearParameter { get; set; }
    public string? PayrollPersonnelParameter { get; set; }
    public string? PayrollReportFormat { get; set; }
    public string? PayrollReportAuthentication { get; set; }
    public string? PayrollReportUsername { get; set; }
    public string? PayrollReportDomain { get; set; }
    public string? PayrollReportLoginUrl { get; set; }
    public string? PayrollReportUsernameField { get; set; }
    public string? PayrollReportPasswordField { get; set; }
    public string? PayrollReportPassword { get; set; }
    public bool PayrollUseIntegratedSecurity { get; set; }
    public bool PayrollAllowPrint { get; set; } = true;
    public bool PayrollAllowDownload { get; set; } = true;

    // Personnel order / SSRS
    public bool PersonnelOrderEnabled { get; set; }
    public string? PersonnelOrderReportUrl { get; set; }
    public string? PersonnelOrderReportServerUrl { get; set; }
    public string? PersonnelOrderReportPath { get; set; }
    public string? PersonnelOrderIdParameter { get; set; }
    public string? PersonnelOrderReportFormat { get; set; }
    public string? PersonnelOrderReportAuthentication { get; set; }
    public string? PersonnelOrderReportUsername { get; set; }
    public string? PersonnelOrderReportDomain { get; set; }
    public string? PersonnelOrderReportLoginUrl { get; set; }
    public string? PersonnelOrderReportUsernameField { get; set; }
    public string? PersonnelOrderReportPasswordField { get; set; }
    public string? PersonnelOrderReportPassword { get; set; }
    public bool PersonnelOrderUseIntegratedSecurity { get; set; }
    public bool PersonnelOrderAllowPrint { get; set; } = true;
    public bool PersonnelOrderAllowDownload { get; set; } = true;

    // Announcement categories (base data)
    public List<AnnouncementCategory> AnnouncementCategories { get; set; } = [];

    // Maintenance
    public bool MaintenanceMode { get; set; }
    public string? MaintenanceMessage { get; set; }
}
