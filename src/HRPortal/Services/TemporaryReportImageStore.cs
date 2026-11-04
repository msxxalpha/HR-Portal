using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace HRPortal.Services;

public sealed record TemporaryReportImageSet(string Token, int PageCount);

public static class ReportTempOwner
{
    public static string GetKey(System.Security.Claims.ClaimsPrincipal user)
    {
        var identity = user.FindFirst("PersonnelNumber")?.Value
            ?? user.FindFirst("EmployeeId")?.Value
            ?? user.FindFirst("AdminId")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("شناسه مالک گزارش مشخص نیست.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
}

public sealed class TemporaryReportImageStore(IWebHostEnvironment environment)
{
    private readonly string _root = Path.Combine(environment.ContentRootPath, "App_Data", "ReportTemp");
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(20);

    public Task<TemporaryReportImageSet> CreateFromTiffAsync(byte[] tiffBytes, string ownerKey, string watermark, CancellationToken ct = default)
    {
        if (tiffBytes.Length < 8 || !((tiffBytes[0] == 0x49 && tiffBytes[1] == 0x49) || (tiffBytes[0] == 0x4D && tiffBytes[1] == 0x4D)))
            throw new InvalidDataException("خروجی IMAGE سرور SSRS از نوع TIFF معتبر نیست.");

        Directory.CreateDirectory(_root);
        var token = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(_root, ownerKey, token);
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, ".created"), DateTime.UtcNow.ToString("O"));
            using var input = new MemoryStream(tiffBytes, writable: false);
            using var source = Image.FromStream(input, true, true);
            var pageDimension = source.FrameDimensionsList.Select(id => new FrameDimension(id))
                .FirstOrDefault(d => d.Guid == FrameDimension.Page.Guid) ?? FrameDimension.Page;
            var count = Math.Max(1, source.GetFrameCount(pageDimension));
            for (var page = 0; page < count; page++)
            {
                ct.ThrowIfCancellationRequested();
                source.SelectActiveFrame(pageDimension, page);
                using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                bitmap.SetResolution(Math.Max(96, source.HorizontalResolution), Math.Max(96, source.VerticalResolution));
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                    using var font = new Font("Tahoma", Math.Max(12f, source.Width / 95f), FontStyle.Bold, GraphicsUnit.Pixel);
                    using var brush = new SolidBrush(Color.FromArgb(105, 65, 75, 85));
                    using var bg = new SolidBrush(Color.FromArgb(35, 255, 255, 255));
                    var label = watermark + " | " + DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                    var size = graphics.MeasureString(label, font);
                    graphics.FillRectangle(bg, 8, Math.Max(0, bitmap.Height - size.Height - 18), bitmap.Width - 16, size.Height + 10);
                    graphics.DrawString(label, font, brush, 14, Math.Max(0, bitmap.Height - size.Height - 12));
                }
                bitmap.Save(GetPagePath(ownerKey, token, page), ImageFormat.Png);
            }
            return Task.FromResult(new TemporaryReportImageSet(token, count));
        }
        catch
        {
            TryDeleteDirectory(folder);
            throw;
        }
    }

    public string GetPagePath(string ownerKey, string token, int page)
    {
        if (!Guid.TryParseExact(token, "N", out _) || page < 0 || page > 99) throw new ArgumentException("شناسه گزارش یا شماره صفحه نامعتبر است.");
        return Path.Combine(_root, ownerKey, token, $"page-{page + 1:000}.png");
    }

    public bool DeleteReport(string ownerKey, string token)
    {
        if (!Guid.TryParseExact(token, "N", out _)) return false;
        var path = Path.Combine(_root, ownerKey, token);
        if (!Directory.Exists(path)) return false;
        TryDeleteDirectory(path);
        return true;
    }

    public void DeleteOwner(string ownerKey)
    {
        var path = Path.Combine(_root, ownerKey);
        if (Directory.Exists(path)) TryDeleteDirectory(path);
    }

    public void DeleteExpired()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var owner in Directory.EnumerateDirectories(_root))
        foreach (var report in Directory.EnumerateDirectories(owner))
        {
            try
            {
                var marker = Path.Combine(report, ".created");
                var created = File.Exists(marker) ? File.GetLastWriteTimeUtc(marker) : Directory.GetCreationTimeUtc(report);
                if (DateTime.UtcNow - created > MaximumAge) TryDeleteDirectory(report);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            Directory.Delete(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class TemporaryReportImageCleanupService(TemporaryReportImageStore store, ILogger<TemporaryReportImageCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { store.DeleteExpired(); }
            catch (Exception ex) { logger.LogWarning(ex, "Temporary report image cleanup failed."); }
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }
}
