namespace HRPortal.Models;

public class SystemSettings
{
    public int Id { get; set; }
    public string ApplicationName { get; set; } = "پورتال جامع منابع انسانی"; public string OrganizationName { get; set; } = ""; public string ShortName { get; set; } = "HR"; public string Slogan { get; set; } = ""; public string FooterText { get; set; } = ""; public string Website { get; set; } = ""; public string Phone { get; set; } = ""; public string Email { get; set; } = ""; public string EconomicCode { get; set; } = ""; public string NationalId { get; set; } = "";
    public string DefaultLanguage { get; set; } = "fa-IR"; public string Calendar { get; set; } = "Persian"; public string TimeZone { get; set; } = "Asia/Tehran"; public string Theme { get; set; } = "Indamin"; public string PrimaryColor { get; set; } = "#17324D"; public string SecondaryColor { get; set; } = "#6C757D"; public string LogoUrl { get; set; } = "/images/logo.png"; public string FaviconUrl { get; set; } = "";
    public int ItemsPerPage { get; set; } = 20; public bool EnableAuditLog { get; set; } = true; public bool MaintenanceMode { get; set; }
    public string MaintenanceMessage { get; set; } = "سامانه در حال بروزرسانی است."; public bool AllowUserSelfService { get; set; } = true; public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}