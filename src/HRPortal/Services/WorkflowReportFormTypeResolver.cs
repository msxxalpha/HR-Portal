namespace HRPortal.Services;

/// <summary>
/// Resolves the operational form identity for workflow activity reporting.
/// Definitions and runtime instances can have different workflow codes while
/// representing the same submitted form; EntityType is the fallback for
/// legacy/internal workflows that do not carry a FormCode.
/// </summary>
public static class WorkflowReportFormTypeResolver
{
    public static string ResolveCode(string? formCode, string? entityType)
    {
        if (!string.IsNullOrWhiteSpace(formCode))
            return formCode.Trim();

        var entity = entityType?.Trim();
        if (string.IsNullOrWhiteSpace(entity))
            return "workflow:unknown";

        return entity switch
        {
            "OutsourcedServiceRequest" => "OutsourcedServices",
            "Suggestion" => "Suggestions",
            // Contract-renewal workflows are intentionally created as one
            // internal runtime definition per request, so never group by its
            // per-request workflow definition code.
            "ContractRenewalRequest" => "ContractRenewalRequest",
            _ => entity
        };
    }

    public static string ResolveTitle(string code, IReadOnlyDictionary<string, string> formTitles)
    {
        if (formTitles.TryGetValue(code, out var registeredTitle) &&
            !string.IsNullOrWhiteSpace(registeredTitle))
            return registeredTitle;

        return code switch
        {
            "OutsourcedServices" => "سفارش خدمات برونسپاری",
            "Suggestions" => "ثبت و بررسی پیشنهادات",
            "ContractRenewalRequest" => "تمدید قرارداد",
            "workflow:unknown" => "گردش کار",
            _ => code
        };
    }
}
