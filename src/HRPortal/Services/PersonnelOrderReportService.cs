using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class PersonnelOrderReportService(
    HRPortalDbContext db,
    InputQueryService queries,
    ReportService reports)
{
    public async Task<(bool Success, string? OrderId, string ErrorMessage)> ResolveOrderIdAsync(Employee employee)
    {
        var settings = await db.PersonnelOrderReportSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is null || !settings.Enabled)
            return (false, null, "نمایش حکم کارگزینی در تنظیمات سامانه غیرفعال است.");
        var queryResult = await queries.ExecuteForEmployeeAsync("حکم کارگزینی", employee);
        return queryResult.Success && !string.IsNullOrWhiteSpace(queryResult.Value)
            ? (true, queryResult.Value, "")
            : (false, null, queryResult.ErrorMessage);
    }

    public async Task<ReportFetchResult> FetchForOrderIdAsync(string orderId)
    {
        var settings = await db.PersonnelOrderReportSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is null || !settings.Enabled)
            return new(false, null, "نمایش حکم کارگزینی در تنظیمات سامانه غیرفعال است.");
        if (string.IsNullOrWhiteSpace(orderId))
            return new(false, null, "شناسه حکم کارگزینی دریافت نشده است.");

        var reportSettings = new PayrollReportSettings
        {
            Enabled = settings.Enabled,
            ReportUrl = settings.ReportUrl,
            ReportServerUrl = settings.ReportServerUrl,
            ReportPath = settings.ReportPath,
            YearParameter = settings.OrderIdParameter,
            PersonnelParameter = settings.OrderIdParameter,
            ReportFormat = settings.ReportFormat,
            ReportAuthentication = settings.ReportAuthentication,
            ReportUsername = settings.ReportUsername,
            ReportDomain = settings.ReportDomain,
            ReportLoginUrl = settings.ReportLoginUrl,
            ReportUsernameField = settings.ReportUsernameField,
            ReportPasswordField = settings.ReportPasswordField,
            ReportPasswordProtected = settings.ReportPasswordProtected,
            UseIntegratedSecurity = settings.UseIntegratedSecurity
        };

        return await reports.FetchConfiguredAsync(
            reportSettings,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [settings.OrderIdParameter.Trim()] = queryResult.Value
            });
    }
}