namespace HRPortal.Models;

/// <summary>
/// Immutable assignment snapshot captured when an organization revision is finalized.
/// It intentionally has no foreign key to the source revision or employee so that
/// historical records survive later revision cleanup and employee deactivation/deletion.
/// </summary>
public sealed class EmployeePositionAssignmentSnapshot
{
    public int Id { get; set; }
    public int RevisionId { get; set; }
    public string RevisionCodeSnapshot { get; set; } = "";
    public string RevisionTitleSnapshot { get; set; } = "";
    public DateTime EffectiveDate { get; set; }
    public DateTime? FinalizedAtSnapshot { get; set; }
    public int? EmployeeId { get; set; }
    public string PersonnelNumberSnapshot { get; set; } = "";
    public string EmployeeNameSnapshot { get; set; } = "";
    public string PositionCode { get; set; } = "";
    public string PositionTitle { get; set; } = "";
    public string RankType { get; set; } = "";
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}
