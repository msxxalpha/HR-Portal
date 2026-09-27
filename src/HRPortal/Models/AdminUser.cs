using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public class AdminUser
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = "";

    [Required, MaxLength(1000)]
    public string PasswordHash { get; set; } = "";

    [MaxLength(200)]
    public string DisplayName { get; set; } = "مدیر سامانه";

    public bool IsActive { get; set; } = true;

    public bool MustChangePassword { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}