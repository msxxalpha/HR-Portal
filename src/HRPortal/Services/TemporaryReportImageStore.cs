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
    // Outside wwwroot: temporary report images are never served as static files.
    private readonly string _root = Path.Combine(environment.ContentRootPath, "App_Data", "ReportTemp");
    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(4);
    private const int MaximumPages = 100;
    private const int MaximumTiffBytes = 30 * 1024 * 1024;

    public Task<TemporaryReportImageSet> CreateFromTiffAsync(
        byte[] tiffBytes, string ownerKey, string watermark, CancellationToken ct = default)
    {
        if (tiffBytes.Length < 8 || tiffBytes.Length > MaximumTiffBytes ||
            !((tiffBytes[0] == 0x49 && tiffBytes[1] == 0x49 && tiffBytes[2] == 0x2A && tiffBytes[3] == 0x00) ||
              (tiffBytes[0] == 0x4D && tiffBytes[1] == 0x4D && tiffBytes[2] == 0x00 && tiffBytes[3] == 0x2A)))
            throw new InvalidDataException("خروجی IMAGE سرور SSRS از نوع TIFF معتبر نیست یا از حد مجاز بزرگ‌تر است.");

        Directory.CreateDirectory(_root);
        var token = Guid.NewGuid().ToString("N");
        var ownerDirectory = Path.Combine(_root, ownerKey);
        var reportDirectory = Path.Combine(ownerDirectory, token);
        Directory.CreateDirectory(reportDirectory);

        try
        {
            File.WriteAllText(Path.Combine(reportDirectory, ".created"), DateTime.UtcNow.ToString("O"));
            using var input = new MemoryStream(tiffBytes, writable: false);
            using var source = Image.FromStream(input, useEmbeddedColorManagement: true, validateImageData: true);
            var pageDimension = source.FrameDimensionsList
                .Select(id => new FrameDimension(id))
                .FirstOrDefault(d => d.Guid == FrameDimension.Page.Guid) ?? FrameDimension.Page;
            var pageCount = Math.Max(1, source.GetFrameCount(pageDimension));
            if (pageCount > MaximumPages)
                throw new InvalidDataException($"تعداد صفحات گزارش از حد مجاز ({MaximumPages}) بیشتر است.");

            for (var page = 0; page < pageCount; page++)
            {
                ct.ThrowIfCancellationRequested();
                source.SelectActiveFrame(pageDimension, page);
                if ((long)source.Width * source.Height > 30_000_000)
                    throw new InvalidDataException("ابعاد صفحه گزارش از حد مجاز بزرگ‌تر است.");

                using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                var dpiX = float.IsFinite(source.HorizontalResolution) && source.HorizontalResolution >= 96 ? source.HorizontalResolution : 96;
                var dpiY = float.IsFinite(source.VerticalResolution) && source.VerticalResolution >= 96 ? source.VerticalResolution : 96;
                bitmap.SetResolution(dpiX, dpiY);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                    var label = watermark + " | " + DateTime.Now.ToString("yyyy/MM/dd HH:mm");
                    using (var watermarkFont = new Font("Tahoma", Math.Max(18f, source.Width / 48f), FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var watermarkBrush = new SolidBrush(Color.FromArgb(48, 65, 75, 85)))
                    {
                        var measured = graphics.MeasureString(label, watermarkFont);
                        graphics.TranslateTransform(bitmap.Width / 2f, bitmap.Height / 2f);
                        graphics.RotateTransform(-24f);
                        for (var y = -bitmap.Height; y < bitmap.Height; y += Math.Max(180, (int)(measured.Height * 4)))
                            graphics.DrawString(label, watermarkFont, watermarkBrush, -measured.Width / 2f, y);
                        graphics.ResetTransform();
                    }

                    using var footerFont = new Font("Tahoma", Math.Max(12f, source.Width / 95f), FontStyle.Bold, GraphicsUnit.Pixel);
                    using var footerBrush = new SolidBrush(Color.FromArgb(105, 65, 75, 85));
                    using var background = new SolidBrush(Color.FromArgb(35, 255, 255, 255));
                    var footerSize = graphics.MeasureString(label, footerFont);
                    var footerY = Math.Max(0, bitmap.Height - footerSize.Height - 18);
                    graphics.FillRectangle(background, 8, footerY, bitmap.Width - 16, footerSize.Height + 10);
                    graphics.DrawString(label, footerFont, footerBrush, 14, footerY + 5);
                }

                bitmap.Save(GetPagePath(ownerKey, token, page), ImageFormat.Png);
            }

            return Task.FromResult(new TemporaryReportImageSet(token, pageCount));
        }
        catch
        {
            RequestDelete(reportDirectory);
            TryDeleteDirectory(reportDirectory);
            throw;
        }
    }

    public string GetPagePath(string ownerKey, string token, int page)
    {
        if (!Guid.TryParseExact(token, "N", out _) || page < 0 || page >= MaximumPages)
            throw new ArgumentException("شناسه گزارش یا شماره صفحه نامعتبر است.");
        return Path.Combine(_root, ownerKey, token, $"page-{page + 1:000}.png");
    }

    public bool DeleteReport(string ownerKey, string token)
    {
        if (!Guid.TryParseExact(token, "N", out _)) return false;
        var path = Path.Combine(_root, ownerKey, token);
        if (!Directory.Exists(path)) return true;
        RequestDelete(path);
        TryDeleteDirectory(path);
        return !Directory.Exists(path);
    }

    public void DeleteOwner(string ownerKey)
    {
        var path = Path.Combine(_root, ownerKey);
        if (!Directory.Exists(path)) return;
        foreach (var report in Directory.EnumerateDirectories(path))
        {
            RequestDelete(report);
            TryDeleteDirectory(report);
        }
        TryDeleteDirectory(path);
    }

    public void DeleteExpired()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var owner in Directory.EnumerateDirectories(_root))
        {
            foreach (var report in Directory.EnumerateDirectories(owner))
            {
                try
                {
                    var deleteRequested = File.Exists(Path.Combine(report, ".delete-requested"));
                    var marker = Path.Combine(report, ".created");
                    var created = File.Exists(marker) ? File.GetLastWriteTimeUtc(marker) : Directory.GetCreationTimeUtc(report);
                    if (deleteRequested || DateTime.UtcNow - created > MaximumAge)
                        TryDeleteDirectory(report);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try
            {
                if (Directory.Exists(owner) && !Directory.EnumerateFileSystemEntries(owner).Any())
                    Directory.Delete(owner);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void RequestDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                File.WriteAllText(Path.Combine(path, ".delete-requested"), DateTime.UtcNow.ToString("O"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
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
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) { Thread.Sleep(100 * (attempt + 1)); }
            catch (UnauthorizedAccessException) { Thread.Sleep(100 * (attempt + 1)); }
        }
    }
}

public sealed class TemporaryReportImageCleanupService(
    TemporaryReportImageStore store,
    ILogger<TemporaryReportImageCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { store.DeleteExpired(); }
            catch (Exception ex) { logger.LogWarning(ex, "Temporary report image cleanup failed."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}