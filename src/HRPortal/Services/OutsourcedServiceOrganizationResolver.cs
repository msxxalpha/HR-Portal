using HRPortal.Models;

namespace HRPortal.Services;

/// <summary>
/// Resolves the reporting unit for an employee by walking upward from the
/// employee's position in the currently selected finalized organization tree.
/// The reporting unit is the nearest Management/Presidency node directly below
/// the CEO node.
/// </summary>
public static class OutsourcedServiceOrganizationResolver
{
    public const string UnassignedTitle = "بدون واحد سازمانی";

    public static string ResolveUnitTitle(
        int employeeId,
        IReadOnlyCollection<OrganizationNode> nodes,
        int? positionNodeId,
        int? sectionNodeId = null,
        int? departmentNodeId = null,
        int? unitNodeId = null)
    {
        if (nodes.Count == 0) return UnassignedTitle;

        var byId = nodes.Where(x => x.IsActive).ToDictionary(x => x.Id);
        var starts = new List<int>();
        foreach (var id in new[] { positionNodeId, sectionNodeId, departmentNodeId, unitNodeId })
            if (id.HasValue && byId.ContainsKey(id.Value) && !starts.Contains(id.Value))
                starts.Add(id.Value);

        // If the employee has no current position reference, honor explicit
        // assignments stored on a position node (including multi-assignee CSV).
        if (starts.Count == 0)
        {
            starts.AddRange(nodes.Where(x =>
                    WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds(
                        x.AssignedEmployeeIdsCsv, x.PositionHolderEmployeeId).Contains(employeeId))
                .Select(x => x.Id));
        }

        string? WalkToReportingUnit(int startId)
        {
            var visited = new HashSet<int>();
            var currentId = startId;
            while (visited.Add(currentId) && byId.TryGetValue(currentId, out var current))
            {
                if (IsReportingUnitRank(current.RankType) &&
                    current.ParentId.HasValue &&
                    byId.TryGetValue(current.ParentId.Value, out var parent) &&
                    NormalizeRank(parent.RankType) == NormalizeRank("مدیرعامل"))
                    return current.Title;

                if (!current.ParentId.HasValue) break;
                currentId = current.ParentId.Value;
            }
            return null;
        }

        foreach (var startId in starts)
        {
            var resolved = WalkToReportingUnit(startId);
            if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
        }

        // If the employee's saved position reference is stale or absent in the
        // selected revision, try the explicit employee assignments in the tree.
        foreach (var assignedNode in nodes.Where(x =>
                     WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds(
                         x.AssignedEmployeeIdsCsv, x.PositionHolderEmployeeId).Contains(employeeId)))
        {
            var resolved = WalkToReportingUnit(assignedNode.Id);
            if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
        }

        return UnassignedTitle;
    }

    private static bool IsReportingUnitRank(string? rank)
    {
        var normalized = NormalizeRank(rank);
        return normalized == NormalizeRank("مدیریت") || normalized == NormalizeRank("ریاست");
    }

    private static string NormalizeRank(string? value) =>
        (value ?? string.Empty).Trim()
            .Replace("ي", "ی", StringComparison.Ordinal)
            .Replace("ك", "ک", StringComparison.Ordinal)
            .Replace("‌", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("رییس", "رئیس", StringComparison.Ordinal);
}
