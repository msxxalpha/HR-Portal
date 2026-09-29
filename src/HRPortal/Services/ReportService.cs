using System.Net;
using System.Net.Http.Headers;
using System.Text;

using HRPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public sealed record ReportFetchResult(
    bool Success,
    byte[]? Content,
    string ContentType,
    string ErrorMessage);

public class ReportService(HRPortalDbContext db, ReportCredentialProtector credentialProtector)
{
    public async Task<ReportFetchResult> FetchAsync(string yearMonth, string personnelNo)
    {
        var settings = await db.PayrollReportSettings.AsNoTracking().FirstOrDefaultAsync();

        if (settings is null || !settings.Enabled)
            return Fail("نمایش فیش حقوقی در تنظیمات سامانه غیرفعال است.");

        if (!TryNormalizeYearMonth(yearMonth, out var normalizedYearMonth))
            return Fail("سال و ماه باید دقیقاً با فرمت ۱۴۰۵۰۶ (شش رقم، بدون اسلش) وارد شود.");

        if (string.IsNullOrWhiteSpace(personnelNo))
            return Fail("شماره پرسنلی کاربر احراز‌شده یافت نشد.");

        var target = BuildUrl(settings, normalizedYearMonth, personnelNo);
        if (target is null)
            return Fail("آدرس گزارش فیش حقوقی در تنظیمات سامانه ثبت نشده است.");

        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                PreAuthenticate = false
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            ConfigureAuthentication(
                handler,
                client,
                settings.ReportAuthentication,
                settings.ReportUsername,
                settings.ReportDomain,
                credentialProtector.Unprotect(settings.ReportPasswordProtected));

            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);

            var bytes = await response.Content.ReadAsByteArrayAsync();

            if (!response.IsSuccessStatusCode)
            {
                var body = SafeErrorBody(bytes, response.Content.Headers.ContentType?.MediaType);
                return Fail($"سرور SSRS کد HTTP موفق برنگرداند: {(int)response.StatusCode} {body}");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(mediaType))
                mediaType = GuessContentType(settings.ReportFormat);

            return new ReportFetchResult(true, bytes, mediaType, "");
        }
        catch (HttpRequestException ex)
        {
            return Fail("ارتباط با سرور SSRS برقرار نشد: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            return Fail("دریافت گزارش SSRS بیش از زمان مجاز طول کشید.");
        }
        catch (Exception ex)
        {
            return Fail("خطا در دریافت گزارش فیش حقوقی: " + ex.Message);
        }
    }

    public static bool TryNormalizeYearMonth(string? value, out string normalized)
    {
        normalized = NormalizeDigits(value).Trim();

        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[0-9]{4}(0[1-9]|1[0-2])$"))
        {
            normalized = "";
            return false;
        }

        return true;
    }

    private static string? BuildUrl(PayrollReportSettings settings, string yearMonth, string personnelNo)
    {
        var target = settings.ReportUrl?.Trim();

        if (string.IsNullOrWhiteSpace(target))
        {
            if (string.IsNullOrWhiteSpace(settings.ReportServerUrl) ||
                string.IsNullOrWhiteSpace(settings.ReportPath))
                return null;

            target = settings.ReportServerUrl.TrimEnd('/')
                   + "?"
                   + settings.ReportPath.TrimStart('?');
        }

        var separator = target.Contains('?') ? '&' : '?';

        target += separator
            + Uri.EscapeDataString(string.IsNullOrWhiteSpace(settings.YearParameter) ? "YearMonth" : settings.YearParameter.Trim())
            + "="
            + Uri.EscapeDataString(yearMonth)
            + "&"
            + Uri.EscapeDataString(string.IsNullOrWhiteSpace(settings.PersonnelParameter) ? "PersonnelNo" : settings.PersonnelParameter.Trim())
            + "="
            + Uri.EscapeDataString(personnelNo)
            + "&rs:Command=Render"
            + "&rs:Format="
            + Uri.EscapeDataString(string.IsNullOrWhiteSpace(settings.ReportFormat) ? "PDF" : settings.ReportFormat.Trim());

        return target;
    }

    private static void ConfigureAuthentication(
        HttpClientHandler handler,
        HttpClient client,
        string? authentication,
        string? username,
        string? domain,
        string password)
    {
        var mode = (authentication ?? "").Trim().ToLowerInvariant();
        username = username?.Trim() ?? "";
        domain = domain?.Trim() ?? "";

        if (mode is "" or "none")
            return;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("نام کاربری و گذرواژه دسترسی به گزارش SSRS تنظیم نشده است.");

        if (mode is "windows" or "ntlm" or "negotiate")
        {
            if (username.Contains('\\') && string.IsNullOrWhiteSpace(domain))
            {
                var parts = username.Split('\\', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    domain = parts[0];
                    username = parts[1];
                }
            }

            handler.Credentials = string.IsNullOrWhiteSpace(domain)
                ? new NetworkCredential(username, password)
                : new NetworkCredential(username, password, domain);

            return;
        }

        if (mode == "basic")
        {
            var raw = string.IsNullOrWhiteSpace(domain)
                ? username + ":" + password
                : domain + "\\" + username + ":" + password;

            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            return;
        }

        throw new InvalidOperationException("نوع احراز هویت گزارش SSRS نامعتبر است.");
    }

    private static ReportFetchResult Fail(string message) =>
        new(false, null, "text/html", message);

    private static string GuessContentType(string? format) =>
        (format ?? "").Trim().ToUpperInvariant() switch
        {
            "PDF" => "application/pdf",
            "CSV" => "text/csv",
            "XML" => "application/xml",
            "EXCELOPENXML" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "WORDOPENXML" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "text/html"
        };

    private static string SafeErrorBody(byte[] bytes, string? contentType)
    {
        if (bytes.Length == 0)
            return "";

        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
        {
            var text = Encoding.UTF8.GetString(bytes).Trim();
            return text.Length > 2000 ? text[..2000] : text;
        }

        return "بدنه خطای قابل نمایش دریافت نشد.";
    }

    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        const string fa = "۰۱۲۳۴۵۶۷۸۹";
        const string ar = "٠١٢٣٤٥٦٧٨٩";

        foreach (var pair in fa.Select((ch, index) => (ch, index)))
            value = value.Replace(pair.ch, (char)('0' + pair.index));

        foreach (var pair in ar.Select((ch, index) => (ch, index)))
            value = value.Replace(pair.ch, (char)('0' + pair.index));

        return value;
    }
}
