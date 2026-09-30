namespace HRPortal.Models;

public class PayrollReportSettings
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public string ReportUrl { get; set; } = "";
    public string ReportServerUrl { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public string YearParameter { get; set; } = "YearMonth";
    public string PersonnelParameter { get; set; } = "PersonnelNo";
    public string ReportFormat { get; set; } = "HTML4.0";
    public string ReportAuthentication { get; set; } = "forms";
    public string ReportUsername { get; set; } = "";
    public string ReportDomain { get; set; } = "";
    public string ReportLoginUrl { get; set; } = "";
    public string ReportUsernameField { get; set; } = "";
    public string ReportPasswordField { get; set; } = "";
    public string ReportPasswordProtected { get; set; } = "";
    public bool UseIntegratedSecurity { get; set; }
}