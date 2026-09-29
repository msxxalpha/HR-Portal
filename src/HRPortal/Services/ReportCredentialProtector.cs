using Microsoft.AspNetCore.DataProtection;

namespace HRPortal.Services;

public sealed class ReportCredentialProtector
{
    private readonly IDataProtector _protector;

    public ReportCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("HRPortal.PayrollReportCredentials.v1");
    }

    public string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return _protector.Protect(value);
    }

    public string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        try
        {
            return _protector.Unprotect(value);
        }
        catch
        {
            return "";
        }
    }
}
