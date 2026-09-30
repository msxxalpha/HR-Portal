using System.Globalization;
namespace HRPortal.Services;

public static class PersianDateService
{
    public static string Format(DateTime date) { var p = new PersianCalendar(); return $"{p.GetYear(date):0000}/{p.GetMonth(date):00}/{p.GetDayOfMonth(date):00}"; }
    public static bool TryParse(string? value, out DateTime date) { date = default; if (string.IsNullOrWhiteSpace(value)) return false; var s = value.Trim().Replace("-", "/"); var p = s.Split('/'); if (p.Length != 3 || !int.TryParse(p[0], out var y) || !int.TryParse(p[1], out var m) || !int.TryParse(p[2], out var d)) return false; try { date = new PersianCalendar().ToDateTime(y, m, d, 0, 0, 0, 0); return true; } catch { return false; } }
}