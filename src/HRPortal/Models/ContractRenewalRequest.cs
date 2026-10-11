using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public class ContractRenewalRequest
{
    public int Id { get; set; }
    [MaxLength(40)] public string RequestCode { get; set; } = "";
    public int EmployeeId { get; set; }
    public int? CreatedByEmployeeId { get; set; }
    public int? WorkflowInstanceId { get; set; }
    [MaxLength(40)] public string Status { get; set; } = "Submitted";

    [MaxLength(200)] public string EmployeeNameSnapshot { get; set; } = "";
    [MaxLength(50)] public string PersonnelNumberSnapshot { get; set; } = "";
    [MaxLength(200)] public string OrganizationUnitSnapshot { get; set; } = "";
    [MaxLength(300)] public string PositionTitleSnapshot { get; set; } = "";
    public DateTime? PreviousContractEndDate { get; set; }

    public int PreviousExtensionMonths1 { get; set; }
    public int PreviousExtensionMonths2 { get; set; }
    public int PreviousExtensionMonths3 { get; set; }

    public bool HasDisciplinaryWarnings { get; set; }
    public int DisciplinaryWarningCount { get; set; }
    public bool HasDisciplinaryCommitteeResolution { get; set; }
    public int DisciplinaryCommitteeResolutionCount { get; set; }
    public bool HasUnpaidLeave { get; set; }
    public int UnpaidLeaveDays { get; set; }
    public bool HasSickLeave { get; set; }
    public int SickLeaveDays { get; set; }
    public bool HasAccidentLeave { get; set; }
    public int AccidentLeaveDays { get; set; }
    public bool HasCommendations { get; set; }
    public int CommendationCount { get; set; }

    public decimal PreviousPeriodScore1 { get; set; }
    public decimal PreviousPeriodScore2 { get; set; }
    public bool PreviousPeriodScore1FromHistory { get; set; }
    public bool PreviousPeriodScore2FromHistory { get; set; }

    public decimal? FinalEvaluationScore { get; set; }
    [MaxLength(30)] public string? SecurityDecision { get; set; }
    [MaxLength(3000)] public string? SecurityComment { get; set; }
    public int? HrProposedContractMonths { get; set; }
    [MaxLength(3000)] public string? HrComment { get; set; }
    public int? FinalContractMonths { get; set; }
    [MaxLength(3000)] public string? CeoComment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public Employee Employee { get; set; } = null!;
    public Employee? CreatedByEmployee { get; set; }
    public WorkflowInstance? WorkflowInstance { get; set; }
}
