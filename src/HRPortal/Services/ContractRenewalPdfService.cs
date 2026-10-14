using HRPortal.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRPortal.Services;

/// <summary>
/// Generates contract-renewal PDFs entirely in-process. No external service or network
/// request is used; fonts are read from the local Windows fonts directory.
/// </summary>
public sealed class ContractRenewalPdfService
{
    public byte[] Generate(
        ContractRenewalRequest request,
        IReadOnlyList<WorkflowStep> steps,
        IReadOnlyList<WorkflowTask> tasks,
        IReadOnlyList<EmployeePerformanceEvaluationScore> scores,
        IReadOnlyList<WorkflowHistory> history)
    {
        RegisterLocalFont();
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.ContentFromRightToLeft();
                page.Margin(28);
                page.DefaultTextStyle(style => style.FontFamily("Tahoma").FontSize(9));
                page.Header().Column(header =>
                {
                    header.Item().AlignRight().Text("گزارش نهایی ارزشیابی و تمدید قرارداد").FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                    header.Item().AlignRight().Text($"شماره درخواست: {request.RequestCode}").FontSize(10).SemiBold();
                    header.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Blue.Darken2);
                });
                page.Content().PaddingVertical(12).Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Element(c => SectionTitle(c, "۱. مشخصات درخواست و کارمند"));
                    column.Item().Element(c => KeyValueTable(c, new (string, string)[]
                    {
                        ("نام کارمند", request.EmployeeNameSnapshot),
                        ("شماره پرسنلی", request.PersonnelNumberSnapshot),
                        ("واحد سازمانی", request.OrganizationUnitSnapshot),
                        ("جایگاه سازمانی", request.PositionTitleSnapshot),
                        ("وضعیت درخواست", request.Status),
                        ("تاریخ ثبت", FormatDate(request.CreatedAt)),
                        ("تاریخ تکمیل", request.CompletedAt.HasValue ? FormatDate(request.CompletedAt.Value) : "—"),
                        ("پایان قرارداد قبلی", request.PreviousContractEndDate.HasValue ? PersianDateService.Format(request.PreviousContractEndDate.Value) : "—"),
                        ("تمدیدهای قبلی", $"{request.PreviousExtensionMonths1}، {request.PreviousExtensionMonths2}، {request.PreviousExtensionMonths3} ماه"),
                        ("امتیاز دوره قبلی اول", request.PreviousPeriodScore1.ToString("0.##")),
                        ("امتیاز دوره قبلی دوم", request.PreviousPeriodScore2.ToString("0.##"))
                    }));

                    column.Item().Element(c => SectionTitle(c, "۲. اطلاعات تکمیلی عملکرد و سوابق"));
                    column.Item().Element(c => KeyValueTable(c, new (string, string)[]
                    {
                        ("اخطار انضباطی", request.HasDisciplinaryWarnings ? $"بله؛ تعداد {request.DisciplinaryWarningCount}" : "خیر"),
                        ("آرای کمیته انضباطی", request.HasDisciplinaryCommitteeResolution ? request.DisciplinaryCommitteeResolutionCount.ToString() : "۰"),
                        ("مرخصی بدون حقوق", request.HasUnpaidLeave ? $"{request.UnpaidLeaveDays} روز" : "۰ روز"),
                        ("مرخصی استعلاجی", request.HasSickLeave ? $"{request.SickLeaveDays} روز" : "۰ روز"),
                        ("مرخصی حادثه", request.HasAccidentLeave ? $"{request.AccidentLeaveDays} روز" : "۰ روز"),
                        ("تقدیرنامه", request.HasCommendations ? request.CommendationCount.ToString() : "۰")
                    }));

