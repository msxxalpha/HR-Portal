namespace HRPortal.Services;

public sealed class OutsourcedServiceWorkflowFormProvider : IWorkflowFormDefinitionProvider
{
    public string Code => "OutsourcedServices";
    public string Title => "سفارش خدمات برونسپاری";
    public string Description => "ثبت درخواست خدمات موردنیاز جهت ارجاع به پیمانکار برون‌سازمانی پس از طی تأییدیه‌های گردش کار.";
    public bool IsActive => true;
}