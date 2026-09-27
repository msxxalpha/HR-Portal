namespace HRPortal.Models;

public class PayrollReportSettings
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public string ReportServerUrl { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public string YearParameter { get; set; } = "YearMonth";
    public string PersonnelParameter { get; set; } = "PersonnelNo";
    public string ReportFormat { get; set; } = "PDF";
    public bool UseIntegratedSecurity { get; set; }
}