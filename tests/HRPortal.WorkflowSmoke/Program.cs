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

Console.WriteLine("PASS: multi-assignee position ownership, holder fallback, malformed/duplicate IDs,");
Console.WriteLine("      explicit employee-only routing, and inactive/invalid employee denial.");
