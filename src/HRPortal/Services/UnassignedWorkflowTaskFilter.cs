using HRPortal.Models;

namespace HRPortal.Services;

public static class UnassignedWorkflowTaskFilter
{
    public static List<UnassignedWorkflowTaskRow> Apply(
        IEnumerable<UnassignedWorkflowTaskRow> source,
        string? query = null,
        string? reasonCode = null,
        int? workflowId = null,
        string? step = null,
        string? position = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null)
    {
        IEnumerable<UnassignedWorkflowTaskRow> rows = source;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            rows = rows.Where(x =>
                Contains(x.WorkflowTitle, term) ||
                Contains(x.EntityType, term) ||
                Contains(x.EntityId, term) ||
                Contains(x.RequesterName, term) ||
                Contains(x.StepTitle, term) ||
                Contains(x.PositionTitle, term));
        }

        if (!string.IsNullOrWhiteSpace(reasonCode))
            rows = rows.Where(x => string.Equals(x.ReasonCode, reasonCode, StringComparison.OrdinalIgnoreCase));
        if (workflowId.HasValue)
            rows = rows.Where(x => x.WorkflowId == workflowId.Value);
        if (!string.IsNullOrWhiteSpace(step))
            rows = rows.Where(x => Contains(x.StepTitle, step.Trim()));
        if (!string.IsNullOrWhiteSpace(position))
        {
            var term = position.Trim();
            rows = rows.Where(x => Contains(x.PositionCode, term) || Contains(x.PositionTitle, term));
        }
        if (createdFrom.HasValue)
            rows = rows.Where(x => x.CreatedAt.Date >= createdFrom.Value.Date);
        if (createdTo.HasValue)
            rows = rows.Where(x => x.CreatedAt.Date <= createdTo.Value.Date);

        return rows.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.TaskId).ToList();
    }

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
