namespace HRPortal.Models;

public class ContractRenewalIntakeViewModel
{
    public int EmployeeId { get; set; }
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
    public bool ShowPreview { get; set; }

    public List<Employee> Employees { get; set; } = [];
    public Employee? SelectedEmployee { get; set; }
    public List<decimal> PreviousScoresFound { get; set; } = [];
    public List<EmployeePerformanceEvaluationQuestion> ActiveQuestions { get; set; } = [];
    public decimal ActiveQuestionsTotal { get; set; }
    public List<ContractRenewalRequest> RecentRequests { get; set; } = [];
    public bool WorkflowSettingsReady { get; set; }
    public bool QuestionsReady { get; set; }
    public string? ErrorMessage { get; set; }
}
