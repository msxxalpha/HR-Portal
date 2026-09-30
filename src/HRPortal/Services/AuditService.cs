using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Services;

public class AuditService(HRPortalDbContext db, IHttpContextAccessor http)
{
    public async Task WriteAsync(string action, string entity, string? entityId = null, string? description = null, int? employeeId = null)
    {
        var settings = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(); if (settings?.EnableAuditLog == false) return;
        if (employeeId is null && int.TryParse(http.HttpContext?.User.FindFirst("EmployeeId")?.Value, out var id)) employeeId = id;
        db.AuditLogs.Add(new AuditLog { EmployeeId = employeeId, Action = action, EntityName = entity, EntityId = entityId, Description = description, IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString() }); await db.SaveChangesAsync();
    }
}