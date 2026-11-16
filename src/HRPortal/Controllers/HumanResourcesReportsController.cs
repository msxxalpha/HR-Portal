using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public class HumanResourcesReportsController(HRPortalDbContext db) : Controller
{
    private sealed record PositionSnapshot(string Code, string Title, string RankType);
    private sealed record EmployeeSnapshot(string PersonnelNumber, string FullName);

    [HttpGet]
    [Authorize(Policy = "Reports.EmployeePositionHistory.View")]
    public async Task<IActionResult> EmployeePositionHistory(
        int? employeeId,
        string? position,
        string? changeType,
        string? fromJalali,
        string? toJalali,
        CancellationToken cancellationToken)
    {
        DateTime? from = null;
        DateTime? to = null;
        if (!string.IsNullOrWhiteSpace(fromJalali))
        {
            if (PersianDateService.TryParse(fromJalali, out var parsedFrom))
                from = parsedFrom.Date;
            else
                ModelState.AddModelError(nameof(fromJalali), "تاریخ شروع شمسی معتبر نیست.");
        }
        if (!string.IsNullOrWhiteSpace(toJalali))
        {
            if (PersianDateService.TryParse(toJalali, out var parsedTo))
                to = parsedTo.Date;
            else
                ModelState.AddModelError(nameof(toJalali), "تاریخ پایان شمسی معتبر نیست.");
        }
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            ModelState.AddModelError(nameof(toJalali), "تاریخ پایان نباید پیش از تاریخ شروع باشد.");

        var model = new EmployeePositionHistoryReportViewModel
        {
            EmployeeId = employeeId,
            Position = position?.Trim() ?? "",
            ChangeType = string.IsNullOrWhiteSpace(changeType) ? "all" : changeType,
            From = from,
            To = to
        };

        var employees = await db.Employees.AsNoTracking()
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new
            {
                x.Id,
                x.PersonnelNumber,
                FullName = x.FirstName + " " + x.LastName
            })
            .ToListAsync(cancellationToken);
        var employeeById = employees.ToDictionary(x => x.Id);
        model.EmployeeOptions = employees.Select(x => new EmployeePositionHistoryEmployeeOption
        {
            Id = x.Id,
            PersonnelNumber = x.PersonnelNumber,
            FullName = x.FullName
        }).ToList();

        var assignmentSnapshots = await db.EmployeePositionAssignmentSnapshots.AsNoTracking()
            .OrderBy(x => x.EffectiveDate)
            .ThenBy(x => x.FinalizedAtSnapshot)
            .ThenBy(x => x.RevisionId)
            .ThenBy(x => x.EmployeeId)
            .ThenBy(x => x.PositionCode)
            .ToListAsync(cancellationToken);

        var revisionGroups = assignmentSnapshots
            .GroupBy(x => x.RevisionId)
            .Select(group => new
            {
                RevisionId = group.Key,
                Revision = group.First(),
                Assignments = group.Where(x => x.EmployeeId.HasValue &&
                    !string.IsNullOrWhiteSpace(x.PositionCode)).ToList()
            })
            .OrderBy(x => x.Revision.EffectiveDate)
            .ThenBy(x => x.Revision.FinalizedAtSnapshot)
            .ThenBy(x => x.RevisionId)
            .ToList();

        if (revisionGroups.Count == 0)
        {
            ViewBag.NoFinalizedStructure = true;
            return View(model);
        }

        var previousAssignments = new Dictionary<int, Dictionary<string, PositionSnapshot>>();
        var previousEmployees = new Dictionary<int, EmployeeSnapshot>();
        var history = new List<EmployeePositionHistoryRow>();

        foreach (var revisionGroup in revisionGroups)
        {
            var revision = revisionGroup.Revision;
            var currentAssignments = new Dictionary<int, Dictionary<string, PositionSnapshot>>();
            var currentEmployees = new Dictionary<int, EmployeeSnapshot>();

            foreach (var snapshot in revisionGroup.Assignments)
            {
                var employeeIdValue = snapshot.EmployeeId!.Value;
                if (!currentAssignments.TryGetValue(employeeIdValue, out var positions))
                {
                    positions = new Dictionary<string, PositionSnapshot>(StringComparer.OrdinalIgnoreCase);
                    currentAssignments[employeeIdValue] = positions;
                }

                positions[snapshot.PositionCode] = new PositionSnapshot(
                    snapshot.PositionCode, snapshot.PositionTitle, snapshot.RankType);
                currentEmployees[employeeIdValue] = new EmployeeSnapshot(
                    snapshot.PersonnelNumberSnapshot, snapshot.EmployeeNameSnapshot);
            }

            var changedEmployeeIds = previousAssignments.Keys
                .Union(currentAssignments.Keys).Distinct().ToArray();

            foreach (var changedEmployeeId in changedEmployeeIds)
            {
                var previous = previousAssignments.GetValueOrDefault(changedEmployeeId)
                    ?? new Dictionary<string, PositionSnapshot>(StringComparer.OrdinalIgnoreCase);
                var current = currentAssignments.GetValueOrDefault(changedEmployeeId)
                    ?? new Dictionary<string, PositionSnapshot>(StringComparer.OrdinalIgnoreCase);

                var removed = previous.Where(x => !current.ContainsKey(x.Key))
                    .Select(x => x.Value).OrderBy(x => x.Code).ToList();
                var added = current.Where(x => !previous.ContainsKey(x.Key))
                    .Select(x => x.Value).OrderBy(x => x.Code).ToList();

                void AddHistory(string type, PositionSnapshot? oldPosition, PositionSnapshot? newPosition)
                {
                    var person = currentEmployees.GetValueOrDefault(changedEmployeeId)
                        ?? previousEmployees.GetValueOrDefault(changedEmployeeId);
                    if (person is null) return;

                    history.Add(new EmployeePositionHistoryRow
                    {
                        EmployeeId = changedEmployeeId,
                        PersonnelNumber = person.PersonnelNumber,
                        EmployeeName = person.FullName,
                        EffectiveDate = revision.EffectiveDate,
                        ChangeType = type,
                        FromPositionCode = oldPosition?.Code ?? "",
                        FromPositionTitle = oldPosition?.Title ?? "",
                        ToPositionCode = newPosition?.Code ?? "",
                        ToPositionTitle = newPosition?.Title ?? "",
                        RankType = newPosition?.RankType ?? oldPosition?.RankType ?? "",
                        RevisionCode = revision.RevisionCodeSnapshot,
                        RevisionTitle = revision.RevisionTitleSnapshot
                    });
                }

                // Pair removals and additions as one transfer where possible.
                var transferCount = Math.Min(removed.Count, added.Count);
                for (var index = 0; index < transferCount; index++)
                    AddHistory("Transfer", removed[index], added[index]);

                for (var index = transferCount; index < added.Count; index++)
                    AddHistory("Assigned", null, added[index]);

                for (var index = transferCount; index < removed.Count; index++)
                    AddHistory("Unassigned", removed[index], null);
            }

            previousAssignments = currentAssignments;
            previousEmployees = currentEmployees;
        }

        IEnumerable<EmployeePositionHistoryRow> filtered = history;
        if (employeeId.HasValue)
            filtered = filtered.Where(x => x.EmployeeId == employeeId.Value);
        if (!string.IsNullOrWhiteSpace(model.Position))
        {
            var term = model.Position;
            filtered = filtered.Where(x =>
                x.FromPositionTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.ToPositionTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.FromPositionCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.ToPositionCode.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (model.ChangeType is "Transfer" or "Assigned" or "Unassigned")
            filtered = filtered.Where(x => x.ChangeType == model.ChangeType);
        if (from.HasValue)
            filtered = filtered.Where(x => x.EffectiveDate.Date >= from.Value);
        if (to.HasValue)
            filtered = filtered.Where(x => x.EffectiveDate.Date <= to.Value);

        model.Rows = filtered
            .OrderByDescending(x => x.EffectiveDate)
            .ThenBy(x => x.EmployeeName)
            .ThenBy(x => x.PersonnelNumber)
            .ToList();

        return View(model);
    }
}
