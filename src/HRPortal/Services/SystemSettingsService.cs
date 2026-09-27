using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class SystemSettingsService(HRPortalDbContext db)
{
    public Task<SystemSettings> GetAsync() => db.SystemSettings.FirstOrDefaultAsync() ?? Task.FromResult(new SystemSettings());
    public async Task SaveAsync(SystemSettings model)
    {
        var current = await db.SystemSettings.FirstOrDefaultAsync();
        if (current is null) db.SystemSettings.Add(model);
        else {
            model.Id = current.Id;
            db.Entry(current).CurrentValues.SetValues(model);
            current.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }
}