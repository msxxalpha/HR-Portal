namespace HRPortal.Services;

/// <summary>
/// Code-first registration metadata for the Suggestions module.
/// The form itself remains implemented by SuggestionsController and its views.
/// </summary>
public sealed class SuggestionsWorkflowFormProvider : IWorkflowFormDefinitionProvider
{
    public string Code => "Suggestions";
    public string Title => "ثبت و بررسی پیشنهادات";
    public string Description => "فرم اختصاصی ثبت پیشنهاد کارکنان؛ بررسی اولیه دبیرخانه و ارجاع به واحد تخصصی از طریق موتور گردش کار.";
    public bool IsActive => true;
}
