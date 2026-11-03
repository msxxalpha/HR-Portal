using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public static class MonthlyPerformanceImprovementStatuses
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static bool IsValid(string? status) =>
        status is InProgress or Completed or Cancelled;

    public static string ToPersian(string status) => status switch
    {
        InProgress => "در حال انجام",
        Completed => "پایان‌یافته",
        Cancelled => "لغوشده",
        _ => "نامشخص"
    };
}

public class MonthlyPerformanceImprovementAction
{
    public int Id { get; set; }
    public int ScoreId { get; set; }
    public int OwnerEmployeeId { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
    public DateTime DueDate { get; set; }
    [MaxLength(20)] public string Status { get; set; } = MonthlyPerformanceImprovementStatuses.InProgress;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public MonthlyPerformanceScore Score { get; set; } = null!;
    public Employee OwnerEmployee { get; set; } = null!;
}
