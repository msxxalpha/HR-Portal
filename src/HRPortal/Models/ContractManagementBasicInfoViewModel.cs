namespace HRPortal.Models;

public class ContractManagementBasicInfoViewModel
{
    public List<EmployeePerformanceEvaluationQuestion> Questions { get; set; } = [];
    public EmployeePerformanceEvaluationQuestion EditingQuestion { get; set; } = new();
    public decimal ActiveMaxScoreTotal { get; set; }
    public int ActiveQuestionCount { get; set; }
    public string? ValidationMessage { get; set; }
}
