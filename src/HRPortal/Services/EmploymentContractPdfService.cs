using System.Globalization;
using HRPortal.Models;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRPortal.Services;

/// <summary>
/// Produces the fixed-term employment contract locally with QuestPDF.
/// No network service or remote document renderer is used.
/// </summary>
public sealed class EmploymentContractPdfService(IWebHostEnvironment environment)
{
    private static readonly object FontRegistrationLock = new();
    private static bool _vazirRegistered;

    // Corporate palette: deep navy, restrained champagne accent, and cool neutral surfaces.
    private const string Navy = "#142B42";
    private const string NavyMid = "#203F59";
    private const string NavySoft = "#EAF0F5";
    private const string Gold = "#B59A6A";
    private const string TextColor = "#263746";
    private const string MutedColor = "#647587";
    private const string BorderColor = "#D9E1E8";
    private const string PaperColor = "#F7F9FB";

    public byte[] Generate(Employee employee, ContractRenewalRequest request, bool showApprovedDates)
    {
        RegisterVazirFont();

        var fullName = Join(employee.FirstName, employee.LastName);
        var fatherName = Value(employee.FatherName);
        var birthCertificate = Value(employee.BirthCertificateNumber);
        var nationalId = Value(employee.NationalId);
        var issuePlace = Value(employee.BirthCertificateIssuePlace);
        var birthDate = FormatDate(employee.BirthDate);
        var address = Value(employee.Address);
        var mobile = Value(employee.Mobile);
        var military = Value(employee.MilitaryServiceStatus);
        var marital = Value(employee.MaritalStatus);
        var children = employee.ChildrenCount.HasValue
            ? employee.ChildrenCount.Value.ToString(CultureInfo.InvariantCulture)
            : "";
        var insurance = Value(employee.InsuranceNumber);
        var skill = Value(employee.Skill);
        var hireDate = FormatDate(employee.HireDate);
        var jobType = Value(employee.JobType ?? employee.PositionTitle);
        var startDate = showApprovedDates ? FormatDate(request.NewContractStartDate) : "";
        var endDate = showApprovedDates ? FormatDate(request.NewContractEndDate) : "";

        var articles = new[]
        {
            "۸- کارگر موظف است امور مربوط به شغل خود را زیر نظر کارفرما یا نماینده او یا سرپرست مستقیم انجام داده، چنانچه بنا به نیاز کارفرما مقرر شد کارگر در سایر زیر مجموعه شرکت و دیگر مشاغل و در هر شیفت کاری انجام وظیفه نماید بدون اعتراض شغل مشابه و شیفت پیشنهادی را بپذیرد.",
            "۱۰- کار عادی روزانه ۸ ساعت تعیین و از ساعت ۷ لغایت ۱۵ و ۶ روز و با توجه به ماده ۵۱ قانون کار مفید در هفته(با توجه به ماده۵۱ قانون کار) می‌باشد.",
            "۱۱- اضافه‌کاری با توافق طرفین طبق مقررات و با توجه به مفاد ماده ۵۹ قانون کار انجام خواهد پذیرفت.",
            "۱۲- کارگر در ازای هر ۶ روز کار(۴۴ ساعت کار عادی) استحقاق استفاده از یک روز تعطیل با حق‌السعی را خواهد داشت. چنانچه روز تعطیلی هفتگی کارگر، روزی بغیر از جمعه باشد، مزد روز جمعه ۴۰درصد بیشتر از روزهای دیگر هفته خواهد بود.",
            "۱۳- کارگر در ایام تعطیل رسمی با استفاده از حق‌السعی تعطیل خواهد بود، بدیهی است هر ساعت کار در ایام تعطیل رسمی، کار اضافی موضوع ماده ۵۹ تلقی خواهد شد.",
            "۱۴- مرخصی استحقاقی سالانه کارگر مطابق ماده ۶۴ قانون کار با استفاده از مزد و احتساب چهار روز جمعه، جمعاً یک ماه می‌باشد. سایر روزهــای تعطیــل جــزء ایــام مرخصی محسوب نخواهد شد. برای کار کمتر از یکسال، مرخصی مزبور به نسبت مدت کار انجام یافته محاسبه می‌شود.",
            "۱۵-حق السعی، حقوق و مزایای ماهیانه بر اساس حکم کارگزینی پیوست که جزء لاینفک قرارداد می‌باشد تعیین و پرداخت می‌گردد.",
            "تبصره ۱ - در صورت افزایش حداقل دستمزد بر اساس مصوبه شورای‌عالی کار و تغییرات سطوح مزدی سالیانه کارگر مشمول افزایش مزبور خواهد بود.",
            "تبصره ۲ - حق بیمه و مالیات متعلقه و سایر کسورات قانونی در پایان هر ماه از حقوق و مزایای مشمول کسر خواهد شد و هرگونه غیبت یا کسری کار مشمول کسر حقوق طبق ساعات غیبت خواهد بود.",
            "تبصره ۳ - سایر پرداختی‌های حقی از قبیل حق سرپرستی، فوق‌العاده شغل و سختی کار جزء حقوق ثابت محسوب می‌گردد.",
            "تبصره ۴ - عیدی و پاداش ایام کارکرد طبق قانون و مصوبات شورای‌عالی کار و اطلاعیه وزارت کار قابل محاسبه و پرداخت می‌باشد.",
            "تبصره ۵ - در صورتی‌که بر اساس ضوابط مربوطه کارگر مشمول طرح طبقه‌بندی مشاغل گردد، مزد مبنای وی بر اساس ضوابط طرح تعیین می‌گردد.",
            "تبصره ۶ - کارفرما مکلف است، کمک هزینه اقلام مصرفی(بن کارگر) مطابق مصوبه شورای‌عالی کار تهیه و در اختیار کارگر قرار دهد.",
            "۱۶- کارگر متعهد می‌شود که بطور مرتب از امکانات و وسائل حفاظتی که به وی آموزش داده شده است و در اختیارش قرار گرفته استفاده نماید و بهداشت فردی و محیط زیست را پاکیزه نگهداری کند.",
            "۱۷- در صورت تخلف از مقررات انضباطی کارگاه و نقض قوانین جاری کشور کارفرما حق دارد با ملاحظه ماده ۹ آئین‌نامه قصور قبل از انقضاء قرارداد نسبت به فسخ آن اقدام و طبق ماده ۲۷ قانون کار با نامبرده رفتار نماید(با نظر اداره کار محل).",
            "۱۸- کارفرما مکلف است کارگر را از تاریخ اشتغال به کار طبق مقررات نزد سازمان تامین اجتماعی بیمه نماید.",
            "۱۹- در صورت ترک کار غیر‌موجه از طرف کارگر، کارفرما می‌تواند با نامبرده قطع رابطه نماید و قرارداد مربوطه را فسخ نماید.",
            "۲۰- به هنگام فسخ یا خاتمه قراداد کار حق سنوات مطابق مصوبه مورخه ۵/۸/۱۳۸۷ مجمع تشخیص مصلحت نظام بر اساس نسبت کارکرد خواهد بود که کارگر با دریافت آن هرگونه ادعای بعدی را مورد دریافت مزایای قرارداد از خود سلب می‌نماید.",
            "۲۱- هر گونه اختلاف ناشی از اجرای این قرارداد قابل طرح در مراجع حل اختلاف مذکور در قانون کار می‌باشد.",
            "۲۲- موارد پیش‌بینی نشده در این قرارداد مطابق قانون کار و مقررات مربوطه حل خواهد شد.",
            "۲۳- این قرارداد هیچ‌گونه تعهدی مبنی بر استخدام دائم کارگر نخواهد داشت.",
            "۲۴- این قرارداد بر اساس بند ح ماده ۲۱ قانون کار در صورت هرگونه کاهش تولید و تغییرات ساختاری در اثر شرایط اقتصادی، اجتماعی ولزوم تغییرات گسترده در فن‌آوری فسخ خواهد گردید.",
            "۲۵- در صورت استعفاء کارگر موظف است طبق تبصره بند«و» ماده ۲۱ قانون کار یک ماه زودتر استعفای خود را کتباً به کارفرما اطلاع دهد.",
            "۲۶- در صورتی‌که کارگر کم‌کاری کند یا دچار رکود کاری گردد مفاد مواد ۱۵و ۳۰ قانون کار در مورد کارکنان برای ایشان اعمال خواهد شد.",
            "۲۷- این قرارداد شامل ۲۷ ماده در ۲ نسخه که هر کدام حکم واحد است تنظیم می‌شود که یک نسخه در اختیار کارگر و یک نسخه نزد کارفرما قرارخواهدگرفت."
        };

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.ContentFromRightToLeft();
                page.MarginHorizontal(30);
                page.MarginTop(24);
                page.MarginBottom(27);
                page.DefaultTextStyle(style => style.FontFamily("Vazir").FontSize(11).FontColor(TextColor));

                page.Header().Column(header =>
                {
                    header.Item().Background(Navy).PaddingHorizontal(17).PaddingVertical(14).Row(row =>
                    {
                        row.RelativeItem().Column(title =>
                        {
                            title.Item().AlignRight().Text("قرارداد کار مدت معین")
                                .FontSize(18).Bold().FontColor(Colors.White);
                            title.Item().PaddingTop(4).AlignRight()
                                .Text("شرکت کارخانجات کمک فنر ایندامین سایپا (سهامی عام)")
                                .FontSize(9).FontColor("#E1E8EE");
                            title.Item().PaddingTop(8).Row(meta =>
                            {
                                meta.AutoItem().AlignRight().Text("امور منابع انسانی")
                                    .FontSize(8).FontColor("#D5C29D");
                                meta.AutoItem().PaddingHorizontal(7).AlignCenter()
                                    .Text("•").FontSize(8).FontColor("#D5C29D");
                                meta.AutoItem().AlignRight().Text("سند قرارداد استخدامی")
                                    .FontSize(8).FontColor("#D5C29D");
                            });
                        });
                        row.ConstantItem(132).AlignMiddle().Background(NavyMid)
                            .Border(1).BorderColor("#526A7D").Padding(10).Column(code =>
                            {
                                code.Item().AlignCenter().Text("کد پرسنلی")
                                    .FontSize(9).FontColor("#DFE7ED");
                                code.Item().PaddingTop(5).AlignCenter()
                                    .Text(PersianDigits(employee.PersonnelNumber))
                                    .FontSize(13).Bold().FontColor(Colors.White);
                                code.Item().PaddingTop(4).AlignCenter().Text("EMPLOYEE ID")
                                    .FontSize(6).FontColor("#C2CED7");
                            });
                    });
                    header.Item().Height(3).Background(Gold);
                    header.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().AlignRight().Text("قرارداد کارکنان")
                            .FontSize(8).Bold().FontColor(Navy);
                        row.RelativeItem().AlignLeft().Text("نسخه قابل چاپ")
                            .FontSize(8).FontColor(MutedColor);
                    });
                });

                page.Content().PaddingTop(9).Column(column =>
                {
                    column.Spacing(4);

                    Article(column, true,
                        "۱- بر اساس ماده ۷ و۱۰ قانون کار جمهوری اسلامی ایران و با عنایت به حقوق و تکالیف مقرر در قانون مزبور و مقررات تبعی قرارداد زیر بین شرکت کارخانجات کمک فنر ایندامین سایپا(سهامی عام) به نشانی تهران بزرگراه شهید لشگری کیلومتر ۱۴ چیتگر جنوبی که از این به بعد کارفرما نامیده می‌شود و به شماره شناسنامه ",
                        Field(birthCertificate),
                        " فرزند ",
                        Field(fatherName),
                        " خانم/آقای ",
                        Field(fullName),
                        " تاریخ تولد ",
                        Field(birthDate),
                        " صادره از ",
                        Field(issuePlace),
                        " کد ملی ",
                        Field(nationalId),
                        " تلفن ",
                        Field(mobile),
                        " به نشانی ",
                        Field(address),
                        " از طرف دیگر که از این پس کارگر نامیده می‌شود منعقد می‌گردد .");

                    Article(column, false, "۲- وضعیت نظام وظیفه کارگر ", Field(military));
                    Article(column, false, "تعداد اولاد ", Field(children));
                    Article(column, false, "۳- وضعیت تأهل کارگر ", Field(marital));
                    Article(column, false, "۴- شماره بیمه ", Field(insurance));
                    Article(column, "۵- محل اشتغال اقامتگاه قانونی شرکت کمک فنر ایندامین سایپا می باشد.");
                    Article(column, false, "۶- مهارت کارگر ", Field(skill), " تاریخ استخدام ", Field(hireDate));
                    Article(column, false, "۷- نوع شغل محوله ", Field(jobType));

                    Article(column, false, articles[0]);
                    Article(column, false,
                        "۹- این قرارداد برای انجام در مدت معین تنظیم و اعتبار آن از ",
                        Field(startDate),
                        " لغایت ",
                        Field(endDate),
                        " می‌باشد.");

                    for (var i = 1; i < articles.Length; i++)
                        Article(column, false, articles[i]);

                    column.Item().PaddingTop(13).ShowEntire()
                        .Border(1).BorderColor(BorderColor)
                        .Background(PaperColor).Padding(12).Column(signatures =>
                        {
                            signatures.Item().Row(caption =>
                            {
                                caption.RelativeItem().AlignRight().Text("محل امضاء و اثر انگشت طرفین")
                                    .FontSize(10).Bold().FontColor(Navy);
                                caption.AutoItem().AlignLeft().Text("SIGNATURES")
                                    .FontSize(7).Bold().FontColor(Gold);
                            });
                            signatures.Item().PaddingTop(8).Row(sign =>
                            {
                                sign.RelativeItem().Border(1).BorderColor(BorderColor)
                                    .Background(Colors.White).Padding(10).Column(employer =>
                                    {
                                        employer.Item().AlignCenter().Text("کارفرما")
                                            .FontSize(9).Bold().FontColor(Navy);
                                        employer.Item().PaddingTop(5).AlignCenter()
                                            .Text("نام و نام خانوادگی کارفرما")
                                            .FontSize(10).Bold();
                                        employer.Item().PaddingTop(25).AlignCenter()
                                            .Text("امضاء")
                                            .FontSize(9).FontColor(MutedColor);
                                        employer.Item().PaddingTop(7).LineHorizontal(0.7f)
                                            .LineColor(BorderColor);
                                    });
                                sign.ConstantItem(10);
                                sign.RelativeItem().Border(1).BorderColor(BorderColor)
                                    .Background(Colors.White).Padding(10).Column(worker =>
                                    {
                                        worker.Item().AlignCenter().Text("کارگر")
                                            .FontSize(9).Bold().FontColor(Navy);
                                        worker.Item().PaddingTop(5).AlignCenter()
                                            .Text("نام و نام خانوادگی کارگر")
                                            .FontSize(10).Bold();
                                        worker.Item().PaddingTop(25).AlignCenter()
                                            .Text("امضاء - اثر انگشت")
                                            .FontSize(9).FontColor(MutedColor);
                                        worker.Item().PaddingTop(7).LineHorizontal(0.7f)
                                            .LineColor(BorderColor);
                                    });
                            });
                        });
                });

                page.Footer().PaddingTop(7).Column(footer =>
                {
                    footer.Item().Height(1).Background(Gold);
                    footer.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem().AlignRight().Text("شرکت کارخانجات کمک فنر ایندامین سایپا (سهامی عام)")
                            .FontSize(8).FontColor(MutedColor);
                        row.RelativeItem().AlignLeft().Text("سند داخلی منابع انسانی")
                            .FontSize(8).FontColor(MutedColor);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static DynamicField Field(string? value) =>
        new(PersianDigits(value?.Trim() ?? ""));

    private static void Article(ColumnDescriptor column, string content) =>
        Article(column, false, PersianDigits(content));

    private static void Article(ColumnDescriptor column, bool emphasized, params object[] parts)
    {
        var item = column.Item();
        var paragraph = item
            .BorderBottom(0.45f).BorderColor(BorderColor)
            .PaddingHorizontal(7).PaddingVertical(emphasized ? 8 : 4)
            .AlignRight();

        // Apply every wrapper before adding the paragraph's single content child.
        // Reusing the original container in a second fluent chain can assign a
        // second child and cause QuestPDF's DocumentComposeException.
        if (emphasized)
        {
            paragraph = paragraph.Background(NavySoft)
                .BorderRight(3).BorderColor(Navy);
        }

        paragraph.Text(text =>
        {
            // Text(Action<TextDescriptor>) returns void; apply paragraph typography
            // through the descriptor's default style instead of chaining on Text().
            text.DefaultTextStyle(style =>
                style.FontSize(11).LineHeight(emphasized ? 1.55f : 1.48f));

            foreach (var part in parts)
            {
                if (part is DynamicField field)
                {
                    if (string.IsNullOrWhiteSpace(field.Value))
                        continue;

                    // Non-breaking spaces keep every variable value visually separated
                    // from the fixed Persian words on both sides, including line wraps.
                    text.Span("\u00A0");
                    text.Span(field.Value).Bold().FontColor(Navy);
                    text.Span("\u00A0");
                }
                else if (part is string literal)
                {
                    text.Span(PersianDigits(literal));
                }
            }
        });
    }

    private sealed record DynamicField(string Value);

    private void RegisterVazirFont()
    {
        lock (FontRegistrationLock)
        {
            if (_vazirRegistered) return;

            var windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var candidates = new[]
            {
                Path.Combine(environment.WebRootPath ?? "", "fonts", "Vazir-Regular.ttf"),
                Path.Combine(environment.WebRootPath ?? "", "fonts", "Vazir.ttf"),
                Path.Combine(environment.ContentRootPath, "App_Data", "Fonts", "Vazir-Regular.ttf"),
                Path.Combine(environment.ContentRootPath, "App_Data", "Fonts", "Vazir.ttf"),
                Path.Combine(windowsFonts, "Vazir-Regular.ttf"),
                Path.Combine(windowsFonts, "Vazir.ttf")
            };
            var regularPath = candidates.FirstOrDefault(File.Exists);
            if (regularPath is null)
                throw new InvalidOperationException("برای تولید قرارداد PDF با فونت وزیر، فایل Vazir-Regular.ttf یا Vazir.ttf را در wwwroot/fonts یا App_Data/Fonts برنامه قرار دهید یا فونت وزیر را روی سرور نصب کنید.");

            using (var regular = File.OpenRead(regularPath))
                FontManager.RegisterFont(regular);

            var boldCandidates = new[]
            {
                Path.Combine(Path.GetDirectoryName(regularPath) ?? "", "Vazir-Bold.ttf"),
                Path.Combine(Path.GetDirectoryName(regularPath) ?? "", "Vazir Bold.ttf")
            };
            var boldPath = boldCandidates.FirstOrDefault(File.Exists);
            if (boldPath is not null)
            {
                using var bold = File.OpenRead(boldPath);
                FontManager.RegisterFont(bold);
            }

            _vazirRegistered = true;
        }
    }

    private static string Join(string? first, string? last) =>
        string.Join(" ", new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

    private static string Value(string? value) => PersianDigits(value?.Trim() ?? "");

    private static string FormatDate(DateTime? value) =>
        value.HasValue ? PersianDigits(PersianDateService.Format(value.Value.Date)) : "";

    private static string PersianDigits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        const string western = "0123456789";
        const string arabic = "٠١٢٣٤٥٦٧٨٩";
        const string persian = "۰۱۲۳۴۵۶۷۸۹";
        for (var i = 0; i < 10; i++)
            value = value.Replace(western[i], persian[i]).Replace(arabic[i], persian[i]);
        return value;
    }
}
