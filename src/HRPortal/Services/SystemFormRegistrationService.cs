using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

/// <summary>
/// Synchronizes code-declared form metadata with SystemFormDefinitions.
/// No user-maintained form builder or manual metadata entry is involved.
/// </summary>
public sealed class SystemFormRegistrationService(
    HRPortalDbContext db,
    IEnumerable<IWorkflowFormDefinitionProvider> providers)
{
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        foreach (var provider in providers)
        {
            var code = provider.Code.Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(provider.Title))
                throw new InvalidOperationException("مشخصات یکی از فرم‌های سیستمی معتبر نیست.");

            var entity = await db.SystemFormDefinitions
                .FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

            if (entity is null)
            {
                entity = new SystemFormDefinition
                {
                    Code = code,
                    CreatedAt = DateTime.UtcNow
                };
                db.SystemFormDefinitions.Add(entity);
            }
            else if (entity.ImplementationType == "Custom")
            {
                // Never convert an independently implemented legacy form.
                continue;
            }

            entity.Title = provider.Title.Trim();
            entity.Description = provider.Description.Trim();
            entity.ImplementationType = "GenericWorkflow";
            entity.IsActive = provider.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
