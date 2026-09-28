namespace HRPortal.Models;

public class SmsSettings
{
    public int Id { get; set; }
    public bool Enabled { get; set; }

    // The field set mirrors the working supplier corrective-action WordPress module.
    public string Endpoint { get; set; } = "";
    public string Method { get; set; } = "POST";
    public string Format { get; set; } = "json";
    public string AuthMode { get; set; } = "header";
    public string ApiKeyName { get; set; } = "Api-Key";
    public string ApiKey { get; set; } = "";

    public string SenderField { get; set; } = "sender";
    public string Sender { get; set; } = "";
    public string RecipientField { get; set; } = "recipient";
    public string RecipientMode { get; set; } = "scalar";
    public string MessageField { get; set; } = "message";
    public string NumberFormatField { get; set; } = "";
    public string NumberFormat { get; set; } = "";
    public string StaticParams { get; set; } = "";
    public string SuccessCodes { get; set; } = "200-299";

    public string Template { get; set; } =
        "کاربر محترم، کد ورود شما: {code}";

    public string TestRecipient { get; set; } = "";
}
