namespace HRPortal.Models;

public class SuggestionsDashboardViewModel
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string Category { get; set; } = "all";
    public string Status { get; set; } = "all";
    public int TotalCount { get; set; }
    public int DraftCount { get; set; }
    public int SubmittedCount { get; set; }
    public int InEvaluationCount { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int UniqueProposers { get; set; }
    public int CollaborativeCount { get; set; }
    public int AttachmentCount { get; set; }
    public int SubmittedLast30Days { get; set; }
    public List<SuggestionDashboardBucket> StatusBuckets { get; set; } = [];
    public List<SuggestionDashboardBucket> CategoryBuckets { get; set; } = [];
    public List<SuggestionDashboardBucket> MonthlyBuckets { get; set; } = [];
    public List<SuggestionDashboardBucket> BenefitBuckets { get; set; } = [];
    public List<SuggestionDashboardBucket> ResourceBuckets { get; set; } = [];
    public List<SuggestionDashboardRow> Rows { get; set; } = [];
}

public class SuggestionDashboardBucket
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public class SuggestionDashboardRow
{
    public int Id { get; set; }
    public string SuggestionNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Status { get; set; } = "";
    public int ProposerEmployeeId { get; set; }
    public string ProposerName { get; set; } = "";
    public string PersonnelNumber { get; set; } = "";
    public string Organization { get; set; } = "—";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public int MemberCount { get; set; }
    public int AttachmentCount { get; set; }
    public string BenefitsJson { get; set; } = "[]";
    public string ResourceTypesJson { get; set; } = "[]";
}
