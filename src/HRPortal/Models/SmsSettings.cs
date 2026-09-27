namespace HRPortal.Models;

public class SmsSettings
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public string ProviderName { get; set; } = "";
    public string ServiceUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string SenderNumber { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string OtpTemplate { get; set; } = "کد ورود شما: {code}";
    public bool VerifySsl { get; set; } = true;
}