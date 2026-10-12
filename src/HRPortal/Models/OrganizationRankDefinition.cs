using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public class OrganizationRankDefinition
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Title { get; set; } = "";
    public int SortOrder { get; set; } = 10;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
