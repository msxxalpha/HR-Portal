namespace HRPortal.Models;

public class AnnouncementEditModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Body { get; set; } = "";
    public int CategoryId { get; set; }
    public string StartAt { get; set; } = "";
    public string EndAt { get; set; } = "";
    public string Priority { get; set; } = "normal";
    public bool IsPinned { get; set; } = true;
    public bool IsActive { get; set; } = true;
}
