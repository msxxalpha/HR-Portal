using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public interface ISmsService
{
    Task<(bool Success, string Response)> SendOtpAsync(string mobile, string code);
}

public class ConfigurableSmsService(HRPortalDbContext db, IHttpClientFactory clients) : ISmsService
{
    public async Task<(bool Success, string Response)> SendOtpAsync(string mobile, string code)
    {
        var settings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is null || !settings.Enabled)
            return (false, "ارسال پیامک غیرفعال است.");

        mobile = NormalizeDigits(mobile);
        mobile = NormalizeIranMobile(mobile);
        var message = ApplyTemplate(settings.Template, mobile, code);

        try
        {
            var request = BuildRequest(settings, mobile, message);
            using var client = clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var ok = IsSuccessCode(settings.SuccessCodes, (int)response.StatusCode);

            return (ok,
                ok
                    ? "پیامک با موفقیت به سرویس‌دهنده ارسال شد."
                    : $"سرویس‌دهنده پیامک کد HTTP موفق برنگرداند: {(int)response.StatusCode} {body}");
        }
        catch (Exception ex)
        {
            return (false, "خطای ارتباط با سرویس پیامک: " + ex.Message);
        }
    }

    private static HttpRequestMessage BuildRequest(SmsSettings settings, string recipient, string message)
    {
        var endpoint = settings.Endpoint?.Trim() ?? "";
        if (endpoint.Length == 0)
            throw new InvalidOperationException("نشانی API پیامک تنظیم نشده است.");

        var data = ParseStaticParams(settings.StaticParams);

        var recipientField = settings.RecipientField?.Trim() ?? "";
        if (recipientField.Length > 0)
        {
            var asArray = string.Equals(settings.RecipientMode, "array", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(recipientField, "recipients", StringComparison.OrdinalIgnoreCase);
            data[recipientField] = asArray ? new[] { recipient } : recipient;
        }

        var messageField = settings.MessageField?.Trim() ?? "";
        if (messageField.Length > 0)
            data[messageField] = message;

        var senderField = settings.SenderField?.Trim() ?? "";
        if (senderField.Length > 0 && !string.IsNullOrWhiteSpace(settings.Sender))
            data[senderField] = settings.Sender;

        var numberFormatField = settings.NumberFormatField?.Trim() ?? "";
        if (numberFormatField.Length > 0 && !string.IsNullOrWhiteSpace(settings.NumberFormat))
            data[numberFormatField] = settings.NumberFormat;

        var headers = new Dictionary<string, string>
        {
            ["Accept"] = "application/json"
        };

        var apiKey = settings.ApiKey?.Trim() ?? "";
        var keyName = string.IsNullOrWhiteSpace(settings.ApiKeyName) ? "Api-Key" : settings.ApiKeyName.Trim();

        switch ((settings.AuthMode ?? "").Trim().ToLowerInvariant())
        {
            case "header" when apiKey.Length > 0:
                headers[keyName] = apiKey;
                break;
            case "bearer" when apiKey.Length > 0:
                headers["Authorization"] = "Bearer " + apiKey;
                break;
        }

        var authMode = (settings.AuthMode ?? "").Trim().ToLowerInvariant();
        if ((authMode is "query" or "body") && apiKey.Length > 0)
        {
            if (authMode == "query")
                endpoint = AddQuery(endpoint, keyName, apiKey);
            else
                data[keyName] = apiKey;
        }

        var method = (settings.Method ?? "POST").Trim().ToUpperInvariant();
        var format = (settings.Format ?? "json").Trim().ToLowerInvariant();
        var request = new HttpRequestMessage(new HttpMethod(method), endpoint);

        foreach (var h in headers)
            request.Headers.TryAddWithoutValidation(h.Key, h.Value);

        if (method == "GET")
        {
            foreach (var pair in data)
                endpoint = AddQuery(endpoint, pair.Key, ConvertToQueryValue(pair.Value));
            request.RequestUri = new Uri(endpoint, UriKind.Absolute);
            return request;
        }

        if (format == "query")
        {
            foreach (var pair in data)
                endpoint = AddQuery(endpoint, pair.Key, ConvertToQueryValue(pair.Value));
            request.RequestUri = new Uri(endpoint, UriKind.Absolute);
        }
        else if (format == "form")
        {
            request.Content = new FormUrlEncodedContent(
                data.ToDictionary(
                    x => x.Key,
                    x => ConvertToQueryValue(x.Value)));
        }
        else
        {
            request.Content = JsonContent.Create(data, options: new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        return request;
    }

    private static Dictionary<string, object?> ParseStaticParams(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw);
            if (parsed is null)
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            return parsed.ToDictionary(
                x => x.Key,
                x => JsonElementToObject(x.Value),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            throw new InvalidOperationException("پارامترهای ثابت JSON معتبر نیستند.");
        }
    }

    private static object? JsonElementToObject(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var i) => i,
            JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToArray(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(x => x.Name, x => JsonElementToObject(x.Value)),
            _ => null
        };

    private static string AddQuery(string endpoint, string key, string value)
    {
        var separator = endpoint.Contains('?') ? "&" : "?";
        return endpoint + separator + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);
    }

    private static string ConvertToQueryValue(object? value) =>
        value switch
        {
            null => "",
            string s => s,
            IEnumerable<string> strings => string.Join(",", strings),
            bool b => b ? "true" : "false",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
        };

    private static string ApplyTemplate(string template, string mobile, string code)
    {
        var values = new Dictionary<string, string>
        {
            ["{code}"] = code,
            ["{mobile}"] = mobile,
            ["{car_no}"] = "",
            ["{supplier_name}"] = "",
            ["{portal_url}"] = "",
            ["{title}"] = "ورود به سامانه منابع انسانی"
        };

        return (template ?? "").Trim().Length == 0
            ? $"کد ورود شما: {code}"
            : values.Aggregate(template, (current, pair) => current.Replace(pair.Key, pair.Value, StringComparison.Ordinal));
    }

    private static bool IsSuccessCode(string raw, int code)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0)
            return code is >= 200 and <= 299;

        foreach (var part in raw.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length == 3 && int.TryParse(part, out var exact) && exact == code)
                return true;

            var range = part.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (range.Length == 2 &&
                int.TryParse(range[0], out var min) &&
                int.TryParse(range[1], out var max) &&
                code >= min && code <= max)
                return true;
        }

        return false;
    }

    private static string NormalizeDigits(string value)
    {
        var fa = "۰۱۲۳۴۵۶۷۸۹";
        var ar = "٠١٢٣٤٥٦٧٨٩";
        for (var i = 0; i < 10; i++)
        {
            value = value.Replace(fa[i], (char)('0' + i)).Replace(ar[i], (char)('0' + i));
        }
        return value;
    }

    private static string NormalizeIranMobile(string value)
    {
        value = new string(value.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (value.StartsWith("+98", StringComparison.Ordinal))
            value = "0" + value[3..];
        else if (value.StartsWith("98", StringComparison.Ordinal) && value.Length >= 12)
            value = "0" + value[2..];
        return value;
    }
}
