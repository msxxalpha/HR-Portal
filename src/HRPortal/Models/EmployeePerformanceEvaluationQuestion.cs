using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public class EmployeePerformanceEvaluationQuestion
{
    public int Id { get; set; }

    [Required, MaxLength(1000)]
    public string QuestionText { get; set; } = "";

    public decimal MaxScore { get; set; }

    [Required, MaxLength(4000)]
    public string ScoringGuidance { get; set; } = "";

    public int SortOrder { get; set; } = 10;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
