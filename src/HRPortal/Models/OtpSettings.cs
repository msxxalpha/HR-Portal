namespace HRPortal.Models;

public class OtpSettings
{
    public int Id { get; set; }
    public int Length { get; set; } = 5;
    public int ValiditySeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 5;
    public bool Enabled { get; set; } = true;
}