using HRPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class ReportService(HRPortalDbContext db)
{
    public async Task<string?> BuildUrlAsync(string yearMonth, string personnelNo)
    {
        var settings = await db.PayrollReportSettings.AsNoTracking().FirstOrDefaultAsync();

        if (settings is null || !settings.Enabled)
            return null;

        var target = settings.ReportUrl?.Trim();

        if (string.IsNullOrWhiteSpace(target))
        {
            if (string.IsNullOrWhiteSpace(settings.ReportServerUrl) || string.IsNullOrWhiteSpace(settings.ReportPath))
                return null;

            target = settings.ReportServerUrl.TrimEnd('/')
                   + "?"
                   + settings.ReportPath.TrimStart('?');

            if (!target.Contains(settings.ReportPath, StringComparison.Ordinal))
                target += settings.ReportPath.StartsWith("/") ? settings.ReportPath : "/" + settings.ReportPath;
        }

        var separator = target.Contains('?') ? '&' : '?';

        return target
            + separator
            + Uri.EscapeDataString(settings.YearParameter)
            + "="
            + Uri.EscapeDataString(yearMonth.Trim())
            + "&"
            + Uri.EscapeDataString(settings.PersonnelParameter)
            + "="
            + Uri.EscapeDataString(personnelNo)
            + "&rs:Command=Render"
            + "&rs:Format="
            + Uri.EscapeDataString(settings.ReportFormat);
    }
}