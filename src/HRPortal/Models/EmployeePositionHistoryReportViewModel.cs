namespace HRPortal.Models;

public sealed class EmployeePositionHistoryReportViewModel
{
    public int? EmployeeId { get; set; }
    public string Position { get; set; } = "";
    public string ChangeType { get; set; } = "all";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<EmployeePositionHistoryRow> Rows { get; set; } = [];
    public List<EmployeePositionHistoryEmployeeOption> EmployeeOptions { get; set; } = [];
}

public sealed class EmployeePositionHistoryEmployeeOption
{
    public int Id { get; set; }
    public string PersonnelNumber { get; set; } = "";
    public string FullName { get; set; } = "";
}

public sealed class EmployeePositionHistoryRow
{
    public int EmployeeId { get; set; }
    public string PersonnelNumber { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public DateTime EffectiveDate { get; set; }
    public string ChangeType { get; set; } = "";
    public string FromPositionCode { get; set; } = "";
    public string FromPositionTitle { get; set; } = "";
    public string ToPositionCode { get; set; } = "";
    public string ToPositionTitle { get; set; } = "";
    public string RankType { get; set; } = "";
    public string RevisionCode { get; set; } = "";
    public string RevisionTitle { get; set; } = "";
}
