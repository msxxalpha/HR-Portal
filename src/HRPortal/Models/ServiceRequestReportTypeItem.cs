namespace HRPortal.Models;

/// <summary>
/// Describes one request type that exposes reports and dashboards.
/// Adding a new request type means registering another item in the report catalog.
/// </summary>
public sealed class ServiceRequestReportTypeItem
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string Icon { get; set; } = "▥";
    public string Category { get; set; } = "";
}
