using System.ComponentModel.DataAnnotations;

namespace HRPortal.Models;

public class InputQuery
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [Required] public string SqlText { get; set; } = "";
    [Required, MaxLength(300)] public string ServerInstance { get; set; } = "";
    [Required, MaxLength(200)] public string DatabaseName { get; set; } = "";
    [MaxLength(20)] public string AuthenticationMode { get; set; } = "sql";
    [MaxLength(300)] public string Username { get; set; } = "";
    public string PasswordProtected { get; set; } = "";
    public bool Encrypt { get; set; }
    public bool TrustServerCertificate { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}