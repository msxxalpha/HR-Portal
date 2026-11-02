namespace HRPortal.Services;

/// <summary>
/// Shared, side-effect-free assignment rules for workflow tasks.
/// Position ownership is resolved from the current effective organization revision
/// by the caller; this type only interprets the assignment on that resolved position.
/// </summary>
public static class WorkflowPositionAssignmentResolver
{
    public static List<int> GetAssignedEmployeeIds(string? assignedEmployeeIdsCsv, int? positionHolderEmployeeId)
    {
        var ids = (assignedEmployeeIdsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0 && positionHolderEmployeeId is > 0)
            ids.Add(positionHolderEmployeeId.Value);

        return ids;
    }

    public static bool IsPositionAssignee(
        int employeeId,
        bool isActiveEmployee,
        string? assignedEmployeeIdsCsv,
        int? positionHolderEmployeeId)
    {
        if (employeeId <= 0 || !isActiveEmployee)
            return false;

        return GetAssignedEmployeeIds(assignedEmployeeIdsCsv, positionHolderEmployeeId)
            .Contains(employeeId);
    }

    public static bool IsExplicitEmployeeAssignee(
        int employeeId,
        int? assignedEmployeeId,
        bool isActiveEmployee) =>
        employeeId > 0 && isActiveEmployee && assignedEmployeeId == employeeId;
}
