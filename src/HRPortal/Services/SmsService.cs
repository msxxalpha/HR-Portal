using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
            var request = BuildRequest(settings, mobile, code, message);
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

    private static HttpRequestMessage BuildRequest(SmsSettings settings, string recipient, string code, string message)
    {
        var endpoint = settings.Endpoint?.Trim() ?? "";
        if (endpoint.Length == 0)
            throw new InvalidOperationException("نشانی API پیامک تنظیم نشده است.");

        var data = ParseStaticParams(settings.StaticParams);
        var sendingType = GetStringValue(data, "sending_type");

        var isIppanelEdge = IsIppanelEdgeEndpoint(endpoint);
        var isFarazPattern = IsFarazPatternEndpoint(endpoint);
        var isPattern = string.Equals(sendingType, "pattern", StringComparison.OrdinalIgnoreCase);

        // Faraz SMS has a dedicated pattern endpoint and does not need
        // sending_type in the request body.
        if (isFarazPattern)
        {
            sendingType = "pattern";
            isPattern = true;
        }

        // For the OTP use case, a configured IPPanel pattern code is enough to
        // select the pattern sending contract even when sending_type was omitted
        // from legacy static parameters.
        if (isIppanelEdge && !isPattern && !string.IsNullOrWhiteSpace(settings.PatternCode))
        {
            sendingType = "pattern";
            isPattern = true;
            data["sending_type"] = "pattern";
        }

        var recipientField = settings.RecipientField?.Trim() ?? "";
        if (recipientField.Length > 0)
        {
            var asArray = string.Equals(settings.RecipientMode, "array", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(recipientField, "recipients", StringComparison.OrdinalIgnoreCase);

            if (isFarazPattern)
            {
                data["recipient"] = NormalizeIranMobile(recipient);
                data.Remove("recipients");
            }
            else if (isIppanelEdge && string.Equals(sendingType, "pattern", StringComparison.OrdinalIgnoreCase))
            {
                // IPPanel Edge Pattern API requires top-level recipients[] in E.164.
                data.Remove("recipient");
                data["recipients"] = new[] { NormalizeIranMobileE164(recipient) };
            }
            else
            {
                data[recipientField] = asArray ? new[] { recipient } : recipient;
            }
        }
        else if (isFarazPattern)
        {
            data["recipient"] = NormalizeIranMobile(recipient);
        }
        else if (isIppanelEdge && string.Equals(sendingType, "pattern", StringComparison.OrdinalIgnoreCase))
        {
            data["recipients"] = new[] { NormalizeIranMobileE164(recipient) };
        }

        var senderField = settings.SenderField?.Trim() ?? "";
        if (isFarazPattern)
        {
            if (!string.IsNullOrWhiteSpace(settings.Sender))
                data["line_number"] = settings.Sender.Trim();
            data.Remove("from_number");
            data.Remove("sender");
        }
        else if (isIppanelEdge && string.Equals(sendingType, "pattern", StringComparison.OrdinalIgnoreCase))
        {
            data.Remove("sender");
            if (!string.IsNullOrWhiteSpace(settings.Sender))
                data["from_number"] = NormalizeIranMobileE164(settings.Sender);
        }
        else if (senderField.Length > 0 && !string.IsNullOrWhiteSpace(settings.Sender))
        {
            data[senderField] = settings.Sender;
        }

        var numberFormatField = settings.NumberFormatField?.Trim() ?? "";
        if (isFarazPattern)
        {
            data["number_format"] = string.IsNullOrWhiteSpace(settings.NumberFormat)
                ? "english"
                : settings.NumberFormat.Trim().ToLowerInvariant();
            data.Remove("numberFormat");
        }
        else if (numberFormatField.Length > 0 && !string.IsNullOrWhiteSpace(settings.NumberFormat))
        {
            data[numberFormatField] = settings.NumberFormat;
        }

        var headers = new Dictionary<string, string>
        {
            ["Accept"] = "application/json"
        };

        var apiKey = settings.ApiKey?.Trim() ?? "";
        var keyName = string.IsNullOrWhiteSpace(settings.ApiKeyName) ? "Api-Key" : settings.ApiKeyName.Trim();

        switch ((settings.AuthMode ?? "").Trim().ToLowerInvariant())
        {
            case "header" when apiKey.Length > 0:
                if (isIppanelEdge)
                    headers["Authorization"] = apiKey;
                else
                    headers[keyName] = apiKey;
                break;
            case "bearer" when apiKey.Length > 0:
                headers["Authorization"] = "Bearer " + apiKey;
                break;
        }

        if (isIppanelEdge && apiKey.Length > 0)
            headers["Authorization"] = apiKey;

        var authMode = (settings.AuthMode ?? "").Trim().ToLowerInvariant();
        if (!isIppanelEdge && (authMode is "query" or "body") && apiKey.Length > 0)
        {
            if (authMode == "query")
                endpoint = AddQuery(endpoint, keyName, apiKey);
            else
                data[keyName] = apiKey;
        }

        // Pattern-based SMS APIs use the top-level "code" as the registered
        // pattern identifier, while the actual OTP belongs inside "params".
        if (isPattern)
        {
            var patternCode = settings.PatternCode?.Trim() ?? "";

            // Faraz's "code" is the pattern UID. A legacy static "code" is
            // also accepted here only when an explicit PatternCode was not set.
            if (patternCode.Length == 0 && isFarazPattern && data.TryGetValue("code", out var farazStaticCode))
                patternCode = ConvertToQueryValue(farazStaticCode);

            if (patternCode.Length == 0 && data.TryGetValue("pattern_code", out var staticPatternCode))
                patternCode = ConvertToQueryValue(staticPatternCode);

            if (patternCode.Length == 0)
                throw new InvalidOperationException("کد الگوی پیامک تنظیم نشده است. شناسه واقعی الگو (Pattern UID) را در فیلد «کد الگوی پیامک» وارد کنید.");

            if (string.Equals(patternCode, "code", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("مقدار «کد الگوی پیامک» نباید code باشد؛ باید UID واقعی الگوی تأییدشده در فراز اس ام اس وارد شود.");

            var patternCodeField = "code";
            data[patternCodeField] = patternCode;

            var otpParameterField = string.IsNullOrWhiteSpace(settings.OtpParameterField)
                ? "code"
                : settings.OtpParameterField.Trim();

            Dictionary<string, object?> parameters;
            if (data.TryGetValue("params", out var existingParams) &&
                existingParams is Dictionary<string, object?> existingDictionary)
            {
                parameters = new Dictionary<string, object?>(existingDictionary, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }

            parameters[otpParameterField] = code;
            data["params"] = parameters;

            // Pattern requests do not use the generic free-form message field.
            var patternMessageField = settings.MessageField?.Trim() ?? "";
            if (patternMessageField.Length > 0 &&
                !string.Equals(patternMessageField, "params", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(patternMessageField, "attributes", StringComparison.OrdinalIgnoreCase))
                data.Remove(patternMessageField);

            if (isFarazPattern)
            {
                // Faraz SMS Pattern contract:
                // code (pattern UID), attributes, recipient, line_number, number_format.
                data.Remove("sending_type");
                data.Remove("params");
                data.Remove("recipients");
                data.Remove("from_number");
                data.Remove("sender");
                data["recipient"] = NormalizeIranMobile(recipient);
                data["line_number"] = settings.Sender?.Trim() ?? "";

                Dictionary<string, object?> attributes;
                if (data.TryGetValue("attributes", out var existingAttributes) &&
                    existingAttributes is Dictionary<string, object?> farazAttributesDictionary)
                {
                    attributes = new Dictionary<string, object?>(farazAttributesDictionary, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                }

                attributes[otpParameterField] = code;
                data["attributes"] = attributes;
                data["number_format"] = string.IsNullOrWhiteSpace(settings.NumberFormat)
                    ? "english"
                    : settings.NumberFormat.Trim().ToLowerInvariant();
            }
            else if (isIppanelEdge)
            {
                // IPPanel Edge requires exactly the pattern request contract:
                // POST /api/send, JSON, code, recipients[], params, from_number.
                data.Remove("recipient");
                data["recipients"] = new[] { NormalizeIranMobileE164(recipient) };
                if (!string.IsNullOrWhiteSpace(settings.Sender))
                    data["from_number"] = NormalizeIranMobileE164(settings.Sender);

                data.Remove("sender");
            }
        }
        else
        {
            var messageField = settings.MessageField?.Trim() ?? "";
            if (messageField.Length > 0)
                data[messageField] = message;

            var codeField = settings.CodeField?.Trim() ?? "";
            if (codeField.Length > 0)
                data[codeField] = code;
        }

        var method = ((isIppanelEdge || isFarazPattern) && isPattern)
            ? "POST"
            : (settings.Method ?? "POST").Trim().ToUpperInvariant();
        var format = ((isIppanelEdge || isFarazPattern) && isPattern)
            ? "json"
            : (settings.Format ?? "json").Trim().ToLowerInvariant();
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

    private static string? GetStringValue(Dictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            null => null,
            string s => s,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
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

        var normalizedTemplate = template?.Trim() ?? "";
        return normalizedTemplate.Length == 0
            ? $"کد ورود شما: {code}"
            : values.Aggregate(normalizedTemplate, (current, pair) => current.Replace(pair.Key, pair.Value, StringComparison.Ordinal));
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

    private static bool IsIppanelEdgeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return false;

        return uri.Host.Contains("ippanel", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Contains("ip-panel", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFarazPatternEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return false;

        return uri.Host.Equals("api.iranpayamak.com", StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.Contains("/ws/v1/sms/pattern", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeIranMobileE164(string value)
    {
        value = NormalizeDigits(value);
        value = new string(value.Where(c => char.IsDigit(c) || c == '+').ToArray());

        if (value.StartsWith("+98", StringComparison.Ordinal))
            return "+98" + value[3..];

        if (value.StartsWith("98", StringComparison.Ordinal) && value.Length >= 12)
            return "+" + value;

        if (value.StartsWith("0", StringComparison.Ordinal) && value.Length >= 11)
            return "+98" + value[1..];

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
