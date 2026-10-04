using HRPortal.Data;
using HRPortal.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace HRPortal.Services;

public sealed record InputQueryResult(bool Success, string? Value, string ErrorMessage);

public class InputQueryService(HRPortalDbContext db, ReportCredentialProtector credentialProtector)
{
    public async Task<InputQueryResult> ExecuteForEmployeeAsync(string title, Employee employee)
    {
        var query = await db.InputQueries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Title == title && x.Enabled);

        if (query is null)
            return new(false, null, $"کوئری فعال با عنوان «{title}» در بخش کوئری‌های ورودی ثبت نشده است.");

        if (string.IsNullOrWhiteSpace(employee.PersonnelNumber))
            return new(false, null, "شماره پرسنلی کارمند برای اجرای کوئری موجود نیست.");

        if (string.IsNullOrWhiteSpace(employee.Identifier))
            return new(false, null, "فیلد «شناسه» این کارمند در اطلاعات کارکنان ثبت نشده است.");

        return await ExecuteAsync(query, employee.Id, employee.PersonnelNumber, employee.Identifier);
    }

    public async Task<InputQueryResult> ExecuteAsync(InputQuery query, int employeeId, string personnelNumber, string? identifier)
    {
        if (!IsReadOnlyQuery(query.SqlText))
            return new(false, null, "متن کوئری فقط باید یک SELECT یا CTE خواندنی باشد؛ دستورات تغییر داده مجاز نیستند.");

        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = query.ServerInstance.Trim(),
                InitialCatalog = query.DatabaseName.Trim(),
                Encrypt = query.Encrypt,
                TrustServerCertificate = query.TrustServerCertificate,
                ConnectTimeout = 15,
                ApplicationName = "HRPortal-InputQuery"
            };

            var mode = (query.AuthenticationMode ?? "sql").Trim().ToLowerInvariant();
            if (mode is "windows" or "integrated")
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                var password = credentialProtector.Unprotect(query.PasswordProtected);
                if (string.IsNullOrWhiteSpace(query.Username) || string.IsNullOrEmpty(password))
                    return new(false, null, $"اطلاعات کاربری SQL برای کوئری «{query.Title}» کامل نیست.");
                builder.UserID = query.Username.Trim();
                builder.Password = password;
                builder.IntegratedSecurity = false;
            }

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = query.SqlText.Trim();
            command.CommandType = System.Data.CommandType.Text;
            command.CommandTimeout = Math.Clamp(query.CommandTimeoutSeconds, 5, 300);

            // The personnel-order query convention is a single input parameter:
            // @Identifier = the value of the logged-in employee's «شناسه» field.
            // The two legacy aliases are retained for older input queries.
            command.Parameters.Add(new SqlParameter("@Identifier", System.Data.SqlDbType.NVarChar, 100)
            {
                Value = identifier ?? ""
            });
            command.Parameters.Add(new SqlParameter("@PersonnelNumber", System.Data.SqlDbType.NVarChar, 50)
            {
                Value = personnelNumber
            });
            command.Parameters.Add(new SqlParameter("@EmployeeId", System.Data.SqlDbType.Int)
            {
                Value = employeeId
            });

            await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow);
            if (!await reader.ReadAsync())
                return new(false, null, $"کوئری «{query.Title}» برای این کارمند هیچ رکوردی برنگرداند.");
            if (reader.FieldCount == 0 || await reader.IsDBNullAsync(0))
                return new(false, null, $"کوئری «{query.Title}» مقدار شناسه‌ای برنگرداند.");

            var value = Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrWhiteSpace(value)
                ? new(false, null, $"کوئری «{query.Title}» مقدار شناسه‌ای برنگرداند.")
                : new(true, value, "");
        }
        catch (SqlException ex)
        {
            return new(false, null, $"اجرای کوئری «{query.Title}» ناموفق بود: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new(false, null, $"خطا در اجرای کوئری «{query.Title}»: {ex.Message}");
        }
    }

    private static bool IsReadOnlyQuery(string sql)
    {
        var text = Regex.Replace(sql ?? "", @"--.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline).Trim();

        // A single trailing semicolon is valid SQL syntax and is commonly
        // included when administrators paste a query from SSMS.
        text = Regex.Replace(text, @";\s*$", "").Trim();

        if (text.Length == 0 || text.Contains(';') || !Regex.IsMatch(text, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase))
            return false;

        return !Regex.IsMatch(text,
            @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|TRUNCATE|CREATE|EXEC|EXECUTE|GRANT|REVOKE|DENY)\b",
            RegexOptions.IgnoreCase);
    }
}