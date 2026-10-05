using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public class Announcement
{
    public int Id { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = "";
    [MaxLength(700)] public string Summary { get; set; } = "";
    [Required] public string Body { get; set; } = "";
    public int CategoryId { get; set; }
    public AnnouncementCategory? Category { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    [MaxLength(20)] public string Priority { get; set; } = "normal";
    public bool IsPinned { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
