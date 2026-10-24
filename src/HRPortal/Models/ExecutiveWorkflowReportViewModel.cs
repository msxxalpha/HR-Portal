namespace HRPortal.Models;

public class ExecutiveWorkflowReportViewModel
{
    public string Direction { get; set; } = "all";
    public string Status { get; set; } = "all";
    public string Workflow { get; set; } = "all";
    public string Employee { get; set; } = "all";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public bool HasCeoNode { get; set; }
    public string? CeoTitle { get; set; }
    public int DirectPositionCount { get; set; }
    public int DirectEmployeeCount { get; set; }
    public int TotalCount { get; set; }
    public int IncomingCount { get; set; }
    public int OutgoingCount { get; set; }
    public int InProgressCount { get; set; }
    public int CompletedCount { get; set; }
    public int RejectedCount { get; set; }
    public int ReturnedCount { get; set; }
    public int PendingTaskCount { get; set; }
    public int AgingCount { get; set; }
    public double AverageCompletionDays { get; set; }
    public List<string> WorkflowOptions { get; set; } = [];
    public List<ExecutiveWorkflowEmployeeOption> EmployeeOptions { get; set; } = [];
    public List<ExecutiveWorkflowChartItem> StatusChart { get; set; } = [];
    public List<ExecutiveWorkflowChartItem> DirectionChart { get; set; } = [];
    public List<ExecutiveWorkflowChartItem> WorkflowChart { get; set; } = [];
    public List<ExecutiveWorkflowReportRow> Rows { get; set; } = [];
}

public class ExecutiveWorkflowEmployeeOption
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
}

public class ExecutiveWorkflowChartItem
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public string Color { get; set; } = "#64748b";
}

public class ExecutiveWorkflowReportRow
{
    public int Id { get; set; }
    public string RequestCode { get; set; } = "";
    public string WorkflowTitle { get; set; } = "";
    public string RequesterName { get; set; } = "";
    public string RequesterPersonnelNumber { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusTitle { get; set; } = "";
    public string CurrentStep { get; set; } = "";
    public string CurrentOwner { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int AgeDays { get; set; }
    public string QualitySignal { get; set; } = "";
    public string QualityClass { get; set; } = "";
    public string LastAction { get; set; } = "";
    public string? LastComment { get; set; }
}
