using System.Globalization;
using HRPortal.Models;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRPortal.Services;

public sealed class WorkflowProcessPdfService
{
    private const string Navy = "#18344D", Light = "#EAF0F5", Accent = "#B18A4A", Muted = "#647587", Border = "#D9E1E8";
    private static readonly object FontLock = new();
    private static bool _fontRegistered;

    public byte[] Generate(WorkflowInstance instance, IReadOnlyList<WorkflowStep> steps,
        IReadOnlyList<WorkflowTask> tasks, IReadOnlyList<WorkflowFieldValue> fields,
        IReadOnlyList<WorkflowHistory> history, IReadOnlyList<(string Label, string Value)>? requestDetails = null)
    {
        RegisterLocalFont();
        var orderedSteps = steps.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.ContentFromRightToLeft();
            page.MarginHorizontal(28);
            page.MarginVertical(22);
            page.DefaultTextStyle(x => x.FontFamily("Tahoma").FontSize(8));
            page.Header().Column(header =>
            {
                header.Item().Background(Navy).Padding(12).Row(row =>
                {
                    row.RelativeItem().Column(title =>
                    {
                        title.Item().Text("گزارش تفصیلی گردش کار").FontSize(16).Bold().FontColor(Colors.White);
                        title.Item().PaddingTop(3).Text(instance.WorkflowDefinition?.Title ?? instance.EntityType).FontSize(9).FontColor("#DCE6EE");
                    });
                    row.ConstantItem(105).AlignLeft().AlignMiddle().Background(Accent).Padding(7)
                        .Text("شناسه گردش: " + instance.Id).FontSize(8).Bold().FontColor(Colors.White);
                });
                header.Item().PaddingTop(5).LineHorizontal(1.5f).LineColor(Accent);
            });
            page.Content().PaddingTop(10).Column(column =>
            {
                column.Spacing(8);
                column.Item().Element(c => Section(c, "مشخصات کلی گردش کار"));
                column.Item().Border(1).BorderColor(Border).Background("#FBFCFD").Padding(8).Column(c =>
                {
                    Pair(c,"عنوان گردش کار",instance.WorkflowDefinition?.Title);
                    Pair(c,"نوع فرم / موجودیت",instance.EntityType);
                    Pair(c,"شناسه درخواست",instance.EntityId);
                    Pair(c,"درخواست‌کننده",instance.RequesterEmployee is null ? "—" : (instance.RequesterEmployee.FirstName+" "+instance.RequesterEmployee.LastName).Trim());
                    Pair(c,"وضعیت گردش",StatusTitle(instance.Status));
                    Pair(c,"تاریخ شروع",PersianDateService.FormatDateTime(PersianDateService.ToTehran(instance.StartedAt)));
                    Pair(c,"تاریخ پایان",instance.CompletedAt.HasValue ? PersianDateService.FormatDateTime(PersianDateService.ToTehran(instance.CompletedAt.Value)) : "—");
                    Pair(c,"مرحله جاری / پایانی",instance.CurrentStep?.Title ?? "پایان گردش");
                });
                if (requestDetails is { Count: > 0 })
                {
                    column.Item().Element(c => Section(c,"اطلاعات اختصاصی درخواست"));
                    column.Item().Border(1).BorderColor(Border).Padding(8).Column(c =>
                    { foreach (var item in requestDetails) Pair(c,item.Label,item.Value); });
                }
                column.Item().Element(c => Section(c,"مراحل گردش کار به ترتیب اجرا"));
                foreach (var step in orderedSteps)
                {
                    var st = tasks.Where(x=>x.WorkflowStepId==step.Id).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToList();
                    var sf = fields.Where(x=>x.WorkflowStepId==step.Id).OrderBy(x=>x.WorkflowStepField?.SortOrder??0).ThenBy(x=>x.Id).ToList();
                    var sh = history.Where(x=>x.WorkflowStepId==step.Id).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToList();
                    column.Item().Border(1).BorderColor(Border).Column(box =>
                    {
                        box.Item().Background(Light).Padding(7).Row(r =>
                        { r.RelativeItem().Text(step.Title).FontSize(10).Bold().FontColor(Navy); r.AutoItem().Text(StepStatus(st)).FontSize(8).Bold().FontColor(Muted); });
                        box.Item().Padding(8).Column(d =>
                        {
                            d.Spacing(4);
                            Pair(d,"جایگاه سازمانی",step.OrganizationNode?.Title ?? (step.AssignedEmployeeId.HasValue ? "کارمند مشخص" : step.AssignmentType=="Hierarchy" ? "مسیر سلسله‌مراتبی" : "—"));
                            Pair(d,"نوع تخصیص",step.AssignmentType);
                            Pair(d,"تعداد وظایف",st.Count.ToString(CultureInfo.InvariantCulture));
                            if(sf.Count>0)
                            {
                                d.Item().PaddingTop(4).Text("اطلاعات ثبت‌شده در این مرحله").Bold().FontColor(Navy);
                                foreach(var f in sf) Pair(d,f.FieldTitle,FormatField(f));
                            }
                            if(st.Count>0)
                            {
                                d.Item().PaddingTop(4).Text("اقدامات، مسئولان و توضیحات").Bold().FontColor(Navy);
                                foreach(var t in st)
                                    d.Item().Border(.5f).BorderColor(Border).Padding(6).Column(x=>
                                    {
                                        Pair(x,"مسئول",t.AssignedEmployee is null ? t.AssignedPosition?.Title ?? "تخصیص‌نیافته" : (t.AssignedEmployee.FirstName+" "+t.AssignedEmployee.LastName).Trim());
                                        Pair(x,"وضعیت وظیفه",StatusTitle(t.Status)); Pair(x,"اقدام",t.Action);
                                        Pair(x,"زمان ایجاد",PersianDateService.FormatDateTime(PersianDateService.ToTehran(t.CreatedAt)));
                                        Pair(x,"زمان تکمیل",t.CompletedAt.HasValue ? PersianDateService.FormatDateTime(PersianDateService.ToTehran(t.CompletedAt.Value)) : "—");
                                        Pair(x,"توضیحات",string.IsNullOrWhiteSpace(t.Comment)?"بدون توضیح":t.Comment);
                                    });
                            }
                            if(sh.Count>0)
                            {
                                d.Item().PaddingTop(4).Text("رویدادهای مرحله").Bold().FontColor(Navy);
                                foreach(var h in sh) HistoryEntry(d,h);
                            }
                        });
                    });
                }
                column.Item().Element(c=>Section(c,"تاریخچه کامل و زمان‌بندی اقدامات"));
                if(history.Count==0) column.Item().Border(1).BorderColor(Border).Padding(8).Text("رویدادی ثبت نشده است.");
                else foreach(var h in history.OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id)) column.Item().Element(c=>HistoryEntry(c,h));
                column.Item().PaddingTop(5).AlignRight().Text("این گزارش از سوابق ثبت‌شده سامانه تهیه شده است.").FontSize(7).FontColor(Muted);
            });
            page.Footer().BorderTop(1).BorderColor(Border).PaddingTop(5).Row(row =>
            {
                row.RelativeItem().AlignRight().Text("پورتال جامع منابع انسانی | گزارش رسمی گردش کار").FontSize(7).FontColor(Muted);
                row.RelativeItem().AlignLeft().Text("شناسه گردش: "+instance.Id).FontSize(7).FontColor(Muted);
            });
        })).GeneratePdf();
    }

    private static void HistoryEntry(IContainer container, WorkflowHistory h)
    {
        var actor=h.ActorEmployee is null ? h.ActorPosition?.Title ?? "سیستم" : (h.ActorEmployee.FirstName+" "+h.ActorEmployee.LastName).Trim();
        container.Border(.5f).BorderColor(Border).Padding(6).Column(x=>
        {
            x.Item().Row(r=>{r.RelativeItem().Text(ActionTitle(h.Action)).Bold().FontColor(Navy);r.AutoItem().Text(PersianDateService.FormatDateTime(PersianDateService.ToTehran(h.CreatedAt))).FontSize(7).FontColor(Muted);});
            Pair(x,"مرحله",h.WorkflowStep?.Title ?? "مرحله عمومی / بدون مرحله"); Pair(x,"انجام‌دهنده",actor);
            Pair(x,"تغییر وضعیت",StatusTitle(h.FromStatus)+" ← "+StatusTitle(h.ToStatus));
            Pair(x,"شرح و توضیحات",string.IsNullOrWhiteSpace(h.Comment)?"بدون توضیح":h.Comment);
        });
    }
    private static string FormatField(WorkflowFieldValue f)=>f.FieldType=="Checkbox"?(f.Value=="true"?"بله":f.Value=="false"?"خیر":"—"):(string.IsNullOrWhiteSpace(f.Value)?"—":f.Value!);
    private static string StepStatus(List<WorkflowTask> t)=>t.Any(x=>x.Action=="UnevaluatedTimeout")?"عبور خودکار / بدون ارزشیابی":t.Any(x=>x.Status=="Completed")?"اقدام انجام شده":t.Any(x=>x.Status=="Pending")?"در انتظار اقدام":t.Any(x=>x.Status=="Superseded")?"جایگزین / تغییر مسیر":"بدون وظیفه ثبت‌شده";
    private static string StatusTitle(string? s)=>s switch {"InProgress"=>"در حال گردش","Approved"=>"تأیید نهایی","Completed"=>"تکمیل‌شده","CompletedBySkip"=>"پایان با عبور از مرحله","Cancelled"=>"لغو شده","Rejected"=>"رد شده","Returned"=>"برگشت داده شده","Pending"=>"در انتظار اقدام","Superseded"=>"جایگزین‌شده",_=>string.IsNullOrWhiteSpace(s)?"—":s};
    private static string ActionTitle(string? a)=>a switch {"Created"=>"ایجاد گردش کار","Submit" or "Submitted"=>"ثبت و ارسال درخواست","Approve"=>"تأیید مرحله","Reject"=>"رد درخواست","Return"=>"بازگشت برای اصلاح","SaveDraft"=>"ذخیره پیش‌نویس","SkipStep"=>"عبور از مرحله","UnevaluatedTimeout"=>"عبور خودکار به علت پایان مهلت","RouteCorrected"=>"اصلاح مسیر گردش","EditIntake"=>"اصلاح اطلاعات اولیه",_=>string.IsNullOrWhiteSpace(a)?"رویداد گردش کار":a};
    private static void Pair(ColumnDescriptor c,string label,string? value)=>c.Item().Row(r=>{r.ConstantItem(118).Text(label+" : ").FontSize(7.5f).Bold().FontColor(Muted);r.RelativeItem().Text(string.IsNullOrWhiteSpace(value)?"—":value).FontSize(7.5f);});
    private static void Section(IContainer c,string title)=>c.Background(Navy).PaddingHorizontal(8).PaddingVertical(5).AlignRight().Text(title).FontSize(9).Bold().FontColor(Colors.White);
    private static void RegisterLocalFont()
    {
        lock(FontLock)
        {
            if(_fontRegistered)return;
            var dir=Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach(var name in new[]{"tahoma.ttf","arial.ttf"})
            {var path=Path.Combine(dir,name);if(!File.Exists(path))continue;using var stream=File.OpenRead(path);FontManager.RegisterFont(stream);_fontRegistered=true;return;}
            throw new InvalidOperationException("فونت فارسی Tahoma یا Arial روی سرور یافت نشد.");
        }
    }
}