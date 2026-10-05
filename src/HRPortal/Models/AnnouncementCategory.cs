using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public class AnnouncementCategory
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Title { get; set; } = "";
    [MaxLength(500)] public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
