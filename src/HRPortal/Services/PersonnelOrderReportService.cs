using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class PersonnelOrderReportService(
    HRPortalDbContext db,
    InputQueryService queries,
    ReportService reports)
{
    public async Task<(bool Success, string? OrderId, string ErrorMessage)> ResolveOrderIdAsync(string personnelNumber)
    {
        var settings = await db.PersonnelOrderReportSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is null || !settings.Enabled)
            return (false, null, "نمایش حکم کارگزینی در تنظیمات سامانه غیرفعال است.");

        var normalizedPersonnelNumber = personnelNumber?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalizedPersonnelNumber))
            return (false, null, "کد پرسنلی کاربر لاگین‌شده مشخص نیست.");

        // The employee record is resolved again from the authenticated username
        // (personnel number). This guarantees that Identifier belongs to this user.
        var employee = await db.Employees.AsNoTracking()
            .Where(x => x.PersonnelNumber == normalizedPersonnelNumber &&
                        x.IsSystemUser &&
                        x.Status == "فعال")
            .Select(x => new Employee
            {
                Id = x.Id,
                PersonnelNumber = x.PersonnelNumber,
                Identifier = x.Identifier
            })
            .SingleOrDefaultAsync();

        if (employee is null)
            return (false, null, "کارمند فعال متناظر با کد پرسنلی کاربر لاگین‌شده یافت نشد.");

        if (string.IsNullOrWhiteSpace(employee.Identifier))
            return (false, null, $"فیلد «شناسه» کارمند با کد پرسنلی {normalizedPersonnelNumber} خالی است.");

        var queryResult = await queries.ExecutePersonnelOrderAsync(employee.PersonnelNumber);
        if (!queryResult.Success || string.IsNullOrWhiteSpace(queryResult.Value))
            return (false, null, queryResult.ErrorMessage);

        return (true, queryResult.Value.Trim(), "");
    }

    public async Task<ReportFetchResult> FetchForOrderIdAsync(string orderId)
    {
        var settings = await db.PersonnelOrderReportSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is null || !settings.Enabled)
            return new(false, null, "text/html", "نمایش حکم کارگزینی در تنظیمات سامانه غیرفعال است.");
        if (string.IsNullOrWhiteSpace(orderId))
            return new(false, null, "text/html", "شناسه حکم کارگزینی دریافت نشده است.");

        var parameterName = settings.OrderIdParameter?.Trim();
        if (string.IsNullOrWhiteSpace(parameterName))
            return new(false, null, "نام پارامتر گزارش شناسه حکم کارگزینی در تنظیمات سامانه وارد نشده است.");

        var reportSettings = new PayrollReportSettings
        {
            Enabled = settings.Enabled,
            ReportUrl = settings.ReportUrl,
            ReportServerUrl = settings.ReportServerUrl,
            ReportPath = settings.ReportPath,
            YearParameter = parameterName,
            PersonnelParameter = parameterName,
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
                [parameterName] = orderId
            });
    }
}