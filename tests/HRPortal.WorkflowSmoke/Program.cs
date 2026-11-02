using HRPortal.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// Multi-assignee position: every active listed employee can act on the same
// position task, duplicates and malformed tokens cannot create extra identities.
var shared = WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds("101, 102,101,broken,-5", 999);
Check(shared.SequenceEqual(new[] { 101, 102 }), "Explicit multi-assignee list was not normalized.");
Check(WorkflowPositionAssignmentResolver.IsPositionAssignee(101, true, "101,102", 999),
    "First explicit position assignee was denied.");
Check(WorkflowPositionAssignmentResolver.IsPositionAssignee(102, true, "101,102", 999),
    "Second explicit position assignee was denied.");
Check(!WorkflowPositionAssignmentResolver.IsPositionAssignee(999, true, "101,102", 999),
    "Stale position holder must not override a non-empty explicit assignee list.");
Check(!WorkflowPositionAssignmentResolver.IsPositionAssignee(101, false, "101,102", null),
    "Inactive employees must never be position assignees.");

// Single-holder fallback is used only when the explicit CSV has no valid IDs.
Check(WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds(null, 201).SequenceEqual(new[] { 201 }),
    "Position-holder fallback failed.");
Check(WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds("invalid,0,-1", 201).SequenceEqual(new[] { 201 }),
    "Invalid CSV tokens should fall back to the designated holder.");
Check(WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds("301,302", null).SequenceEqual(new[] { 301, 302 }),
    "A position with multiple explicit assignees was not resolved.");
Check(WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds("301,301,302", null).SequenceEqual(new[] { 301, 302 }),
    "Duplicate employee IDs were not removed.");

// Employee-routed tasks are deliberately person-specific. Being a position
// holder or sharing the same department must not grant access to these tasks.
Check(WorkflowPositionAssignmentResolver.IsExplicitEmployeeAssignee(401, 401, true),
    "The explicitly assigned active employee was denied.");
Check(!WorkflowPositionAssignmentResolver.IsExplicitEmployeeAssignee(402, 401, true),
    "A colleague was granted an employee-specific task.");
Check(!WorkflowPositionAssignmentResolver.IsExplicitEmployeeAssignee(401, 401, false),
    "An inactive employee retained access to an employee-specific task.");
Check(!WorkflowPositionAssignmentResolver.IsExplicitEmployeeAssignee(0, 0, true),
    "An invalid employee identity was accepted.");



Check(WorkflowPositionAssignmentResolver.CanViewInstance(
        isRequester: true, hasActedPreviously: false, isCurrentAssigneeOfPendingTask: false),
    "A requester must be able to view their own request.");
Check(WorkflowPositionAssignmentResolver.CanViewInstance(
        isRequester: false, hasActedPreviously: true, isCurrentAssigneeOfPendingTask: false),
    "A previous actor must retain read access to the workflow history.");
Check(WorkflowPositionAssignmentResolver.CanViewInstance(
        isRequester: false, hasActedPreviously: false, isCurrentAssigneeOfPendingTask: true),
    "The current assignee of a pending task must be able to view the instance.");
Check(!WorkflowPositionAssignmentResolver.CanViewInstance(
        isRequester: false, hasActedPreviously: false, isCurrentAssigneeOfPendingTask: false),
    "An unrelated employee must not view another employee's workflow instance.");


var reportRows = new[]
{
    new HRPortal.Models.UnassignedWorkflowTaskRow
    {
        TaskId = 1, WorkflowId = 10, WorkflowTitle = "تمدید قرارداد",
        EntityType = "ContractRenewal", EntityId = "CR-100",
        RequesterName = "کارمند الف", StepTitle = "حراست",
        PositionCode = "SEC-01", PositionTitle = "حراست",
        CreatedAt = new DateTime(2026, 10, 10, 9, 0, 0),
        ReasonCode = "NoActiveHolder", Reason = "بدون متصدی"
    },
    new HRPortal.Models.UnassignedWorkflowTaskRow
    {
        TaskId = 2, WorkflowId = 20, WorkflowTitle = "درخواست دیگر",
        EntityType = "Other", EntityId = "REQ-200",
        RequesterName = "کارمند ب", StepTitle = "مدیر منابع انسانی",
        PositionCode = "HR-02", PositionTitle = "منابع انسانی",
        CreatedAt = new DateTime(2026, 10, 12, 9, 0, 0),
        ReasonCode = "PositionMissing", Reason = "جایگاه ناموجود"
    }
};
var filteredReport = UnassignedWorkflowTaskFilter.Apply(
    reportRows,
    query: "CR-100",
    reasonCode: "NoActiveHolder",
    workflowId: 10,
    step: "حراست",
    position: "sec-01",
    createdFrom: new DateTime(2026, 10, 10),
    createdTo: new DateTime(2026, 10, 10));
Check(filteredReport.Count == 1 && filteredReport[0].TaskId == 1,
    "Combined report filters for request, reason, workflow, step, position and inclusive date range failed.");
Check(UnassignedWorkflowTaskFilter.Apply(
        reportRows, createdFrom: new DateTime(2026, 10, 11)).Select(x => x.TaskId).SequenceEqual(new[] { 2 }),
    "Created-from date filter failed.");
Check(UnassignedWorkflowTaskFilter.Apply(
        reportRows, createdTo: new DateTime(2026, 10, 10)).Select(x => x.TaskId).SequenceEqual(new[] { 1 }),
    "Created-to date filter failed.");

var selectedRevision = WorkflowPositionAssignmentResolver.SelectCurrentRevisionId(
    new[]
    {
        (Id: 10, EffectiveDate: new DateTime(2026, 1, 1), IsFinalized: true),
        (Id: 11, EffectiveDate: new DateTime(2026, 3, 1), IsFinalized: true),
        (Id: 12, EffectiveDate: new DateTime(2026, 3, 1), IsFinalized: true),
        (Id: 13, EffectiveDate: new DateTime(2026, 4, 1), IsFinalized: false),
        (Id: 14, EffectiveDate: new DateTime(2026, 5, 1), IsFinalized: true)
    },
    new DateTime(2026, 3, 1));
Check(selectedRevision == 12,
    "Current revision must be the latest effective finalized revision, using highest ID as a same-date tie-breaker.");
Check(WorkflowPositionAssignmentResolver.SelectCurrentRevisionId(
        new[] { (Id: 15, EffectiveDate: new DateTime(2026, 6, 1), IsFinalized: true) },
        new DateTime(2026, 3, 1)) is null,
    "A future-effective revision must not become current.");

var positionCandidates = new[]
{
    new { Id = 20, Code = "ORG-HR-MANAGER" },
    new { Id = 21, Code = "ORG-FINANCE" }
};
var stableMatch = WorkflowPositionAssignmentResolver.FindByStableCode(
    positionCandidates, " org-hr-manager ", x => x.Code);
Check(stableMatch?.Id == 20, "Stable position codes must match case-insensitively after trimming.");
Check(WorkflowPositionAssignmentResolver.FindByStableCode(
        positionCandidates, "ORG-REMOVED", x => x.Code) is null,
    "A removed position must not fall back to a similarly numbered or unrelated position.");

Console.WriteLine("PASS: multi-assignee position ownership, holder fallback, malformed/duplicate IDs,");
Console.WriteLine("      explicit employee-only routing, inactive/invalid employee denial,");
Console.WriteLine("      effective revision selection, stable-code mapping, and instance visibility;");
Console.WriteLine("      unassigned report filtering by request, reason, workflow, step, position, and date range.");
