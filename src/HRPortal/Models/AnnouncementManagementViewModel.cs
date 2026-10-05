namespace HRPortal.Models;

public class AnnouncementManagementViewModel
{
    public AnnouncementEditModel Form { get; set; } = new();
    public List<Announcement> Announcements { get; set; } = [];
    public List<AnnouncementCategory> Categories { get; set; } = [];
    public string CurrentSystemDateTime { get; set; } = "";
}
