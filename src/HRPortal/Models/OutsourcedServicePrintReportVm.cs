namespace HRPortal.Models;

public sealed class OutsourcedServicePrintReportVm
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string ServiceDescription { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal Quantity { get; set; }
    public string NeedByDate { get; set; } = "";
    public string Priority { get; set; } = "";
    public string RequesterName { get; set; } = "";
    public string PersonnelNumber { get; set; } = "";
    public string OrganizationalUnit { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string WorkflowStatus { get; set; } = "";
    public string WorkflowStatusTitle { get; set; } = "";
    public string WorkflowStartedAt { get; set; } = "";
    public string WorkflowCompletedAt { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public List<OutsourcedServicePrintHistoryRow> History { get; set; } = [];
    public List<string> Attachments { get; set; } = [];
}

public sealed class OutsourcedServicePrintHistoryRow
{
    public int Sequence { get; set; }
    public string Stage { get; set; } = "";
    public string Actor { get; set; } = "";
    public string PersonnelNumber { get; set; } = "";
    public string Action { get; set; } = "";
    public string Date { get; set; } = "";
    public string Comment { get; set; } = "";
}