                    column.Item().Element(c => SectionTitle(c, "۳. گردش کار و ارزشیابی مرحله‌ای"));
                    foreach (var step in steps.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
                    {
                        var stepTasks = tasks.Where(x => x.WorkflowStepId == step.Id).OrderBy(x => x.CreatedAt).ToList();
                        var stepScores = scores.Where(x => x.WorkflowStepId == step.Id).OrderBy(x => x.Id).ToList();
                        column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(stage =>
                        {
                            stage.Spacing(5);
                            stage.Item().Row(row =>
                            {
                                row.RelativeItem().AlignRight().Text(step.Title).Bold().FontSize(11).FontColor(Colors.Blue.Darken3);
                                row.ConstantItem(100).AlignLeft().Text(stepTasks.Any(x => x.Status == "Completed") ? "انجام‌شده" : stepTasks.Any(x => x.Status == "Pending") ? "در انتظار اقدام" : "بدون اقدام").FontSize(8);
                            });
                            stage.Item().AlignRight().Text("جایگاه: " + (step.OrganizationNode?.Title ?? (step.AssignmentType == "Hierarchy" ? "مسیر سلسله‌مراتبی" : "—"))).FontSize(8).FontColor(Colors.Grey.Darken2);
                            if (stepScores.Count > 0)
                            {
                                stage.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(4);
                                        columns.RelativeColumn(2);
                                        columns.ConstantColumn(38);
                                        columns.ConstantColumn(38);
                                    });
                                    table.Header(header =>
                                    {
                                        header.Cell().Element(HeaderCell).Text("پرسش");
                                        header.Cell().Element(HeaderCell).Text("ارزیاب");
                                        header.Cell().Element(HeaderCell).Text("سقف");
                                        header.Cell().Element(HeaderCell).Text("امتیاز");
                                    });
                                    foreach (var score in stepScores)
                                    {
                                        table.Cell().Element(BodyCell).Text(score.QuestionTextSnapshot);
                                        table.Cell().Element(BodyCell).Text(score.EvaluatorEmployee is null ? "—" : $"{score.EvaluatorEmployee.FirstName} {score.EvaluatorEmployee.LastName}");
                                        table.Cell().Element(BodyCell).Text(score.MaxScoreSnapshot.ToString("0.##"));
                                        table.Cell().Element(BodyCell).Text(score.Score?.ToString("0.##") ?? "—");
                                    }
                                    table.Cell().ColumnSpan(2).Element(BodyCell).Text("مجموع امتیاز مرحله").Bold();
                                    table.Cell().Element(BodyCell).Text(stepScores.Sum(x => x.MaxScoreSnapshot).ToString("0.##"));
                                    table.Cell().Element(BodyCell).Text(stepScores.All(x => x.Score.HasValue) ? stepScores.Sum(x => x.Score!.Value).ToString("0.##") : "ناقص").Bold();
                                });
                            }
                            foreach (var task in stepTasks)
                            {
                                stage.Item().Text("مسئول: " + (task.AssignedEmployee is not null ? task.AssignedEmployee.FirstName + " " + task.AssignedEmployee.LastName : task.AssignedPosition?.Title ?? "—")).FontSize(8);
                                stage.Item().Text($"اقدام: {task.Action ?? task.Status} | زمان: {FormatDate(task.CompletedAt ?? task.CreatedAt)}").FontSize(8);
                                stage.Item().Text("نظر مرحله: " + (string.IsNullOrWhiteSpace(task.Comment) ? "نظری ثبت نشده است." : task.Comment)).FontSize(8);
                            }
                        });
                    }

                    column.Item().Element(c => SectionTitle(c, "۴. جمع‌بندی تصمیم‌های نهایی"));
                    column.Item().Element(c => KeyValueTable(c, new (string, string)[]
                    {
                        ("مجموع امتیاز نهایی ارزشیابی", request.FinalEvaluationScore.HasValue ? request.FinalEvaluationScore.Value.ToString("0.##") + " از ۱۰۰" : "ثبت نشده"),
                        ("تصمیم حراست", request.SecurityDecision switch { "ApproveFull" => "موافقت", "ApproveOneMonth" => "موافقت با تمدید یک‌ماهه", "Reject" => "عدم موافقت", _ => "ثبت نشده" }),
                        ("نظر مدیر حراست", request.SecurityComment ?? "ثبت نشده"),
                        ("پیشنهاد مدت قرارداد منابع انسانی", request.HrProposedContractMonths.HasValue ? request.HrProposedContractMonths.Value + " ماه" : "ثبت نشده"),
                        ("نظر مدیر منابع انسانی و خدمات پشتیبانی", request.HrComment ?? "ثبت نشده"),
                        ("مدت نهایی مصوب مدیرعامل", request.FinalContractMonths.HasValue ? request.FinalContractMonths.Value + " ماه" : "ثبت نشده"),
                        ("نظر مدیرعامل", request.CeoComment ?? "ثبت نشده")
                    }));

                    column.Item().Element(c => SectionTitle(c, "۵. تاریخچه اقدامات سامانه"));
                    if (history.Count == 0)
                        column.Item().AlignRight().Text("رویدادی در تاریخچه ثبت نشده است.");
                    foreach (var item in history.OrderBy(x => x.CreatedAt))
                    {
                        column.Item().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Column(entry =>
                        {
                            entry.Item().AlignRight().Text($"{item.Action} — {FormatDate(item.CreatedAt)}").Bold();
                            entry.Item().AlignRight().Text($"اقدام‌کننده: {(item.ActorEmployee is null ? "سیستم" : item.ActorEmployee.FirstName + " " + item.ActorEmployee.LastName)} | جایگاه: {item.ActorPosition?.Title ?? "—"}").FontSize(8);
                            entry.Item().AlignRight().Text("توضیحات: " + (string.IsNullOrWhiteSpace(item.Comment) ? "—" : item.Comment)).FontSize(8);
                        });
                    }
                });
                page.Footer().PaddingTop(6).BorderTop(1).BorderColor(Colors.Grey.Lighten2).Row(row =>
                {
                    row.RelativeItem().AlignRight().Text($"گزارش سیستمی | {DateTime.Now:yyyy/MM/dd HH:mm}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.RelativeItem().AlignLeft().Text(text =>
                    {
                        text.Span("صفحه ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        text.CurrentPageNumber();
                        text.Span(" از ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        text.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void RegisterLocalFont()
    {
        var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!File.Exists(Path.Combine(fontsDirectory, "tahoma.ttf")) &&
            !File.Exists(Path.Combine(fontsDirectory, "arial.ttf")))
            throw new InvalidOperationException("فونت فارسی محلی Tahoma یا Arial در پوشه فونت‌های سرور یافت نشد؛ برای تولید PDF باید فونت مناسب روی سرور نصب باشد.");
    }

    private static string FormatDate(DateTime value) => PersianDateService.Format(value.ToLocalTime()) + " " + value.ToLocalTime().ToString("HH:mm");

    private static void SectionTitle(IContainer container, string title)
    {
        container.Background(Colors.Blue.Darken3).Padding(7).AlignRight()
            .Text(title).FontColor(Colors.White).Bold().FontSize(11);
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten2).Border(1).BorderColor(Colors.Grey.Lighten1)
            .Padding(4).AlignRight().AlignMiddle();

    private static IContainer BodyCell(IContainer container) =>
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().AlignMiddle();

    private static void KeyValueTable(IContainer container, IReadOnlyList<(string Label, string Value)> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1);
                columns.RelativeColumn(2);
            });
            foreach (var row in rows)
            {
                table.Cell().Element(HeaderCell).Text(row.Label).Bold();
                table.Cell().Element(BodyCell).Text(string.IsNullOrWhiteSpace(row.Value) ? "—" : row.Value);
            }
        });
    }
}
