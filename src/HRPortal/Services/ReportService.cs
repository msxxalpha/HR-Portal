using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using HRPortal.Data;
using HRPortal.Models;
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

        var target = BuildReportUrl(settings, normalizedYearMonth, personnelNo);
        if (target is null)
            return Fail("آدرس مستقیم گزارش فیش حقوقی در تنظیمات سامانه ثبت نشده است.");

        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                PreAuthenticate = false
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            var mode = NormalizeAuthenticationMode(settings.ReportAuthentication);

            // A browser may open the SSRS portal without prompting because it
            // silently supplies Windows Integrated credentials. In that case
            // an anonymous server-side HttpClient gets 401. Let HttpClient
            // answer a Windows challenge with the portal process identity.
            if (mode is "" or "windows")
            {
                // The browser can authenticate to SSRS silently with Windows
                // Integrated Authentication. The server-side HttpClient must
                // explicitly use the Windows identity of the portal process.
                handler.UseDefaultCredentials = true;
                handler.Credentials = CredentialCache.DefaultNetworkCredentials;
            }

            if (mode == "forms")
            {
                var authenticated = await LoginFormsAsync(
                    client,
                    target,
                    settings,
                    credentialProtector.Unprotect(settings.ReportPasswordProtected));

                if (!authenticated.Success)
                    return Fail(authenticated.ErrorMessage);
            }
            else
            {
                ConfigureHttpAuthentication(
                    handler,
                    client,
                    mode,
                    settings.ReportUsername,
                    settings.ReportDomain,
                    credentialProtector.Unprotect(settings.ReportPasswordProtected));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);

            var bytes = await response.Content.ReadAsByteArrayAsync();

            if (!response.IsSuccessStatusCode)
            {
                var body = SafeErrorBody(bytes, response.Content.Headers.ContentType?.MediaType);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return Fail(
                        "سرور SSRS درخواست سامانه را با خطای 401 رد کرد. " +
                        "SSRS این گزارش را با Windows Integrated Authentication محافظت می‌کند و حساب Windows اجرای پورتال " +
                        "باید روی گزارش SalaryReceiptItems مجوز مشاهده (Browser/Read) داشته باشد." +
                        (string.IsNullOrWhiteSpace(body) ? "" : $" جزئیات سرور: {body}"));
                }

                return Fail($"سرور SSRS کد HTTP موفق برنگرداند: {(int)response.StatusCode} {body}");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(mediaType))
                mediaType = GuessContentType(settings.ReportFormat);

            // A Forms-authentication server can redirect an unauthenticated
            // request back to Login.aspx with HTTP 200 after the final redirect.
            // Treat that as authentication failure instead of displaying the login page.
            if (IsLoginPage(response.RequestMessage?.RequestUri, settings.ReportLoginUrl) ||
                LooksLikeLoginPage(bytes, mediaType))
            {
                return Fail("احراز هویت SSRS انجام نشد یا نشست ورود SSRS ایجاد نشد.");
            }

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

        if (!Regex.IsMatch(normalized, @"^[0-9]{4}(0[1-9]|1[0-2])$"))
        {
            normalized = "";
            return false;
        }

        return true;
    }

    private static string NormalizeReportTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
            return target;

        var path = uri.AbsolutePath;
        var markerIndex = path.IndexOf("/Reports/report/", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var reportPath = path[(markerIndex + "/Reports/report/".Length)..];
            reportPath = Uri.UnescapeDataString(reportPath).Trim('/');

            if (reportPath.Length > 0)
            {
                var builder = new UriBuilder(uri.Scheme, uri.Host, uri.Port);
                builder.Path = "/ReportServer";
                builder.Query = "/" + reportPath;
                return builder.Uri.ToString().TrimEnd('?');
            }
        }

        if (path.Equals("/Reports", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Reports/", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri.Scheme, uri.Host, uri.Port);
            builder.Path = "/ReportServer";
            return builder.Uri.ToString().TrimEnd('/');
        }

        return target;
    }

    private static string NormalizeReportServerUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return value.TrimEnd('/');

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Equals("/Reports", StringComparison.OrdinalIgnoreCase))
            path = "/ReportServer";

        if (!path.EndsWith("/ReportServer", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains("/ReportServer_", StringComparison.OrdinalIgnoreCase))
        {
            // Leave custom virtual directories untouched.
        }

        var builder = new UriBuilder(uri)
        {
            Path = path
        };
        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string? BuildReportUrl(
        PayrollReportSettings settings,
        string yearMonth,
        string personnelNo)
    {
        // When Report Server + Report Path are configured, always build the
        // native direct report URL. This avoids stale ReportViewer.aspx/catalog URLs.
        var target = string.IsNullOrWhiteSpace(settings.ReportServerUrl) ||
                     string.IsNullOrWhiteSpace(settings.ReportPath)
            ? settings.ReportUrl?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(target))
        {
            if (string.IsNullOrWhiteSpace(settings.ReportServerUrl) ||
                string.IsNullOrWhiteSpace(settings.ReportPath))
                return null;

            var baseUrl = NormalizeReportServerUrl(settings.ReportServerUrl.TrimEnd('/'));
            var reportPath = settings.ReportPath.Trim();

            if (!reportPath.StartsWith('/'))
                reportPath = "/" + reportPath;

            target = baseUrl + "?" + reportPath;
        }
        else
        {
            // Users commonly copy the browser portal URL:
            // /Reports/report/SalaryReceiptItems
            // That is the web portal, not the native ReportServer URL.
            // Convert it automatically to the URL-access endpoint.
            target = NormalizeReportTarget(target);
        }

        var separator = target.Contains('?') ? '&' : '?';
        var yearParameter = string.IsNullOrWhiteSpace(settings.YearParameter)
            ? "YearMonth"
            : settings.YearParameter.Trim();
        var personnelParameter = string.IsNullOrWhiteSpace(settings.PersonnelParameter)
            ? "PersonnelNo"
            : settings.PersonnelParameter.Trim();
        var reportFormat = string.IsNullOrWhiteSpace(settings.ReportFormat)
            ? "PDF"
            : settings.ReportFormat.Trim();

        return target
            + separator
            + Uri.EscapeDataString(yearParameter)
            + "="
            + Uri.EscapeDataString(yearMonth)
            + "&"
            + Uri.EscapeDataString(personnelParameter)
            + "="
            + Uri.EscapeDataString(personnelNo)
            + "&rs:Command=Render"
            + "&rs:Format="
            + Uri.EscapeDataString(reportFormat);
    }

    private async Task<(bool Success, string ErrorMessage)> LoginFormsAsync(
        HttpClient client,
        string reportTarget,
        PayrollReportSettings settings,
        string password)
    {
        var loginUrl = settings.ReportLoginUrl?.Trim();

        if (string.IsNullOrWhiteSpace(loginUrl))
        {
            if (string.IsNullOrWhiteSpace(settings.ReportServerUrl))
                return (false, "آدرس صفحه ورود SSRS تنظیم نشده است.");

            loginUrl = settings.ReportServerUrl.TrimEnd('/')
                      + "/Pages/Login.aspx?AspxAutoDetectCookieSupport=1";
        }

        if (string.IsNullOrWhiteSpace(settings.ReportUsername) || string.IsNullOrEmpty(password))
            return (false, "نام کاربری و گذرواژه دسترسی به گزارش SSRS تنظیم نشده است.");

        using var getResponse = await client.GetAsync(loginUrl);
        var loginHtml = await getResponse.Content.ReadAsStringAsync();

        if (!getResponse.IsSuccessStatusCode)
            return (false, $"صفحه ورود SSRS قابل دریافت نیست: {(int)getResponse.StatusCode}");

        var form = ExtractLoginForm(loginHtml, loginUrl, settings.ReportUsernameField, settings.ReportPasswordField);
        if (form is null)
            return (false, "فرم ورود SSRS در صفحه Login.aspx شناسایی نشد.");

        var formData = form.Value.Fields;

        formData[form.Value.UsernameField] = settings.ReportUsername.Trim();
        formData[form.Value.PasswordField] = password;

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, form.Value.Action);
        loginRequest.Content = new FormUrlEncodedContent(formData);

        using var postResponse = await client.SendAsync(
            loginRequest,
            HttpCompletionOption.ResponseHeadersRead);

        if (!postResponse.IsSuccessStatusCode)
            return (false, $"ورود به SSRS ناموفق بود: {(int)postResponse.StatusCode}");

        var finalUri = postResponse.RequestMessage?.RequestUri;
        var responseBytes = await postResponse.Content.ReadAsByteArrayAsync();
        var responseType = postResponse.Content.Headers.ContentType?.MediaType;

        if (IsLoginPage(finalUri, loginUrl) || LooksLikeLoginPage(responseBytes, responseType))
            return (false, "نام کاربری یا گذرواژه SSRS صحیح نیست، یا فرم ورود SSRS نیاز به تنظیمات بیشتری دارد.");

        // Force a request to the actual report while the authenticated cookie is
        // still held by the same HttpClient/CookieContainer.
        using var probe = new HttpRequestMessage(HttpMethod.Get, reportTarget);
        using var probeResponse = await client.SendAsync(
            probe,
            HttpCompletionOption.ResponseHeadersRead);

        if ((int)probeResponse.StatusCode == StatusCodes.Status401Unauthorized ||
            (int)probeResponse.StatusCode == StatusCodes.Status403Forbidden)
            return (false, "حساب SSRS وارد شد ولی اجازه مشاهده این گزارش را ندارد.");

        var probeBytes = await probeResponse.Content.ReadAsByteArrayAsync();
        var probeType = probeResponse.Content.Headers.ContentType?.MediaType;

        if (IsLoginPage(probeResponse.RequestMessage?.RequestUri, loginUrl) ||
            LooksLikeLoginPage(probeBytes, probeType))
            return (false, "نشست ورود SSRS ایجاد نشد و سرور دوباره صفحه ورود را برگرداند.");

        return (true, "");
    }

    private static (string Action, Dictionary<string, string> Fields, string UsernameField, string PasswordField)?
        ExtractLoginForm(
            string html,
            string loginUrl,
            string? configuredUsernameField,
            string? configuredPasswordField)
    {
        // Prefer the HTML form containing the password input. Some custom
        // reporting portals render login controls without a traditional <form>
        // wrapper; in that case use the whole page and POST back to Login.aspx.
        var formMatches = Regex.Matches(
            html,
            @"<form\b(?<attrs>[^>]*)>(?<body>.*?)</form>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        string body;
        string attrs;
        string action;

        var selected = formMatches.Cast<Match>()
            .FirstOrDefault(x =>
                Regex.IsMatch(
                    x.Groups["body"].Value,
                    @"<input\b[^>]*type\s*=\s*[""'']?password",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline));

        if (selected is not null)
        {
            body = selected.Groups["body"].Value;
            attrs = selected.Groups["attrs"].Value;
            action = GetHtmlAttribute(attrs, "action") ?? loginUrl;
        }
        else if (formMatches.Count > 0)
        {
            var first = formMatches[0];
            body = first.Groups["body"].Value;
            attrs = first.Groups["attrs"].Value;
            action = GetHtmlAttribute(attrs, "action") ?? loginUrl;
        }
        else
        {
            body = html;
            attrs = "";
            action = loginUrl;
        }

        if (!Uri.TryCreate(new Uri(loginUrl), action, out var actionUri))
            actionUri = new Uri(loginUrl);

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match input in Regex.Matches(
                     body,
                     @"<input\b(?<attrs>[^>]*)>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var inputAttrs = input.Groups["attrs"].Value;
            var name = GetHtmlAttribute(inputAttrs, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var type = GetHtmlAttribute(inputAttrs, "type") ?? "text";
            var value = GetHtmlAttribute(inputAttrs, "value") ?? "";

            if (type.Equals("hidden", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("submit", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("button", StringComparison.OrdinalIgnoreCase))
            {
                fields[name] = value;
            }
            else if (type.Equals("image", StringComparison.OrdinalIgnoreCase))
            {
                // ASP.NET WebForms ImageButton postback fields.
                fields[name + ".x"] = "1";
                fields[name + ".y"] = "1";
            }
        }

        // Some WebForms login pages use a <button> rather than an <input>.
        foreach (Match button in Regex.Matches(
                     body,
                     @"<button\b(?<attrs>[^>]*)>.*?</button>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var buttonAttrs = button.Groups["attrs"].Value;
            var buttonName = GetHtmlAttribute(buttonAttrs, "name");
            if (!string.IsNullOrWhiteSpace(buttonName))
                fields[buttonName] = GetHtmlAttribute(buttonAttrs, "value") ?? "";
        }

        var detectedPasswordField = FindInputName(html, "password");
        var passwordField = !string.IsNullOrWhiteSpace(configuredPasswordField) &&
                            HasNamedInput(html, configuredPasswordField.Trim(), "password")
            ? configuredPasswordField.Trim()
            : detectedPasswordField;

        if (string.IsNullOrWhiteSpace(passwordField))
            return null;

        var detectedUsernameField = FindUsernameField(html);
        var usernameField = !string.IsNullOrWhiteSpace(configuredUsernameField) &&
                            HasNamedInput(html, configuredUsernameField.Trim(), "text")
            ? configuredUsernameField.Trim()
            : detectedUsernameField;

        if (string.IsNullOrWhiteSpace(usernameField))
            return null;

        return (actionUri.ToString(), fields, usernameField, passwordField);
    }

    private static bool HasNamedInput(string html, string name, string expectedType)
    {
        foreach (Match input in Regex.Matches(
                     html,
                     @"<input\b(?<attrs>[^>]*)>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attrs = input.Groups["attrs"].Value;
            var inputName = GetHtmlAttribute(attrs, "name");
            var inputType = GetHtmlAttribute(attrs, "type") ?? "text";

            if (string.Equals(inputName, name, StringComparison.OrdinalIgnoreCase) &&
                (expectedType == "text" ||
                 string.Equals(inputType, expectedType, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static string? FindUsernameField(string body)
    {
        foreach (Match input in Regex.Matches(
                     body,
                     @"<input\b(?<attrs>[^>]*)>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attrs = input.Groups["attrs"].Value;
            var inputType = GetHtmlAttribute(attrs, "type") ?? "text";
            if (!inputType.Equals("text", StringComparison.OrdinalIgnoreCase) &&
                !inputType.Equals("email", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = GetHtmlAttribute(attrs, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var hint = (name + " " +
                        (GetHtmlAttribute(attrs, "id") ?? "") + " " +
                        (GetHtmlAttribute(attrs, "autocomplete") ?? "") + " " +
                        (GetHtmlAttribute(attrs, "placeholder") ?? ""))
                        .ToLowerInvariant();

            if (hint.Contains("user") || hint.Contains("login") || hint.Contains("account") ||
                hint.Contains("username") || hint.Contains("کاربر") || hint.Contains("نام"))
                return name;
        }

        return FindInputName(body, "text", true);
    }

    private static string? FindInputName(string body, string type, bool usernameMode = false)
    {
        foreach (Match input in Regex.Matches(
                     body,
                     @"<input\b(?<attrs>[^>]*)>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var attrs = input.Groups["attrs"].Value;
            var inputType = GetHtmlAttribute(attrs, "type") ?? "text";
            if (!inputType.Equals(type, StringComparison.OrdinalIgnoreCase))
                continue;

            var name = GetHtmlAttribute(attrs, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (usernameMode)
            {
                var lower = (name + " " + (GetHtmlAttribute(attrs, "id") ?? "") + " " +
                             (GetHtmlAttribute(attrs, "placeholder") ?? "")).ToLowerInvariant();

                if (lower.Contains("user") || lower.Contains("login") || lower.Contains("name") ||
                    lower.Contains("کاربر") || lower.Contains("نام"))
                    return name;
            }
            else
            {
                return name;
            }
        }

        if (usernameMode)
        {
            foreach (Match input in Regex.Matches(
                         body,
                         @"<input\b(?<attrs>[^>]*)>",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var attrs = input.Groups["attrs"].Value;
                var inputType = GetHtmlAttribute(attrs, "type") ?? "text";
                if (!inputType.Equals("text", StringComparison.OrdinalIgnoreCase) &&
                    !inputType.Equals("email", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = GetHtmlAttribute(attrs, "name");
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }

        return null;
    }

    private static string? GetHtmlAttribute(string attrs, string name)
    {
        var match = Regex.Match(
            attrs,
            $@"\b{Regex.Escape(name)}\s*=\s*[""''](?<value>.*?)[""'']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (match.Success)
            return WebUtility.HtmlDecode(match.Groups["value"].Value);

        return null;
    }

    private static void ConfigureHttpAuthentication(
        HttpClientHandler handler,
        HttpClient client,
        string mode,
        string? username,
        string? domain,
        string password)
    {
        username = username?.Trim() ?? "";
        domain = domain?.Trim() ?? "";

        if (mode is "" or "none")
            return;

        if (mode is "windows" or "ntlm" or "negotiate")
        {
            // No explicit username/password is required for automatic Windows
            // authentication. The handler has already been configured with the
            // current process identity above. Explicit credentials remain
            // supported when the administrator supplies them.
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return;

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
                : domain + "\\\\" + username + ":" + password;

            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            return;
        }

        throw new InvalidOperationException("نوع احراز هویت گزارش SSRS نامعتبر است.");
    }

    private static string NormalizeAuthenticationMode(string? value)
    {
        var mode = (value ?? "").Trim().ToLowerInvariant();
        return mode switch
        {
            "form" or "forms" or "custom" => "forms",
            "ntlm" or "negotiate" or "windows" => "windows",
            "basic" => "basic",
            _ => "none"
        };
    }

    private static bool IsLoginPage(Uri? uri, string? loginUrl)
    {
        if (uri is null)
            return false;

        if (!string.IsNullOrWhiteSpace(loginUrl) &&
            Uri.TryCreate(loginUrl, UriKind.Absolute, out var configured))
        {
            return string.Equals(uri.Host, configured.Host, StringComparison.OrdinalIgnoreCase) &&
                   uri.AbsolutePath.Equals(configured.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }

        return uri.AbsolutePath.EndsWith("/Login.aspx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeLoginPage(byte[] bytes, string? contentType)
    {
        if (bytes.Length == 0 ||
            (contentType is not null &&
             !contentType.Contains("text", StringComparison.OrdinalIgnoreCase) &&
             !contentType.Contains("html", StringComparison.OrdinalIgnoreCase)))
            return false;

        var text = Encoding.UTF8.GetString(bytes);

        return (text.Contains("کلمه عبور", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("نام کاربری", StringComparison.OrdinalIgnoreCase)) ||
               text.Contains("نام کاربری :", StringComparison.OrdinalIgnoreCase);
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
            (contentType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("html", StringComparison.OrdinalIgnoreCase)))
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
