# ماژول ارزیابی عملکرد کارکنان — برنامه انتقال ایمن

## مبنا
- مخزن مقصد: HR-Portal؛ شاخه مستقل `feature/performance-evaluation-module`
- منبع: EmployeePerformanceSystem
- نسخه اصلی HRPortal بدون تغییر می‌ماند. هیچ اسکریپت حذف/بازسازی دیتابیس اجرا نشود.
- هدف فنی: .NET 8 / EF Core 8 سازگار با HRPortal.

## قواعد ادغام
1. **Employees:** تنها `HRPortal.Models.Employee` مرجع اطلاعات پرسنلی است. اتصال رکورد ارزیابی با `Employee.Id` انجام شود؛ شناسه‌های پایگاه داده پروژه مستقل بدون نگاشت وارد نشوند.
2. **ساختار سازمانی:** تنها `OrganizationNode` و `OrganizationStructureRevision` معتبر هستند. برای هر دوره، نسخه مبنای ساختار ذخیره و مسیر ارزیاب با آن محاسبه شود. صرف تعلق به یک واحد به معنای رابطه سرپرستی نیست.
3. **رده‌های پستی:** `Employee.PositionTitle` فعلاً متنی است؛ کاتالوگ اختصاصی رده و نگاشت صریح به کارمند/جایگاه طراحی شود؛ هیچ فیلد فعلی حذف نشود.
4. **احراز هویت:** کنترلر Account، کوکی، OTP، نشست و مجوزهای HRPortal دست‌نخورده بمانند. `AppUser` و `PasswordHasher` پروژه مستقل منتقل نشوند.
5. **پایگاه داده:** تنها جداول اختصاصی ماژول با نام‌های متمایز مانند `PerformanceEvaluationPeriods`، `PerformanceEvaluations`، `PerformanceScores`، `PerformanceScoreHistory`، `PerformanceEvaluatorHistory`، `PerformanceDomains`، `PerformanceQuestions`، `PerformancePositionQuestions` ایجاد شوند. نگاشت‌های FK و داده‌های موجود پیش از اعمال اسکریپت بررسی شوند.
6. **MVC:** کنترلرها و Viewهای ماژول در مسیرهای اختصاصی مانند `/Performance/...` قرار گیرند تا با `AdminController`، `AccountController`، `ReportsController` و مسیرهای فعلی تداخل نداشته باشند.
7. **مجوزها:** سیاست‌های `Performance.View`، `Performance.Evaluate`، `Performance.Manage`، `Performance.Reports` در `RoleService.DefaultPermissions` ثبت شوند و در سمت سرور اعمال گردند.
8. **حفظ تاریخچه:** سقف امتیاز، متن سؤال، رده و نسخه ساختار در زمان ارزیابی باید قابل بازسازی باشند؛ تغییرات اطلاعات پایه نباید تاریخچه را بازنویسی کنند.
9. **دوره حذف‌شده:** دوره‌ها و داده‌های تاریخی با سیاست آرشیو مشخص مدیریت شوند؛ حذف فیزیکی بدون بررسی سوابق مجاز نیست.

## ترتیب کار
- [x] ایجاد شاخه ایزوله از HRPortal
- [x] بررسی اولیه مدل‌ها، DbContext، احراز هویت و تفاوت نسخه‌های .NET
- [ ] تهیه نگاشت دقیق مدل‌های مشترک و طراحی جداول اختصاصی
- [ ] افزودن مدل‌ها و سرویس‌های مستقل ماژول بدون تغییر مسیرهای موجود
- [ ] طراحی اسکریپت افزایشی دیتابیس با کنترل وجود جداول/ستون‌ها
- [ ] انتقال صفحات، گزارش‌ها و محاسبات ارزیابی
- [ ] اتصال به نقش‌ها، مجوزها و ساختار سازمانی HRPortal
- [ ] آزمون Build، سناریوهای ارزیابی، رگرسیون ورود، کارکنان، سازمان، فیش و حکم
- [ ] ایجاد PR برای بررسی؛ ادغام در main فقط پس از تأیید

## موارد حساس
- HRPortal: `HRPortalDbContext` و مدل `Employee` شامل PersonnelNumber، OrganizationUnitId/DepartmentId/SectionId و PositionTitle است.
- پروژه ارزیابی: `AppDbContext` دارای مدل‌های مستقل Employee، OrgUnit، Position و AppUser است؛ این مدل‌ها نباید کپی مستقیم شوند.
- ارتباط رئیس/سرپرست با کارمند نیازمند نگاشت جایگاه و متصدی سازمانی است.
- تمام تغییرات این شاخه باید بدون تخریب داده‌های موجود و قابل بازگشت باشند.
