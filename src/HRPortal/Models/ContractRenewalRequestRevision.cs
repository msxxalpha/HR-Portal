using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public class ContractRenewalRequestRevision
{
    public int Id { get; set; }
    public int ContractRenewalRequestId { get; set; }
    public int RevisionNumber { get; set; }
    [Required, MaxLength(30)] public string Action { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public int? ChangedByEmployeeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
