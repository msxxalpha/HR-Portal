using HRPortal.Models;

namespace HRPortal.Services;

/// <summary>
/// Resolves reporting relationships for monthly performance evaluations.
/// Position assignment is authoritative; HR unit/department/section IDs only
/// locate staff who have no explicit position in the finalized structure.
/// </summary>
public static class MonthlyPerformanceScopeResolver
{
    public sealed record Node(int Id, int? ParentId, string RankType,
        int? PositionHolderEmployeeId, string? AssignedEmployeeIdsCsv);

    public sealed record Person(int Id, string Status, int? PositionId, int? SectionId,
        int? DepartmentId, int? UnitId, int? PerformanceRankId, string? PositionTitle);

    public sealed record Rank(int Id, string Title);
    public sealed record Job(string Title, string? Grade);
    public sealed record Entry(int EmployeeId, string RankTitle, int NodeId, int Depth);

    private enum PlacementKind { AssignedPosition, UnitMembership }

    private sealed record Placement(int NodeId, PlacementKind Kind);

    public static List<Entry> Resolve(
        int evaluatorId,
        IReadOnlyCollection<Node> currentNodes,
        IReadOnlyCollection<Person> employees,
        IReadOnlyCollection<Rank> activeRanks,
        IReadOnlyCollection<Job> jobs)
    {
        var people = employees.Where(e => e.Status == "فعال").ToDictionary(e => e.Id);
        if (!people.ContainsKey(evaluatorId)) return [];

        // Only the active nodes of the current finalized revision are passed here.
        // Never mix assignments from previous revisions with the current hierarchy.
        var nodes = currentNodes.ToDictionary(n => n.Id);
        if (nodes.Count == 0) return [];

        var directPositions = new Dictionary<int, HashSet<int>>();
        void Attach(int employeeId, int nodeId)
        {
            if (!people.ContainsKey(employeeId)) return;
            if (!directPositions.TryGetValue(employeeId, out var positions))
                directPositions[employeeId] = positions = [];
            positions.Add(nodeId);
        }

        foreach (var node in nodes.Values)
        {
            if (node.PositionHolderEmployeeId is int holder)
                Attach(holder, node.Id);
            foreach (var raw in (node.AssignedEmployeeIdsCsv ?? "").Split(
                [',', '،', ';', '؛'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(raw, out var id)) Attach(id, node.Id);
        }

        // A branch is usable only if every ancestor is present and cycles are absent.
        var depths = new Dictionary<int, int>();
        int Depth(int nodeId)
        {
            if (depths.TryGetValue(nodeId, out var known)) return known;
            var seen = new HashSet<int>();
            var current = nodeId;
            var depth = 0;
            while (nodes.TryGetValue(current, out var node) && seen.Add(current))
            {
                if (node.ParentId is null)
                {
                    depths[nodeId] = depth;
                    return depth;
                }
                current = node.ParentId.Value;
                depth++;
            }
            depths[nodeId] = -1;
            return -1;
        }

        // Explicit assignments (position holder or position's employee list) take
        // precedence over department/unit membership. The latter is intentionally
        // not treated as being the manager/holder of that organizational node.
        Placement? Locate(Person person)
        {
            var explicitIds = new HashSet<int>();
            if (person.PositionId is int positionId && nodes.ContainsKey(positionId))
                explicitIds.Add(positionId);
            if (directPositions.TryGetValue(person.Id, out var directIds))
                explicitIds.UnionWith(directIds);

            var explicitNode = explicitIds.Where(id => Depth(id) >= 0)
                .OrderByDescending(Depth).ThenBy(id => id).FirstOrDefault();
            if (explicitNode != 0)
                return new Placement(explicitNode, PlacementKind.AssignedPosition);

            foreach (var id in new[] { person.SectionId, person.DepartmentId, person.UnitId })
                if (id is int value && nodes.ContainsKey(value) && Depth(value) >= 0)
                    return new Placement(value, PlacementKind.UnitMembership);
            return null;
        }

        var placements = people.Values.ToDictionary(e => e.Id, Locate);
        var evaluatorPlacement = placements[evaluatorId];
        if (evaluatorPlacement is null) return [];
        var evaluatorNodeId = evaluatorPlacement.NodeId;

        bool IsStrictDescendant(int candidateNodeId)
        {
            if (candidateNodeId == evaluatorNodeId) return false;
            var seen = new HashSet<int>();
            var cursor = candidateNodeId;
            while (nodes.TryGetValue(cursor, out var node) && seen.Add(cursor) &&
                   node.ParentId is int parentId)
            {
                if (parentId == evaluatorNodeId) return true;
                cursor = parentId;
            }
            return false;
        }

        // Staff listed only as members of their manager's department/section are
        // below its *actual holder*, not peers of arbitrary employees in that node.
        // Other explicit position occupants in that same node are always peers.
        bool IsStaffUnderTheHolder(Person person, Placement candidate)
            => candidate.Kind == PlacementKind.UnitMembership &&
               candidate.NodeId == evaluatorNodeId &&
               evaluatorPlacement.Kind == PlacementKind.AssignedPosition &&
               nodes[evaluatorNodeId].PositionHolderEmployeeId == evaluatorId;

        var rankNames = activeRanks.ToDictionary(r => r.Id, r => r.Title);
        var rankByTitle = activeRanks.GroupBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Title, StringComparer.OrdinalIgnoreCase);
        var gradesByJob = jobs.Where(j => !string.IsNullOrWhiteSpace(j.Title))
            .GroupBy(j => j.Title.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Grade, StringComparer.OrdinalIgnoreCase);

        string RankFor(Person person, int placementNodeId)
        {
            if (person.PerformanceRankId is int rankId &&
                rankNames.TryGetValue(rankId, out var explicitRank)) return explicitRank;

            var title = person.PositionTitle?.Trim() ?? "";
            if (title.Length > 0)
            {
                if (rankByTitle.TryGetValue(title, out var exact)) return exact;
                if (gradesByJob.TryGetValue(title, out var grade) && grade is not null &&
                    rankByTitle.TryGetValue(grade.Trim(), out var graded)) return graded;
                var prefix = activeRanks.Where(rank =>
                    title.StartsWith(rank.Title + " ", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(rank => rank.Title.Length).FirstOrDefault();
                if (prefix is not null) return prefix.Title;
            }
            return nodes[placementNodeId].RankType;
        }

        return people.Values.Where(person => person.Id != evaluatorId)
            .Select(person => (person, location: placements[person.Id]))
            .Where(item => item.location is not null &&
                (IsStrictDescendant(item.location.NodeId) ||
                 IsStaffUnderTheHolder(item.person, item.location)))
            .Select(item => new Entry(item.person.Id,
                RankFor(item.person, item.location!.NodeId),
                item.location.NodeId, Depth(item.location.NodeId)))
            .OrderBy(x => x.Depth).ThenBy(x => x.EmployeeId)
            .ToList();
    }
}
