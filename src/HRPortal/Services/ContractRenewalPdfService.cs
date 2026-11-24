using System.Globalization;
using HRPortal.Models;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRPortal.Services;

/// <summary>One-page A4 management summary for a completed contract-renewal evaluation.</summary>
public sealed class ContractRenewalPdfService
{
    private const string Navy = "#18344D";
    private const string NavyLight = "#EAF0F5";
    private const string Accent = "#B18A4A";
    private const string Muted = "#647587";
    private const string Border = "#D9E1E8";
    private static readonly object FontLock = new();
    private static bool _fontRegistered;

    public byte[] Generate(
        ContractRenewalRequest request,
        IReadOnlyList<WorkflowStep> steps,
        IReadOnlyList<WorkflowTask> tasks,
        IReadOnlyList<EmployeePerformanceEvaluationScore> scores,
        IReadOnlyList<WorkflowHistory> history)
    {
        RegisterLocalFont();

        // Historical score rows can outlive or lose their WorkflowStep navigation.
        // Ignore orphaned/non-evaluation rows rather than failing the entire PDF.
        var evaluationScores = scores
            .Where(x => x.WorkflowStep is not null &&
                x.WorkflowStep.Code?.StartsWith("Evaluation_H", StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(x => x.WorkflowStep!.SortOrder).ThenBy(x => x.QuestionId).ThenBy(x => x.Id)
            .ToList();

        var evaluators = evaluationScores
            .GroupBy(x => x.EvaluatorEmployeeId ?? 0)
            .Select(group => new
            {
                Id = group.Key,
                Name = group.Select(x => x.EvaluatorEmployee is null
                    ? "ارزیاب نامشخص"
                    : $"{x.EvaluatorEmployee.FirstName} {x.EvaluatorEmployee.LastName}".Trim()).FirstOrDefault()
                    ?? "ارزیاب نامشخص",
                FirstOrder = group.Min(x => x.WorkflowStep!.SortOrder)
            })
            .OrderBy(x => x.FirstOrder).ThenBy(x => x.Id).ToList();

        var questions = evaluationScores
            .GroupBy(x => x.QuestionId)
            .Select(group => new
            {
                QuestionId = group.Key,
                Text = group.OrderBy(x => x.WorkflowStep!.SortOrder).First().QuestionTextSnapshot,
                MaxScore = group.Max(x => x.MaxScoreSnapshot),
                Scores = group
            })
            .OrderBy(x => evaluationScores.Where(s => s.QuestionId == x.QuestionId)
                 .Min(s => s.WorkflowStep!.SortOrder))
            .ThenBy(x => x.QuestionId).ToList();

        var initialScore = request.OriginalEvaluationScore ?? evaluationScores
            .Where(x => x.Score.HasValue)
            .GroupBy(x => x.WorkflowStepId)
            .OrderBy(x => x.Key)
            .Select(g => (decimal?)g.Sum(x => x.Score!.Value))
            .FirstOrDefault();

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.ContentFromRightToLeft();
                page.MarginHorizontal(22);
                page.MarginVertical(18);
                page.DefaultTextStyle(x => x.FontFamily("Tahoma").FontSize(7.5f));

                page.Header().Column(header =>
                {
                    header.Item().Background(Navy).PaddingHorizontal(12).PaddingVertical(9).Row(row =>
                    {
                        row.RelativeItem().AlignRight().Column(title =>
                        {
                            title.Item().Text("گزارش خلاصه ارزشیابی و تمدید قرارداد")
                                .FontSize(15).Bold().FontColor(Colors.White);
                            title.Item().PaddingTop(2).Text("خلاصه مدیریتی نهایی پس از تکمیل گردش کار")
                                .FontSize(8).FontColor("#DCE6EE");
                        });
                        row.ConstantItem(112).AlignLeft().AlignMiddle().Background(Accent).Padding(6)
                            .Text(request.RequestCode).FontSize(9).Bold().FontColor(Colors.White);
                    });
                    header.Item().PaddingTop(5).LineHorizontal(1.5f).LineColor(Accent);
                });

                // Shrink the complete composition to the available A4 content area.
                page.Content().Shrink().PaddingTop(7).Column(column =>
                {
                    column.Spacing(5);

                    column.Item().Element(c => SectionHeader(c, "مشخصات کارمند و درخواست"));
                    column.Item().Border(1).BorderColor(Border).Background("#FBFCFD").Padding(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            Pair(c, "نام و نام خانوادگی", request.EmployeeNameSnapshot);
                            Pair(c, "واحد سازمانی", request.OrganizationUnitSnapshot);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            Pair(c, "شماره پرسنلی", request.PersonnelNumberSnapshot);
                            Pair(c, "جایگاه سازمانی", request.PositionTitleSnapshot);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            Pair(c, "تاریخ ثبت", PersianDateService.Format(request.CreatedAt.ToLocalTime()));
                            Pair(c, "تاریخ تکمیل", request.CompletedAt.HasValue
                                ? PersianDateService.Format(request.CompletedAt.Value.ToLocalTime()) : "—");
                        });
                    });

                    column.Item().Element(c => SectionHeader(c, "خلاصه نتایج و تصمیم نهایی"));
                    column.Item().Row(row =>
                    {
                        Metric(row.RelativeItem(), "امتیاز اولیه", initialScore?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—", "/ ۱۰۰");
                        Metric(row.RelativeItem(), "درصد کسر", request.HrDisciplinaryDeductionPercent.ToString("0.##", CultureInfo.InvariantCulture) + "٪", "");
                        Metric(row.RelativeItem(), "امتیاز نهایی", request.FinalEvaluationScore?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—", "/ ۱۰۰");
                        Metric(row.RelativeItem(), "مدت مصوب", request.FinalContractMonths?.ToString(CultureInfo.InvariantCulture) ?? "—", "ماه");
                        Metric(row.RelativeItem(), "تعداد ارزیاب", evaluators.Count.ToString(CultureInfo.InvariantCulture), "نفر");
                    });

                    column.Item().Element(c => SectionHeader(c, "جزئیات پرسش‌ها و امتیاز ارزیابان"));
                    if (questions.Count == 0)
                    {
                        column.Item().Border(1).BorderColor(Border).Padding(8).AlignCenter()
                            .Text("برای این درخواست امتیاز ارزیابی ثبت نشده است.");
                    }
                    else
                    {
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(5);
                                cols.RelativeColumn(0.85f);
                                foreach (var evaluator in evaluators)
                                    cols.RelativeColumn(1.05f);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell).Text("پرسش ارزشیابی");
                                header.Cell().Element(HeaderCell).AlignCenter().Text("سقف");
                                foreach (var evaluator in evaluators)
                                    header.Cell().Element(HeaderCell).AlignCenter()
                                        .Text(evaluator.Name).FontSize(6.5f);
                            });

                            var index = 0;
                            foreach (var question in questions)
                            {
                                var alternate = index++ % 2 == 1;
                                table.Cell().Element(c => DataCell(c, alternate)).Text(question.Text).FontSize(7);
                                table.Cell().Element(c => DataCell(c, alternate)).AlignCenter()
                                    .Text(question.MaxScore.ToString("0.##", CultureInfo.InvariantCulture));
                                foreach (var evaluator in evaluators)
                                {
                                    var score = question.Scores
                                        .Where(x => (x.EvaluatorEmployeeId ?? 0) == evaluator.Id)
                                         .OrderByDescending(x => x.WorkflowStep!.SortOrder)
                                        .Select(x => x.Score)
                                        .FirstOrDefault();
                                    table.Cell().Element(c => DataCell(c, alternate)).AlignCenter()
                                        .Text(score.HasValue
                                            ? score.Value.ToString("0.##", CultureInfo.InvariantCulture)
                                            : "—").Bold();
                                }
                            }
                        });
                    }

                    column.Item().Element(c => SectionHeader(c, "سوابق و تصمیم‌های کلیدی"));
                    column.Item().Border(1).BorderColor(Border).Padding(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            Pair(c, "تصمیم حراست", request.SecurityDecision switch
                            {
                                "ApproveFull" => "موافقت با تمدید قرارداد",
                                "ApproveOneMonth" => "موافقت با تمدید یک‌ماهه",
                                "Reject" => "عدم موافقت",
                                _ => "ثبت نشده"
                            });
                            Pair(c, "پیشنهاد مدت منابع انسانی", request.HrProposedContractMonths.HasValue
                                ? request.HrProposedContractMonths + " ماه" : "—");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            Pair(c, "کسر امتیاز انضباطی", request.HrDisciplinaryDeductionPercent.ToString("0.##", CultureInfo.InvariantCulture) + "٪");
                            Pair(c, "مدت نهایی مدیرعامل", request.FinalContractMonths.HasValue
                                ? request.FinalContractMonths + " ماه" : "—");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            Pair(c, "تاریخ شروع قرارداد", request.NewContractStartDate.HasValue
                                ? PersianDateService.Format(request.NewContractStartDate.Value) : "—");
                            Pair(c, "تاریخ پایان قرارداد", request.NewContractEndDate.HasValue
                                ? PersianDateService.Format(request.NewContractEndDate.Value) : "—");
                        });
                    });

                    var lastHistory = history.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
                    column.Item().AlignRight().Text("آخرین اقدام ثبت‌شده: " +
                        (lastHistory is null ? "—" : lastHistory.Action + " | " +
                            PersianDateService.Format(lastHistory.CreatedAt.ToLocalTime())))
                        .FontSize(7).FontColor(Muted);
                });

                page.Footer().PaddingTop(5).BorderTop(1).BorderColor(Border).Row(row =>
                {
                    row.RelativeItem().AlignRight().Text("پورتال جامع منابع انسانی | گزارش سیستمی")
                        .FontSize(7).FontColor(Muted);
                    row.RelativeItem().AlignLeft().Text("شماره درخواست: " + request.RequestCode)
                        .FontSize(7).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }

    private static void Pair(ColumnDescriptor column, string label, string? value)
    {
        column.Item().Row(row =>
        {
            row.AutoItem().Text(label + " : ").FontSize(7).Bold().FontColor(Muted);
            row.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(7);
        });
    }

    private static void Metric(IContainer container, string label, string value, string suffix)
    {
        container.Border(1).BorderColor(Border).Background(NavyLight).PaddingVertical(5).PaddingHorizontal(3)
            .Column(c =>
            {
                c.Item().AlignCenter().Text(label).FontSize(7).FontColor(Muted);
                c.Item().PaddingTop(2).AlignCenter().Text(t =>
                {
                    t.Span(value).FontSize(12).Bold().FontColor(Navy);
                    if (!string.IsNullOrEmpty(suffix))
                        t.Span(" " + suffix).FontSize(6.5f).FontColor(Muted);
                });
            });
    }

    private static void SectionHeader(IContainer container, string title)
    {
        container.Background(Navy).PaddingHorizontal(7).PaddingVertical(4)
            .AlignRight().Text(title).FontSize(8.5f).Bold().FontColor(Colors.White);
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(NavyLight).Border(0.5f).BorderColor(Border)
            .PaddingVertical(4).PaddingHorizontal(3).AlignMiddle().AlignRight();

    private static IContainer DataCell(IContainer container, bool alternate) =>
        container.Background(alternate ? "#F6F8FA" : Colors.White)
            .Border(0.5f).BorderColor(Border).PaddingVertical(3).PaddingHorizontal(3)
            .AlignMiddle().AlignRight();

    private static void RegisterLocalFont()
    {
        lock (FontLock)
        {
            if (_fontRegistered) return;
            var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var font in new[] { "tahoma.ttf", "arial.ttf" })
            {
                var path = Path.Combine(fontsDirectory, font);
                if (!File.Exists(path)) continue;
                using var stream = File.OpenRead(path);
                FontManager.RegisterFont(stream);
                _fontRegistered = true;
                return;
            }
            throw new InvalidOperationException("فونت فارسی Tahoma یا Arial روی سرور یافت نشد.");
        }
    }
}
