using HRPortal.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRPortal.Services;

public sealed class OutsourcedServicePrintPdfDocument(OutsourcedServicePrintReportVm model) : IDocument
{
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(22);
            page.DefaultTextStyle(x => x.FontFamily("Tahoma").FontSize(8).FontColor("#243447"));
            page.Content().Column(column =>
            {
                column.Spacing(8);
                column.Item().Background("#173653").Padding(12).Column(header =>
                {
                    header.Item().AlignCenter().Text("گزارش نهایی درخواست خدمات برونسپاری")
                        .FontSize(15).Bold().FontColor("#FFFFFF");
                    header.Item().AlignCenter().Text("سوابق درخواست و گردش تأیید")
                        .FontSize(8).FontColor("#DCE7F1");
                    header.Item().AlignCenter().Text($"شماره درخواست: {model.RequestNumber}")
                        .FontSize(9).Bold().FontColor("#FFFFFF");
                });

                column.Item().Background("#EEF2F6").Padding(8).Row(row =>
                {
                    row.RelativeItem().Text($"وضعیت نهایی: {model.WorkflowStatusTitle}").Bold();
                    row.RelativeItem().AlignLeft().Text($"تاریخ ثبت: {model.CreatedAt}");
                });

                Section(column, "۱. مشخصات درخواست‌کننده و درخواست");
                column.Item().Border(1).BorderColor("#D7E0E8").Padding(8).Column(details =>
                {
                    details.Spacing(5);
                    details.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"درخواست‌کننده: {model.RequesterName}");
                        row.RelativeItem().Text($"شماره پرسنلی: {model.PersonnelNumber}");
                    });
                    details.Item().Text($"واحد سازمانی درخواست‌کننده: {model.OrganizationalUnit}").Bold();
                    details.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"تاریخ نیازمندی: {model.NeedByDate}");
                        row.RelativeItem().Text($"اولویت: {model.Priority}");
                        row.RelativeItem().Text($"مقدار: {model.Quantity:0.###} {model.UnitOfMeasure}");
                    });
                    details.Item().Text("شرح خدمات درخواستی").Bold().FontColor("#173653");
                    details.Item().Background("#F7F9FB").Padding(7).Text(model.ServiceDescription);
                    if (model.Attachments.Count > 0)
                        details.Item().Text("پیوست‌ها: " + string.Join("، ", model.Attachments));
                });

                Section(column, "۲. سوابق گردش کار و تأییدها");
                if (model.History.Count == 0)
                    column.Item().Text("سابقه‌ای برای گردش کار ثبت نشده است.");
                else
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(22);
                            columns.RelativeColumn(1.3f);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(1.0f);
                            columns.RelativeColumn(1.5f);
                        });
                        table.Header(header =>
                        {
                            HeaderCell(header.Cell(), "ردیف");
                            HeaderCell(header.Cell(), "مرحله / مسئول");
                            HeaderCell(header.Cell(), "نتیجه اقدام");
                            HeaderCell(header.Cell(), "تاریخ");
                            HeaderCell(header.Cell(), "توضیحات");
                        });
                        foreach (var item in model.History)
                        {
                            BodyCell(table.Cell(), item.Sequence.ToString());
                            BodyCell(table.Cell(), item.Stage + "\n" + item.Actor +
                                (string.IsNullOrWhiteSpace(item.PersonnelNumber) ? "" : $" ({item.PersonnelNumber})"));
                            BodyCell(table.Cell(), item.Action);
                            BodyCell(table.Cell(), item.Date);
                            BodyCell(table.Cell(), string.IsNullOrWhiteSpace(item.Comment) ? "—" : item.Comment);
                        }
                    });

                column.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text($"شروع گردش: {model.WorkflowStartedAt}");
                    row.RelativeItem().Text($"پایان گردش: {model.WorkflowCompletedAt}");
                });
                column.Item().PaddingTop(4).BorderTop(1).BorderColor("#CBD5E1").PaddingTop(5)
                    .Text($"تاریخ تهیه گزارش: {model.GeneratedAt}").FontSize(7).FontColor("#64748B");
            });
        });
    }

    private static void Section(ColumnDescriptor column, string title) =>
        column.Item().PaddingTop(2).Background("#E8EEF4").Padding(6)
            .Text(title).Bold().FontSize(9).FontColor("#173653");

    private static void HeaderCell(IContainer cell, string value) =>
        cell.Background("#294A68").Padding(5).AlignCenter().Text(value)
            .Bold().FontColor("#FFFFFF").FontSize(7);

    private static void BodyCell(IContainer cell, string value) =>
        cell.BorderBottom(1).BorderColor("#E2E8F0").Padding(4).Text(value).FontSize(7);
}
