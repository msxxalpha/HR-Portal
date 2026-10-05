using System.Globalization;

namespace HRPortal.Services;

public static class PersianDateService
{
    public static string Format(DateTime date)
    {
        var p = new PersianCalendar();
        return $"{p.GetYear(date):0000}/{p.GetMonth(date):00}/{p.GetDayOfMonth(date):00}";
    }

    public static string FormatDateTime(DateTime date)
    {
        var p = new PersianCalendar();
        return $"{p.GetYear(date):0000}/{p.GetMonth(date):00}/{p.GetDayOfMonth(date):00} {date:HH:mm}";
    }

    public static bool TryParse(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = NormalizeDigits(value.Trim()).Replace("-", "/");
        var parts = normalized.Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var y) ||
            !int.TryParse(parts[1], out var m) || !int.TryParse(parts[2], out var d))
            return false;

        try
        {
            date = new PersianCalendar().ToDateTime(y, m, d, 0, 0, 0, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParseDateTime(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = NormalizeDigits(value.Trim()).Replace("-", "/");
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2) return false;

        var datePart = parts[0].Split('/');
        if (datePart.Length != 3 ||
            !int.TryParse(datePart[0], out var y) ||
            !int.TryParse(datePart[1], out var m) ||
            !int.TryParse(datePart[2], out var d))
            return false;

        var hour = 0;
        var minute = 0;
        if (parts.Length == 2)
        {
            var timeParts = parts[1].Split(':');
            if (timeParts.Length is < 2 or > 3 ||
                !int.TryParse(timeParts[0], out hour) ||
                !int.TryParse(timeParts[1], out minute))
                return false;

            if (timeParts.Length == 3 && !int.TryParse(timeParts[2], out var second))
                return false;

            var secondValue = timeParts.Length == 3 ? int.Parse(timeParts[2]) : 0;
            if (hour is < 0 or > 23 || minute is < 0 or > 59 || secondValue is < 0 or > 59)
                return false;

            try
            {
                date = new PersianCalendar().ToDateTime(y, m, d, hour, minute, secondValue, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            date = new PersianCalendar().ToDateTime(y, m, d, hour, minute, 0, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeDigits(string value)
    {
        const string fa = "۰۱۲۳۴۵۶۷۸۹";
        const string ar = "٠١٢٣٤٥٦٧٨٩";

        for (var i = 0; i < 10; i++)
            value = value.Replace(fa[i], (char)('0' + i)).Replace(ar[i], (char)('0' + i));

        return value;
    }
}
