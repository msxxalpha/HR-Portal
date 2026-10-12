using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public class OrganizationPositionDefinition
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = "";
    [Required, MaxLength(150)] public string Title { get; set; } = "";
    [MaxLength(100)] public string? JobFamily { get; set; }
    [MaxLength(50)] public string? Grade { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public int SortOrder { get; set; } = 10;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
