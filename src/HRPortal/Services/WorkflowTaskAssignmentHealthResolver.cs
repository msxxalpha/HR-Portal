using HRPortal.Models;

namespace HRPortal.Services;

/// <summary>
/// Determines whether a pending workflow task has a broken assignment that
/// requires managerial repair. The same resolver is used by the report and POST
/// endpoint so a forged task ID cannot bypass the report's eligibility rules.
/// </summary>
public static class WorkflowTaskAssignmentHealthResolver
{
    public static string? GetIssueCode(
        WorkflowTask task,
        IReadOnlyDictionary<string, OrganizationNode> currentPositions,
        IReadOnlyCollection<Employee> activeEmployees)
    {
        var activeIds = activeEmployees.Select(x => x.Id).ToHashSet();

        if (task.AssignedPositionId.HasValue)
        {
            var code = task.AssignedPosition?.Code ?? task.WorkflowStep.OrganizationNode?.Code;
            if (string.IsNullOrWhiteSpace(code) ||
                !currentPositions.TryGetValue(code.Trim(), out var currentPosition))
                return "PositionMissing";

            var assignedIds = (currentPosition.AssignedEmployeeIdsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => int.TryParse(value, out var parsedId) ? parsedId : 0)
                .ToList();
            if (assignedIds.Count == 0 && currentPosition.PositionHolderEmployeeId.HasValue)
                assignedIds.Add(currentPosition.PositionHolderEmployeeId.Value);
            if (assignedIds.Count == 0)
            {
                assignedIds = activeEmployees.Where(e =>
                    e.OrganizationUnitId == currentPosition.Id ||
                    e.OrganizationDepartmentId == currentPosition.Id ||
                    e.OrganizationSectionId == currentPosition.Id ||
                    e.OrganizationPositionId == currentPosition.Id)
                    .Select(e => e.Id).ToList();
            }

            var hasInvalidAssignment = assignedIds.Any(id => id <= 0 || !activeIds.Contains(id));
            var hasActiveHolder = assignedIds.Any(activeIds.Contains);
            if (!hasActiveHolder) return "NoActiveHolder";
            if (hasInvalidAssignment) return "InvalidAssignment";
            return null;
        }

        return !task.AssignedEmployeeId.HasValue || !activeIds.Contains(task.AssignedEmployeeId.Value)
            ? "EmployeeMissing"
            : null;
    }
}
