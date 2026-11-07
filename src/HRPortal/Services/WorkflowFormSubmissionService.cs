using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

/// <summary>
/// Shared entry point used by code-first custom forms after their business record
/// has been saved. It discovers the active workflow by FormCode and prevents
/// duplicate active instances for the same business record.
/// </summary>
public sealed class WorkflowFormSubmissionService(
    HRPortalDbContext db,
    WorkflowService workflow)
{
    public async Task<WorkflowInstance?> StartForFormAsync(
        string formCode,
        string entityType,
        string entityId,
        int requesterEmployeeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(formCode) ||
            string.IsNullOrWhiteSpace(entityType) ||
            string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("مشخصات فرم یا شناسه درخواست برای شروع گردش کار کامل نیست.");

        var definitionCode = await db.WorkflowDefinitions.AsNoTracking()
            .Where(x => x.FormCode == formCode && x.IsActive && !x.IsRuntimeInstance)
            .OrderByDescending(x => x.Version)
            .Select(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(definitionCode))
            return null;

        var existing = await db.WorkflowInstances
            .Where(x => x.EntityType == entityType && x.EntityId == entityId &&
                x.WorkflowDefinition.FormCode == formCode)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return existing;

        return await workflow.StartAsync(
            definitionCode, entityType, entityId, requesterEmployeeId, cancellationToken);
    }
}
