using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using HRPortal.Models;
using PDFtoImage;
using SkiaSharp;

namespace HRPortal.Services;

public sealed class EngineeringDocumentFileStore
{
    private const long MaximumPdfBytes = 35L * 1024 * 1024;

    public async Task<byte[]> ReadPdfAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length < 8 || file.Length > MaximumPdfBytes)
            throw new InvalidDataException("حجم فایل PDF باید بیشتر از صفر و حداکثر ۳۵ مگابایت باشد.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("فقط فایل با پسوند PDF قابل بارگذاری است.");

        await using var input = file.OpenReadStream();
        var signature = new byte[5];
        var read = await input.ReadAsync(signature.AsMemory(), cancellationToken);
        if (read != 5 || !signature.AsSpan().SequenceEqual("%PDF-"u8))
            throw new InvalidDataException("محتوای فایل انتخاب‌شده یک PDF معتبر نیست.");

        input.Position = 0;
        using var buffer = new MemoryStream(checked((int)file.Length));
        await input.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}

public sealed record EngineeringDocumentImageSet(string Token, int PageCount);

public sealed class EngineeringDocumentImageStore(IWebHostEnvironment environment)
{
    private const int MaximumPages = 100;
    private const long MaximumPagePixels = 30_000_000;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(4);
    private readonly string _root = Path.Combine(environment.ContentRootPath, "App_Data", "EngineeringDocumentTemp");

    public async Task<EngineeringDocumentImageSet> CreateAsync(
        byte[] pdfContent, int documentId, string ownerKey, string watermark, CancellationToken cancellationToken = default)
    {
        if (documentId <= 0)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (pdfContent is null || pdfContent.Length < 8 || pdfContent.Length > 35L * 1024 * 1024 ||
            !pdfContent.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new InvalidDataException("محتوای PDF ذخیره‌شده معتبر نیست.");

        Directory.CreateDirectory(_root);
        var token = Guid.NewGuid().ToString("N");
        var ownerDirectory = Path.Combine(_root, ownerKey);
        var reportDirectory = Path.Combine(ownerDirectory, token);
        Directory.CreateDirectory(reportDirectory);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            // PDFium reads the database BLOB directly from memory. No PDF file is
            // staged on disk and no external executable (such as pdftoppm) is needed.
            var pageCount = await Task.Run(() => Conversion.GetPageCount(pdfContent), timeout.Token);
            if (pageCount <= 0)
                throw new InvalidDataException("فایل PDF هیچ صفحه قابل نمایشی ندارد.");
            if (pageCount > MaximumPages)
                throw new InvalidDataException($"تعداد صفحات مستند نمی‌تواند بیشتر از {MaximumPages} صفحه باشد.");

            var pageIndex = 0;
            var renderOptions = new RenderOptions(Dpi: 120, UseTiling: true);
            await foreach (var renderedPage in Conversion.ToImagesAsync(
                pdfContent,
                Enumerable.Range(0, pageCount),
                options: renderOptions,
                cancellationToken: timeout.Token))
            {
                using (renderedPage)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if ((long)renderedPage.Width * renderedPage.Height > MaximumPagePixels)
                        throw new InvalidDataException("ابعاد یکی از صفحات مستند از حد مجاز بیشتر است.");

                    using var encoded = new MemoryStream();
                    if (!renderedPage.Encode(encoded, SKEncodedImageFormat.Png, 100))
                        throw new InvalidDataException("تبدیل صفحه PDF به تصویر PNG ناموفق بود.");
                    encoded.Position = 0;

                    using (var source = Image.FromStream(encoded))
                    using (var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
                    {
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(Color.White);
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                            DrawWatermark(graphics, bitmap.Width, bitmap.Height, watermark);
                        }

                        bitmap.Save(GetPagePath(ownerKey, token, pageIndex), ImageFormat.Png);
                    }
                }

                pageIndex++;
            }

            if (pageIndex == 0)
                throw new InvalidDataException("از فایل PDF هیچ صفحه قابل نمایشی تولید نشد.");

            File.WriteAllText(Path.Combine(reportDirectory, ".document-id"),
                documentId.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(Path.Combine(reportDirectory, ".created"),
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            return new EngineeringDocumentImageSet(token, pageIndex);
        }
        catch
        {
            TryDeleteDirectory(reportDirectory);
            throw;
        }
    }

    public string GetPagePath(string ownerKey, string token, int page)
    {
        if (!Guid.TryParseExact(token, "N", out _) || page < 0 || page >= MaximumPages)
            throw new ArgumentException("شناسه تصویر یا شماره صفحه نامعتبر است.");
        return Path.Combine(_root, ownerKey, token, $"page-{page + 1:000}.png");
    }

    public void Delete(string ownerKey, string token)
    {
        if (!Guid.TryParseExact(token, "N", out _)) return;
        TryDeleteDirectory(Path.Combine(_root, ownerKey, token));
    }

    public void DeleteForDocuments(IEnumerable<int> documentIds)
    {
        try
        {
            var ids = documentIds.Where(x => x > 0).ToHashSet();
            if (ids.Count == 0 || !Directory.Exists(_root)) return;

            foreach (var owner in Directory.EnumerateDirectories(_root))
            {
                foreach (var directory in Directory.EnumerateDirectories(owner))
                {
                    try
                    {
                        var marker = Path.Combine(directory, ".document-id");
                        if (!File.Exists(marker)) continue;
                        var value = File.ReadAllText(marker).Trim();
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var documentId) &&
                            ids.Contains(documentId))
                            TryDeleteDirectory(directory);
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
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void DeleteExpired()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var owner in Directory.EnumerateDirectories(_root))
        {
            foreach (var directory in Directory.EnumerateDirectories(owner))
            {
                try
                {
                    var marker = Path.Combine(directory, ".created");
                    var created = File.Exists(marker) ? File.GetLastWriteTimeUtc(marker) : Directory.GetCreationTimeUtc(directory);
                    if (DateTime.UtcNow - created > MaximumAge)
                        TryDeleteDirectory(directory);
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

    private static void DrawWatermark(Graphics graphics, int width, int height, string watermark)
    {
        var label = (watermark ?? "کاربر سامانه") + " | " + DateTime.Now.ToString("yyyy/MM/dd HH:mm");
        using var font = new Font("Tahoma", Math.Max(18f, width / 55f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(34, 85, 96, 108));
        var measured = graphics.MeasureString(label, font);
        graphics.TranslateTransform(width / 2f, height / 2f);
        graphics.RotateTransform(-25f);
        var step = Math.Max(160, (int)(measured.Height * 5));
        for (var y = -height; y < height; y += step)
            graphics.DrawString(label, font, brush, -measured.Width / 2f, y);
        graphics.ResetTransform();

        using var footerFont = new Font("Tahoma", Math.Max(12f, width / 100f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var footerBrush = new SolidBrush(Color.FromArgb(100, 85, 96, 108));
        using var footerBackground = new SolidBrush(Color.FromArgb(30, 255, 255, 255));
        var footer = graphics.MeasureString(label, footerFont);
        var footerY = Math.Max(0, height - footer.Height - 16);
        graphics.FillRectangle(footerBackground, 8, footerY, width - 16, footer.Height + 8);
        graphics.DrawString(label, footerFont, footerBrush, 12, footerY + 4);
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 4; attempt++)
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

public sealed class EngineeringDocumentImageCleanupService(
    EngineeringDocumentImageStore store,
    ILogger<EngineeringDocumentImageCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { store.DeleteExpired(); }
            catch (Exception exception) { logger.LogWarning(exception, "Engineering document image cleanup failed."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}