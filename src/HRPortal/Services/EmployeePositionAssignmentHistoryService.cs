using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

/// <summary>
/// Captures immutable employee/position assignments for finalized organization
/// revisions. Records are deliberately independent of revision/employee foreign
/// keys, allowing the HR history report to survive later administrative cleanup.
/// </summary>
public static class EmployeePositionAssignmentHistoryService
{
    public static async Task BackfillMissingRevisionsAsync(HRPortalDbContext db)
    {
        var revisionIds = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized)
            .OrderBy(x => x.EffectiveDate).ThenBy(x => x.FinalizedAt).ThenBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        foreach (var revisionId in revisionIds)
            await CaptureRevisionAsync(db, revisionId);
    }

    public static async Task CaptureRevisionAsync(HRPortalDbContext db, int revisionId)
    {
        if (await db.EmployeePositionAssignmentSnapshots.AnyAsync(x => x.RevisionId == revisionId))
            return;

        var revision = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.Id == revisionId && x.IsFinalized)
            .Select(x => new
            {
                x.Id,
                x.RevisionCode,
                x.Title,
                x.EffectiveDate,
                x.FinalizedAt
            })
            .FirstOrDefaultAsync();
        if (revision is null) return;

        var nodes = await db.OrganizationNodes.AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == revisionId && x.IsActive)
            .Select(x => new
            {
                x.Code,
                x.Title,
                x.RankType,
                x.PositionHolderEmployeeId,
                x.AssignedEmployeeIdsCsv
            })
            .ToListAsync();

        var assignments = nodes
            .SelectMany(node => WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds(
                    node.AssignedEmployeeIdsCsv, node.PositionHolderEmployeeId)
                .Select(employeeId => new
                {
                    EmployeeId = employeeId,
                    node.Code,
                    node.Title,
                    node.RankType
                }))
            .ToList();

        var employeeIds = assignments.Select(x => x.EmployeeId).Distinct().ToArray();
        var employees = employeeIds.Length == 0
            ? new Dictionary<int, (string PersonnelNumber, string FullName)>()
            : await db.Employees.AsNoTracking()
                .Where(x => employeeIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.PersonnelNumber,
                    FullName = x.FirstName + " " + x.LastName
                })
                .ToDictionaryAsync(
                    x => x.Id,
                    x => (x.PersonnelNumber, x.FullName));

        var capturedAt = DateTime.UtcNow;
        var rows = new List<EmployeePositionAssignmentSnapshot>();
        foreach (var assignment in assignments)
        {
            employees.TryGetValue(assignment.EmployeeId, out var employee);
            rows.Add(new EmployeePositionAssignmentSnapshot
            {
                RevisionId = revision.Id,
                RevisionCodeSnapshot = revision.RevisionCode,
                RevisionTitleSnapshot = revision.Title,
                EffectiveDate = revision.EffectiveDate,
                FinalizedAtSnapshot = revision.FinalizedAt,
                EmployeeId = assignment.EmployeeId,
                PersonnelNumberSnapshot = employee.PersonnelNumber ?? $"شناسه {assignment.EmployeeId}",
                EmployeeNameSnapshot = string.IsNullOrWhiteSpace(employee.FullName)
                    ? $"کارمند با شناسه {assignment.EmployeeId}"
                    : employee.FullName,
                PositionCode = assignment.Code,
                PositionTitle = assignment.Title,
                RankType = assignment.RankType,
                CapturedAtUtc = capturedAt
            });
        }

        // Keep a marker row for revisions with no assigned positions. It allows
        // the report to detect removals made by an otherwise empty structure.
        if (rows.Count == 0)
        {
            rows.Add(new EmployeePositionAssignmentSnapshot
            {
                RevisionId = revision.Id,
                RevisionCodeSnapshot = revision.RevisionCode,
                RevisionTitleSnapshot = revision.Title,
                EffectiveDate = revision.EffectiveDate,
                FinalizedAtSnapshot = revision.FinalizedAt,
                EmployeeId = null,
                PersonnelNumberSnapshot = "",
                EmployeeNameSnapshot = "",
                PositionCode = "",
                PositionTitle = "",
                RankType = "",
                CapturedAtUtc = capturedAt
            });
        }

        db.EmployeePositionAssignmentSnapshots.AddRange(rows);
        await db.SaveChangesAsync();
    }
}
