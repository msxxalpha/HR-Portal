namespace HRPortal.Models;

public sealed class SecureReportViewerVm
{
    public required string Token { get; init; }
    public required int PageCount { get; init; }
    public required string PageUrl { get; init; }
    public required string CloseUrl { get; init; }
    public required string AntiForgeryToken { get; init; }
    public required string Title { get; init; }
}