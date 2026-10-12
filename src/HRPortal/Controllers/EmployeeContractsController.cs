using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public class EmployeeContractsController(
    HRPortalDbContext db,
    EmployeeExcelService excel,
    AuditService audit,
    RoleService roles,
    ContractRenewalWorkflowService renewalWorkflow) : Controller
{
    [Authorize(Policy = "ContractRenewal.Manage")]
    public IActionResult Index() => View();

    [Authorize(Policy = "ContractRenewal.Manage")]
    public async Task<IActionResult> Dashboard()
    {
        var today = DateTime.Today;
        var limit = today.AddDays(30);
        var employees = await db.Employees.AsNoTracking()
            .Include(x => x.OrganizationUnit)
            .Include(x => x.OrganizationDepartment)
            .Include(x => x.OrganizationSection)
            .Where(x => x.Status == "فعال" &&
                x.EmploymentType != "غیرقراردادی" && x.EmploymentType != "غیر قراردادی" &&
                x.ContractEndDate.HasValue && x.ContractEndDate.Value <= limit)
            .OrderBy(x => x.ContractEndDate).ThenBy(x => x.LastName).ThenBy(x => x.FirstName)
            .ToListAsync();

        var requests = await db.ContractRenewalRequests.AsNoTracking()
            .Include(x => x.WorkflowInstance)
            .Where(x => x.Status != "حذف شده")
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync();

        var rows = new List<ContractRenewalExpiryRow>();
        foreach (var employee in employees)
        {
            var endDate = employee.ContractEndDate!.Value.Date;
            var matching = requests.Where(x => x.EmployeeId == employee.Id &&
                x.PreviousContractEndDate.HasValue && x.PreviousContractEndDate.Value.Date == endDate).ToList();
            var blockingRequest = matching.FirstOrDefault(x =>
                x.Status != "متوقف شده" &&
                x.WorkflowInstance?.Status != "Cancelled");
            rows.Add(new ContractRenewalExpiryRow
            {
                EmployeeId = employee.Id,
                FullName = $"{employee.FirstName} {employee.LastName}".Trim(),
                PersonnelNumber = employee.PersonnelNumber,
                OrganizationUnit = employee.OrganizationSection?.Title ??
                    employee.OrganizationDepartment?.Title ?? employee.OrganizationUnit?.Title ?? "—",
                PositionTitle = employee.PositionTitle ?? "—",
                ContractEndDate = endDate,
                DaysRemaining = (endDate - today).Days,
                LatestRequest = blockingRequest ?? matching.FirstOrDefault(),
                CanStartRequest = blockingRequest is null
            });
        }

        var currentRequests = requests.Where(x => x.Status != "حذف شده").ToList();
        bool isInProgress(ContractRenewalRequest request) =>
            request.WorkflowInstance?.Status == "InProgress" ||
            request.Status is "در حال شروع" or "در حال ارزیابی سازمانی";
        bool isCompleted(ContractRenewalRequest request) =>
            request.Status == "تکمیل شده" || request.WorkflowInstance?.Status == "Approved";
        bool isStopped(ContractRenewalRequest request) =>
            request.Status == "متوقف شده" || request.WorkflowInstance?.Status == "Cancelled";
        var inProgress = currentRequests.Count(isInProgress);
        var completed = currentRequests.Count(isCompleted);
        var stopped = currentRequests.Count(isStopped);
        var other = Math.Max(0, currentRequests.Count - inProgress - completed - stopped);

        var persian = new System.Globalization.PersianCalendar();
        var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-5);
        var monthly = currentRequests
            .Where(x => x.CreatedAt.ToLocalTime().Date >= monthStart && x.CreatedAt.ToLocalTime().Date <= today)
            .GroupBy(x =>
            {
                var date = x.CreatedAt.ToLocalTime();
                return (persian.GetYear(date), persian.GetMonth(date));
            })
            .ToDictionary(g => g.Key, g => g.Count());
        var months = new List<ContractRenewalDashboardBucket>();
        for (var i = 5; i >= 0; i--)
        {
            var month = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
            var key = (persian.GetYear(month), persian.GetMonth(month));
            months.Add(new ContractRenewalDashboardBucket
            {
                Label = $"{key.Item1}/{key.Item2:00}",
                Count = monthly.TryGetValue(key, out var count) ? count : 0
            });
        }

        return View(new ContractRenewalDashboardViewModel
        {
            Today = today,
            ExpiringEmployeeCount = rows.Count,
            TotalRequests = currentRequests.Count,
            InProgressRequests = inProgress,
            CompletedRequests = completed,
            StoppedRequests = stopped,
            NeedsAttentionRequests = other,
            AverageCompletedScore = currentRequests.Where(isCompleted)
                .Where(x => x.FinalEvaluationScore.HasValue)
                .Select(x => (decimal?)x.FinalEvaluationScore!.Value).Average(),
            ExpiringEmployees = rows,
            StatusBuckets =
            [
                new ContractRenewalDashboardBucket { Label = "در جریان", Count = inProgress },
                new ContractRenewalDashboardBucket { Label = "تکمیل‌شده", Count = completed },
                new ContractRenewalDashboardBucket { Label = "متوقف‌شده", Count = stopped },
                new ContractRenewalDashboardBucket { Label = "سایر وضعیت‌ها", Count = other }
            ],
            MonthlyBuckets = months
        });
    }

    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> BasicInformation(int? editId)
    {
        EmployeePerformanceEvaluationQuestion? editing = null;
        if (editId.HasValue)
            editing = await db.EmployeePerformanceEvaluationQuestions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == editId.Value);

        return View(await BuildBasicInfoViewModelAsync(editing));
    }

    [HttpPost, Authorize(Policy = "AdminOnly"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveQuestion(EmployeePerformanceEvaluationQuestion input)
    {
        input.QuestionText = input.QuestionText?.Trim() ?? "";
        input.ScoringGuidance = input.ScoringGuidance?.Trim() ?? "";

        EmployeePerformanceEvaluationQuestion? existing = null;
        if (input.Id > 0)
        {
            existing = await db.EmployeePerformanceEvaluationQuestions
                .FirstOrDefaultAsync(x => x.Id == input.Id);
            if (existing is null) return NotFound();
        }

        if (string.IsNullOrWhiteSpace(input.QuestionText))
            ModelState.AddModelError(nameof(input.QuestionText), "متن پرسش الزامی است.");
        if (string.IsNullOrWhiteSpace(input.ScoringGuidance))
            ModelState.AddModelError(nameof(input.ScoringGuidance), "راهنمای امتیازدهی الزامی است.");
        if (input.MaxScore <= 0 || input.MaxScore > 100)
            ModelState.AddModelError(nameof(input.MaxScore), "سقف امتیاز باید بیشتر از صفر و حداکثر ۱۰۰ باشد.");
        if (input.MaxScore != decimal.Round(input.MaxScore, 2))
            ModelState.AddModelError(nameof(input.MaxScore), "سقف امتیاز حداکثر می‌تواند دو رقم اعشار داشته باشد.");

        var activeTotalWithoutCurrent = await db.EmployeePerformanceEvaluationQuestions
            .Where(x => x.IsActive && x.Id != input.Id)
            .SumAsync(x => (decimal?)x.MaxScore) ?? 0m;

        if (input.IsActive && activeTotalWithoutCurrent + input.MaxScore > 100m)
            ModelState.AddModelError(nameof(input.MaxScore),
                $"جمع سقف امتیاز پرسش‌های فعال پس از این تغییر {activeTotalWithoutCurrent + input.MaxScore:0.##} می‌شود و نباید از ۱۰۰ بیشتر باشد.");

        if (!ModelState.IsValid)
            return View("BasicInformation", await BuildBasicInfoViewModelAsync(input));

        if (existing is null)
        {
            input.CreatedAt = DateTime.UtcNow;
            input.UpdatedAt = DateTime.UtcNow;
            db.EmployeePerformanceEvaluationQuestions.Add(input);
        }
        else
        {
            existing.QuestionText = input.QuestionText;
            existing.MaxScore = input.MaxScore;
            existing.ScoringGuidance = input.ScoringGuidance;
            existing.SortOrder = input.SortOrder;
            existing.IsActive = input.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        var saved = existing ?? input;
        await audit.WriteAsync(
            existing is null ? "ایجاد پرسش ارزشیابی عملکرد" : "ویرایش پرسش ارزشیابی عملکرد",
            nameof(EmployeePerformanceEvaluationQuestion),
            saved.Id.ToString(),
            saved.QuestionText);

        TempData["Success"] = existing is null ? "پرسش ارزشیابی ایجاد شد." : "پرسش ارزشیابی به‌روزرسانی شد.";
        return RedirectToAction(nameof(BasicInformation));
    }

    [HttpPost, Authorize(Policy = "AdminOnly"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleQuestion(int id)
    {
        var question = await db.EmployeePerformanceEvaluationQuestions.FirstOrDefaultAsync(x => x.Id == id);
        if (question is null) return NotFound();

        if (!question.IsActive)
        {
            var activeTotal = await db.EmployeePerformanceEvaluationQuestions
                .Where(x => x.IsActive && x.Id != id)
                .SumAsync(x => (decimal?)x.MaxScore) ?? 0m;

            if (activeTotal + question.MaxScore > 100m)
            {
                TempData["Error"] = $"فعال‌سازی این پرسش باعث می‌شود مجموع سقف امتیاز از ۱۰۰ بیشتر شود (مجموع جدید: {activeTotal + question.MaxScore:0.##}).";
                return RedirectToAction(nameof(BasicInformation));
            }
        }

        question.IsActive = !question.IsActive;
        question.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync(
            question.IsActive ? "فعال‌سازی پرسش ارزشیابی عملکرد" : "غیرفعال‌سازی پرسش ارزشیابی عملکرد",
            nameof(EmployeePerformanceEvaluationQuestion),
            question.Id.ToString(),
            question.QuestionText);

        TempData["Success"] = question.IsActive ? "پرسش فعال شد." : "پرسش غیرفعال شد.";
        return RedirectToAction(nameof(BasicInformation));
    }

    [HttpPost, Authorize(Policy = "AdminOnly"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id)
    {
        var question = await db.EmployeePerformanceEvaluationQuestions.FirstOrDefaultAsync(x => x.Id == id);
        if (question is null) return NotFound();

        if (await db.EmployeePerformanceEvaluationScores.AnyAsync(x => x.QuestionId == id))
        {
            TempData["Error"] = "این پرسش در یک درخواست ارزشیابی استفاده شده است و برای حفظ سوابق قابل حذف نیست؛ آن را غیرفعال کنید.";
            return RedirectToAction(nameof(BasicInformation));
        }

        db.EmployeePerformanceEvaluationQuestions.Remove(question);
        await db.SaveChangesAsync();
        await audit.WriteAsync("حذف پرسش ارزشیابی عملکرد", nameof(EmployeePerformanceEvaluationQuestion), id.ToString(), question.QuestionText);
        TempData["Success"] = "پرسش حذف شد.";
        return RedirectToAction(nameof(BasicInformation));
    }

    [Authorize(Policy = "AdminOnly")]
    public IActionResult WorkflowSettings() =>
        Redirect("/EmployeeContracts/BasicInformation#workflow-settings");

    [HttpPost, Authorize(Policy = "AdminOnly"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWorkflowSettings(int? securityPositionId, int? humanResourcesPositionId)
    {
        var current = await CurrentRevisionAsync();
        if (current is null)
        {
            TempData["Error"] = "ابتدا آخرین نسخه ساختار سازمانی را نهایی کنید.";
            return RedirectToAction(nameof(WorkflowSettings));
        }

        var nodes = await db.OrganizationNodes
            .Where(x => x.OrganizationStructureRevisionId == current.Id && x.IsActive)
            .ToListAsync();
        if (!securityPositionId.HasValue || !nodes.Any(x => x.Id == securityPositionId.Value))
            ModelState.AddModelError(nameof(securityPositionId), "جایگاه فعال مدیر حراست را انتخاب کنید.");
        if (!humanResourcesPositionId.HasValue || !nodes.Any(x => x.Id == humanResourcesPositionId.Value))
            ModelState.AddModelError(nameof(humanResourcesPositionId), "جایگاه فعال مدیر منابع انسانی و خدمات پشتیبانی را انتخاب کنید.");
        if (securityPositionId.HasValue && securityPositionId == humanResourcesPositionId)
            ModelState.AddModelError(nameof(humanResourcesPositionId), "جایگاه مدیر حراست و مدیر منابع انسانی و خدمات پشتیبانی باید متفاوت باشد.");

        var ceoNodes = nodes.Where(x => x.RankType == "مدیرعامل").ToList();
        if (ceoNodes.Count != 1)
            ModelState.AddModelError("ChiefExecutiveEmployee", "در نسخه نهایی ساختار سازمانی باید دقیقاً یک گره فعال با رده مدیرعامل وجود داشته باشد.");

        var ceoEmployees = ceoNodes.Count != 1 ? new List<Employee>() : await db.Employees.AsNoTracking()
            .Where(x => x.Status == "فعال" && x.OrganizationPositionId == ceoNodes[0].Id)
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync();
        if (ceoEmployees.Count != 1)
            ModelState.AddModelError("ChiefExecutiveEmployee", "باید دقیقاً یک کارمند فعال به جایگاه مدیرعامل نسخه نهایی متصل باشد. این جایگاه را در فرم کارکنان تخصیص دهید.");

        if (!ModelState.IsValid)
        {
            var basicInfo = await BuildBasicInfoViewModelAsync();
            basicInfo.WorkflowSettings = await BuildWorkflowSettingsViewModelAsync();
            basicInfo.WorkflowSettings.Settings.SecurityPositionId = securityPositionId;
            basicInfo.WorkflowSettings.Settings.HumanResourcesPositionId = humanResourcesPositionId;
            basicInfo.WorkflowSettings.ErrorMessage = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            ViewBag.ActiveTab = "workflow-settings";
            return View("BasicInformation", basicInfo);
        }

        var settings = await db.ContractRenewalWorkflowSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (settings is null)
        {
            settings = new ContractRenewalWorkflowSettings { Id = 1 };
            db.ContractRenewalWorkflowSettings.Add(settings);
        }
        settings.SecurityPositionId = securityPositionId;
        settings.HumanResourcesPositionId = humanResourcesPositionId;
        settings.CeoEmployeeId = ceoEmployees[0].Id;
        settings.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception)
        {
            db.ChangeTracker.Clear();
            var basicInfo = await BuildBasicInfoViewModelAsync();
            basicInfo.WorkflowSettings = await BuildWorkflowSettingsViewModelAsync(securityPositionId, humanResourcesPositionId);
            basicInfo.WorkflowSettings.ErrorMessage = "ذخیره تنظیمات مراحل ثابت در پایگاه داده انجام نشد. ارتباط پایگاه داده و ساختار فعال را بررسی کنید؛ مقادیر انتخاب‌شده حفظ شده‌اند.";
            ViewBag.ActiveTab = "workflow-settings";
            return View("BasicInformation", basicInfo);
        }
        await audit.WriteAsync("تنظیم مراحل ثابت تمدید قرارداد", nameof(ContractRenewalWorkflowSettings), "1");
        TempData["Success"] = "تنظیم مراحل ثابت گردش تمدید قرارداد ذخیره شد.";
        return Redirect("/EmployeeContracts/BasicInformation#workflow-settings");
    }

    [Authorize(Policy = "ContractRenewal.Manage")]
    public async Task<IActionResult> Renewal(int? employeeId = null)
    {
        if (employeeId.HasValue)
        {
            var employee = await db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == employeeId.Value && x.Status == "فعال");
            if (employee is not null && await HasExistingRenewalForPeriodAsync(employee.Id, employee.ContractEndDate))
            {
                TempData["Error"] = "برای تاریخ پایان قرارداد فعلی این کارمند، درخواست تمدید ثبت‌شده، در جریان یا نهایی وجود دارد؛ درخواست تکراری ایجاد نمی‌شود.";
                return RedirectToAction(nameof(Dashboard));
            }
        }
        var model = await BuildIntakeViewModelAsync(new ContractRenewalIntakeViewModel { EmployeeId = employeeId ?? 0 }, true);
        return View(model);
    }

    [HttpPost, Authorize(Policy = "ContractRenewal.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(ContractRenewalIntakeViewModel input)
    {
        ContractRenewalRequest? editingRequest = null;
        if (input.RequestId > 0)
        {
            editingRequest = await db.ContractRenewalRequests
                .Include(x => x.WorkflowInstance)
                .FirstOrDefaultAsync(x => x.Id == input.RequestId && x.Status != "حذف شده");
            if (editingRequest?.WorkflowInstance is null ||
                editingRequest.WorkflowInstance.Status != "InProgress" ||
                await HasEvaluationActivityAsync(editingRequest.WorkflowInstance.Id))
            {
                TempData["Error"] = "این درخواست دیگر برای ویرایش پیش از ارزیابی واجد شرایط نیست.";
                return RedirectToAction(nameof(Requests));
            }

            // Edit mode is derived from the persisted request, not trusted from hidden form values.
            input.IsEditMode = true;
            input.EmployeeId = editingRequest.EmployeeId;
            input.PreviousPeriodScore1 = editingRequest.PreviousPeriodScore1;
            input.PreviousPeriodScore2 = editingRequest.PreviousPeriodScore2;
            input.PreviousPeriodScore1FromHistory = editingRequest.PreviousPeriodScore1FromHistory;
            input.PreviousPeriodScore2FromHistory = editingRequest.PreviousPeriodScore2FromHistory;
        }
        else
        {
            input.IsEditMode = false;
            input.RequestId = 0;
        }

        var model = await BuildIntakeViewModelAsync(input, true);
        if (editingRequest is not null)
        {
            model.IsEditMode = true;
            model.RequestId = editingRequest.Id;
            model.PreviousPeriodScore1 = editingRequest.PreviousPeriodScore1;
            model.PreviousPeriodScore2 = editingRequest.PreviousPeriodScore2;
            model.PreviousPeriodScore1FromHistory = editingRequest.PreviousPeriodScore1FromHistory;
            model.PreviousPeriodScore2FromHistory = editingRequest.PreviousPeriodScore2FromHistory;
        }
        ValidateIntake(model);
        model.ShowPreview = ModelState.IsValid;
        return View("Renewal", model);
    }

    [HttpPost, Authorize(Policy = "ContractRenewal.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(ContractRenewalIntakeViewModel input)
    {
        // This endpoint creates new requests only; a posted edit flag must never bypass prerequisites.
        input.IsEditMode = false;
        input.RequestId = 0;
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var requesterEmployeeId))
        {
            TempData["Error"] = "برای ثبت درخواست، ورود با حساب کاربری متصل به پرونده پرسنلی لازم است.";
            return RedirectToAction(nameof(Renewal));
        }

        var model = await BuildIntakeViewModelAsync(input, true);
        ValidateIntake(model);
        if (!ModelState.IsValid)
        {
            model.ShowPreview = true;
            return View("Renewal", model);
        }

        var employee = model.SelectedEmployee!;
        if (await HasExistingRenewalForPeriodAsync(employee.Id, employee.ContractEndDate))
        {
            model.ErrorMessage = "برای تاریخ پایان قرارداد فعلی این کارمند، درخواست تمدید ثبت‌شده، در جریان یا نهایی وجود دارد. برای جلوگیری از درخواست تکراری، ابتدا فهرست درخواست‌ها را بررسی کنید.";
            model.ShowPreview = true;
            return View("Renewal", model);
        }

        var request = new ContractRenewalRequest
        {
            RequestCode = "TEMP-" + Guid.NewGuid().ToString("N")[..20],
            EmployeeId = employee.Id,
            CreatedByEmployeeId = requesterEmployeeId,
            Status = "در حال شروع",
            EmployeeNameSnapshot = $"{employee.FirstName} {employee.LastName}".Trim(),
            PersonnelNumberSnapshot = employee.PersonnelNumber,
            OrganizationUnitSnapshot = employee.OrganizationUnit?.Title ?? "",
            PositionTitleSnapshot = employee.PositionTitle ?? employee.OrganizationPosition?.Title ?? "",
            PreviousContractEndDate = employee.ContractEndDate,
            PreviousExtensionMonths1 = model.PreviousExtensionMonths1,
            PreviousExtensionMonths2 = model.PreviousExtensionMonths2,
            PreviousExtensionMonths3 = model.PreviousExtensionMonths3,
            HasDisciplinaryWarnings = model.HasDisciplinaryWarnings,
            DisciplinaryWarningCount = model.HasDisciplinaryWarnings ? model.DisciplinaryWarningCount : 0,
            HasDisciplinaryCommitteeResolution = model.HasDisciplinaryCommitteeResolution,
            DisciplinaryCommitteeResolutionCount = model.HasDisciplinaryCommitteeResolution ? model.DisciplinaryCommitteeResolutionCount : 0,
            HasUnpaidLeave = model.HasUnpaidLeave,
            UnpaidLeaveDays = model.HasUnpaidLeave ? model.UnpaidLeaveDays : 0,
            HasSickLeave = model.HasSickLeave,
            SickLeaveDays = model.HasSickLeave ? model.SickLeaveDays : 0,
            HasAccidentLeave = model.HasAccidentLeave,
            AccidentLeaveDays = model.HasAccidentLeave ? model.AccidentLeaveDays : 0,
            HasCommendations = model.HasCommendations,
            CommendationCount = model.HasCommendations ? model.CommendationCount : 0,
            PreviousPeriodScore1 = model.PreviousPeriodScore1,
            PreviousPeriodScore2 = model.PreviousPeriodScore2,
            PreviousPeriodScore1FromHistory = model.PreviousPeriodScore1FromHistory,
            PreviousPeriodScore2FromHistory = model.PreviousPeriodScore2FromHistory,
            CreatedAt = DateTime.UtcNow
        };

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            db.ContractRenewalRequests.Add(request);
            await db.SaveChangesAsync();
            request.RequestCode = $"CR-{DateTime.UtcNow:yyMMdd}-{request.Id:000000}";
            await db.SaveChangesAsync();

            var instance = await renewalWorkflow.StartAsync(request, requesterEmployeeId);
            request.WorkflowInstanceId = instance.Id;
            request.Status = "در حال ارزیابی سازمانی";
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            await audit.WriteAsync("ثبت و شروع ارزشیابی تمدید قرارداد", nameof(ContractRenewalRequest),
                request.Id.ToString(), request.RequestCode);
            TempData["Success"] = $"درخواست {request.RequestCode} ثبت شد و برای ارزشیابی به زنجیره جایگاه‌های سازمانی ارجاع شد.";
            return RedirectToAction(nameof(Renewal));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            model.ErrorMessage = ex.Message;
            model.ShowPreview = true;
            return View("Renewal", model);
        }
    }

    [Authorize(Policy = "ContractRenewal.Manage")]
    public async Task<IActionResult> Requests(bool includeDeleted = false)
    {
        var requestQuery = db.ContractRenewalRequests.AsNoTracking()
            .Include(x => x.WorkflowInstance).ThenInclude(x => x!.CurrentStep)
            .AsQueryable();

        // Soft-deleted requests are hidden from the ordinary operational list,
        // but remain discoverable from this list to preserve access to their audit trail.
        if (!includeDeleted)
            requestQuery = requestQuery.Where(x => x.Status != "حذف شده");

        ViewBag.IncludeDeleted = includeDeleted;
        var requests = await requestQuery
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync();

        var instanceIds = requests.Where(x => x.WorkflowInstanceId.HasValue)
            .Select(x => x.WorkflowInstanceId!.Value).Distinct().ToList();
        var scoredInstanceIds = await db.EmployeePerformanceEvaluationScores.AsNoTracking()
            .Where(x => instanceIds.Contains(x.WorkflowInstanceId) && x.Score.HasValue)
            .Select(x => x.WorkflowInstanceId).Distinct().ToListAsync();
        var completedEvaluationInstanceIds = await db.WorkflowTasks.AsNoTracking()
            .Where(x => instanceIds.Contains(x.WorkflowInstanceId) &&
                x.Status == "Completed" && x.WorkflowStep.Code.StartsWith("Evaluation_H"))
            .Select(x => x.WorkflowInstanceId).Distinct().ToListAsync();
        var evaluated = scoredInstanceIds.Concat(completedEvaluationInstanceIds).ToHashSet();

        var rows = requests.Select(request =>
        {
            var instance = request.WorkflowInstance;
            var hasEvaluation = instance is not null && evaluated.Contains(instance.Id);
            var active = instance is not null && instance.Status == "InProgress";
            return new ContractRenewalRequestListItem
            {
                Request = request,
                LastStatus = request.Status,
                CurrentStageTitle = instance?.CurrentStep?.Title ?? (instance?.Status switch
                {
                    "Cancelled" => "گردش متوقف شده",
                    "Approved" => "گردش تأیید و تکمیل شده",
                    "Rejected" => "گردش رد شده",
                    "Returned" => "گردش برگشت خورده",
                    _ => "مرحله جاری ثبت نشده"
                }),
                HasAnyEvaluation = hasEvaluation,
                CanEditOrDelete = active && !hasEvaluation && request.Status != "متوقف شده",
                CanStop = active
            };
        }).ToList();

        return View(rows);
    }

    [Authorize(Policy = "ContractRenewal.Manage")]
    public async Task<IActionResult> History(int id)
    {
        var request = await db.ContractRenewalRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (request is null) return NotFound();

        ViewBag.Request = request;
        var revisions = await db.ContractRenewalRequestRevisions.AsNoTracking()
            .Where(x => x.ContractRenewalRequestId == id)
            .OrderByDescending(x => x.RevisionNumber)
            .ToListAsync();
        var workflowHistory = request.WorkflowInstanceId.HasValue
            ? await db.WorkflowHistory.AsNoTracking()
                .Include(x => x.WorkflowStep)
                .Include(x => x.ActorEmployee)
                .Include(x => x.ActorPosition)
                .Where(x => x.WorkflowInstanceId == request.WorkflowInstanceId.Value)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync()
            : new List<WorkflowHistory>();
        ViewBag.WorkflowHistory = workflowHistory;

        var evaluationStages = new List<ContractRenewalHistoryScoreStage>();
        if (request.WorkflowInstanceId.HasValue)
        {
            var instanceId = request.WorkflowInstanceId.Value;
            var scoreRows = await db.EmployeePerformanceEvaluationScores.AsNoTracking()
                .Include(x => x.WorkflowStep).ThenInclude(x => x.OrganizationNode)
                .Where(x => x.WorkflowInstanceId == instanceId &&
                    x.WorkflowStep.Code.StartsWith("Evaluation_H"))
                .OrderBy(x => x.WorkflowStep.SortOrder).ThenBy(x => x.Id)
                .ToListAsync();
            var stageTasks = await db.WorkflowTasks.AsNoTracking()
                .Where(x => x.WorkflowInstanceId == instanceId)
                .OrderByDescending(x => x.CompletedAt).ThenByDescending(x => x.Id)
                .ToListAsync();

            evaluationStages = scoreRows
                .GroupBy(x => x.WorkflowStepId)
                .Select(group =>
                {
                    var rows = group.OrderBy(x => x.Id).ToList();
                    var step = rows[0].WorkflowStep;
                    var complete = rows.All(x => x.Score.HasValue);
                    return new ContractRenewalHistoryScoreStage
                    {
                        StageTitle = step.Title,
                        PositionTitle = step.OrganizationNode?.Title ?? step.Title,
                        SortOrder = step.SortOrder,
                        IsComplete = complete,
                        ScoreTotal = complete ? rows.Sum(x => x.Score!.Value) : null,
                        Comment = stageTasks.FirstOrDefault(x =>
                            x.WorkflowStepId == step.Id && x.Status == "Completed")?.Comment,
                        Scores = rows
                    };
                })
                .OrderBy(x => x.SortOrder)
                .ToList();
        }

        ViewBag.EvaluationStages = evaluationStages;
        return View(revisions);
    }

    [HttpGet, Authorize(Policy = "ContractRenewal.Manage")]
    public async Task<IActionResult> Edit(int id)
    {
        var request = await db.ContractRenewalRequests
            .Include(x => x.WorkflowInstance)
            .FirstOrDefaultAsync(x => x.Id == id && x.Status != "حذف شده");
        if (request is null) return NotFound();
        if (request.WorkflowInstance is null || request.WorkflowInstance.Status != "InProgress" ||
            await HasEvaluationActivityAsync(request.WorkflowInstance.Id))
        {
            TempData["Error"] = "ویرایش فقط تا پیش از ثبت اولین ارزیابی سازمانی امکان‌پذیر است؛ سوابق ثبت‌شده حفظ می‌شوند.";
            return RedirectToAction(nameof(Requests));
        }

        var input = new ContractRenewalIntakeViewModel
        {
            RequestId = request.Id,
            IsEditMode = true,
            EmployeeId = request.EmployeeId,
            PreviousExtensionMonths1 = request.PreviousExtensionMonths1,
            PreviousExtensionMonths2 = request.PreviousExtensionMonths2,
            PreviousExtensionMonths3 = request.PreviousExtensionMonths3,
            HasDisciplinaryWarnings = request.HasDisciplinaryWarnings,
            DisciplinaryWarningCount = request.DisciplinaryWarningCount,
            HasDisciplinaryCommitteeResolution = request.HasDisciplinaryCommitteeResolution,
            DisciplinaryCommitteeResolutionCount = request.DisciplinaryCommitteeResolutionCount,
            HasUnpaidLeave = request.HasUnpaidLeave,
            UnpaidLeaveDays = request.UnpaidLeaveDays,
            HasSickLeave = request.HasSickLeave,
            SickLeaveDays = request.SickLeaveDays,
            HasAccidentLeave = request.HasAccidentLeave,
            AccidentLeaveDays = request.AccidentLeaveDays,
            HasCommendations = request.HasCommendations,
            CommendationCount = request.CommendationCount,
            PreviousPeriodScore1 = request.PreviousPeriodScore1,
            PreviousPeriodScore2 = request.PreviousPeriodScore2,
            PreviousPeriodScore1FromHistory = request.PreviousPeriodScore1FromHistory,
            PreviousPeriodScore2FromHistory = request.PreviousPeriodScore2FromHistory
        };
        var model = await BuildIntakeViewModelAsync(input, false);
        model.PreviousPeriodScore1 = request.PreviousPeriodScore1;
        model.PreviousPeriodScore2 = request.PreviousPeriodScore2;
        model.PreviousPeriodScore1FromHistory = request.PreviousPeriodScore1FromHistory;
        model.PreviousPeriodScore2FromHistory = request.PreviousPeriodScore2FromHistory;
        return View("Renewal", model);
    }

    [HttpPost, Authorize(Policy = "ContractRenewal.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRequest(ContractRenewalIntakeViewModel input)
    {
        var request = await db.ContractRenewalRequests
            .Include(x => x.WorkflowInstance)
            .FirstOrDefaultAsync(x => x.Id == input.RequestId && x.Status != "حذف شده");
        if (request is null) return NotFound();
        if (request.WorkflowInstance is null || request.WorkflowInstance.Status != "InProgress" ||
            await HasEvaluationActivityAsync(request.WorkflowInstance.Id))
        {
            TempData["Error"] = "این درخواست پس از شروع ارزیابی قابل ویرایش نیست.";
            return RedirectToAction(nameof(Requests));
        }
        if (input.EmployeeId != request.EmployeeId)
            ModelState.AddModelError(nameof(input.EmployeeId), "کارمند موضوع درخواست پس از ایجاد قابل تغییر نیست؛ برای کارمند دیگر درخواست جدید ثبت کنید.");
        input.EmployeeId = request.EmployeeId;
        input.IsEditMode = true;

        var model = await BuildIntakeViewModelAsync(input, false);
        model.IsEditMode = true;
        model.RequestId = request.Id;
        model.ShowPreview = true;
        model.PreviousPeriodScore1 = request.PreviousPeriodScore1;
        model.PreviousPeriodScore2 = request.PreviousPeriodScore2;
        model.PreviousPeriodScore1FromHistory = request.PreviousPeriodScore1FromHistory;
        model.PreviousPeriodScore2FromHistory = request.PreviousPeriodScore2FromHistory;
        ValidateIntake(model);
        if (!ModelState.IsValid)
            return View("Renewal", model);

        await PreserveRequestRevisionAsync(request, "BeforeEdit");
        ApplyIntakeToRequest(request, model, model.SelectedEmployee!);
        db.WorkflowHistory.Add(new WorkflowHistory
        {
            WorkflowInstanceId = request.WorkflowInstance.Id,
            WorkflowStepId = request.WorkflowInstance.CurrentStepId,
            FromStatus = request.WorkflowInstance.Status,
            ToStatus = request.WorkflowInstance.Status,
            Action = "EditIntake",
            ActorEmployeeId = GetCurrentEmployeeId(),
            Comment = "اطلاعات اولیه درخواست پیش از نخستین ارزیابی اصلاح شد."
        });
        await db.SaveChangesAsync();
        await audit.WriteAsync("ویرایش اطلاعات اولیه درخواست تمدید قرارداد", nameof(ContractRenewalRequest),
            request.Id.ToString(), request.RequestCode);
        TempData["Success"] = $"اطلاعات اولیه درخواست {request.RequestCode} اصلاح شد؛ مسیر گردش و سوابق موجود حفظ شدند.";
        return RedirectToAction(nameof(Requests));
    }

    [HttpPost, Authorize(Policy = "ContractRenewal.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRequest(int id)
    {
        var request = await db.ContractRenewalRequests
            .Include(x => x.WorkflowInstance).ThenInclude(x => x!.Tasks)
            .FirstOrDefaultAsync(x => x.Id == id && x.Status != "حذف شده");
        if (request is null) return NotFound();
        if (request.WorkflowInstance is null || request.WorkflowInstance.Status != "InProgress" ||
            await HasEvaluationActivityAsync(request.WorkflowInstance.Id))
        {
            TempData["Error"] = "حذف فقط تا پیش از ثبت اولین ارزیابی مجاز است. سوابق قبلی به‌صورت نرم‌حذف حفظ می‌شوند.";
            return RedirectToAction(nameof(Requests));
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var instance = request.WorkflowInstance;
        var previousStatus = instance.Status;
        await PreserveRequestRevisionAsync(request, "BeforeDelete");
        request.Status = "حذف شده";
        request.CompletedAt = DateTime.UtcNow;
        instance.Status = "Cancelled";
        instance.CurrentStepId = null;
        instance.CompletedAt = DateTime.UtcNow;
        foreach (var task in instance.Tasks.Where(x => x.Status == "Pending"))
        {
            task.Status = "Cancelled";
            task.Action = "Delete";
            task.CompletedAt = DateTime.UtcNow;
        }
        db.WorkflowHistory.Add(new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            WorkflowStepId = instance.Tasks.FirstOrDefault(x => x.Status == "Cancelled")?.WorkflowStepId,
            FromStatus = previousStatus,
            ToStatus = "Cancelled",
            Action = "Delete",
            ActorEmployeeId = GetCurrentEmployeeId(),
            Comment = "درخواست پیش از اولین ارزیابی به‌صورت نرم‌حذف شد؛ رکوردها و تاریخچه حفظ شدند."
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await audit.WriteAsync("نرم‌حذف درخواست تمدید قرارداد", nameof(ContractRenewalRequest),
            request.Id.ToString(), request.RequestCode);
        TempData["Success"] = $"درخواست {request.RequestCode} از فهرست فعال حذف شد؛ تاریخچه و رکوردهای وابسته حفظ شدند.";
        return RedirectToAction(nameof(Requests));
    }

    [HttpPost, Authorize(Policy = "ContractRenewal.Manage"), ValidateAntiForgeryToken]
    public async Task<IActionResult> StopRequest(int id, string? reason)
    {
        if (reason is { Length: > 2000 })
        {
            TempData["Error"] = "توضیح توقف حداکثر می‌تواند ۲۰۰۰ نویسه باشد.";
            return RedirectToAction(nameof(Requests));
        }

        var request = await db.ContractRenewalRequests
            .Include(x => x.WorkflowInstance).ThenInclude(x => x!.Tasks)
            .FirstOrDefaultAsync(x => x.Id == id && x.Status != "حذف شده");
        if (request is null) return NotFound();
        if (request.WorkflowInstance is null || request.WorkflowInstance.Status != "InProgress")
        {
            TempData["Error"] = "فقط درخواست دارای گردش فعال را می‌توان متوقف کرد؛ درخواست‌های خاتمه‌یافته سابقه خود را حفظ می‌کنند.";
            return RedirectToAction(nameof(Requests));
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var instance = request.WorkflowInstance;
        var previousStatus = instance.Status;
        await PreserveRequestRevisionAsync(request, "BeforeStop");
        request.Status = "متوقف شده";
        request.CompletedAt = DateTime.UtcNow;
        instance.Status = "Cancelled";
        instance.CurrentStepId = null;
        instance.CompletedAt = DateTime.UtcNow;
        foreach (var task in instance.Tasks.Where(x => x.Status == "Pending"))
        {
            task.Status = "Cancelled";
            task.Action = "Stop";
            task.Comment = reason?.Trim();
            task.CompletedAt = DateTime.UtcNow;
        }
        db.WorkflowHistory.Add(new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            WorkflowStepId = instance.Tasks.FirstOrDefault(x => x.Status == "Cancelled")?.WorkflowStepId,
            FromStatus = previousStatus,
            ToStatus = "Cancelled",
            Action = "Stop",
            ActorEmployeeId = GetCurrentEmployeeId(),
            Comment = string.IsNullOrWhiteSpace(reason) ? "گردش درخواست متوقف شد." : reason.Trim()
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await audit.WriteAsync("توقف گردش تمدید قرارداد", nameof(ContractRenewalRequest),
            request.Id.ToString(), request.RequestCode);
        TempData["Success"] = $"گردش درخواست {request.RequestCode} متوقف شد؛ امتیازها و تاریخچه حفظ شدند.";
        return RedirectToAction(nameof(Requests));
    }

    private async Task PreserveRequestRevisionAsync(ContractRenewalRequest request, string action)
    {
        var nextRevision = (await db.ContractRenewalRequestRevisions
            .Where(x => x.ContractRenewalRequestId == request.Id)
            .Select(x => (int?)x.RevisionNumber)
            .MaxAsync() ?? 0) + 1;

        var snapshot = new
        {
            request.Id,
            request.RequestCode,
            request.EmployeeId,
            request.CreatedByEmployeeId,
            request.WorkflowInstanceId,
            WorkflowStatus = request.WorkflowInstance?.Status,
            WorkflowCurrentStepId = request.WorkflowInstance?.CurrentStepId,
            request.Status,
            request.EmployeeNameSnapshot,
            request.PersonnelNumberSnapshot,
            request.OrganizationUnitSnapshot,
            request.PositionTitleSnapshot,
            request.PreviousContractEndDate,
            request.PreviousExtensionMonths1,
            request.PreviousExtensionMonths2,
            request.PreviousExtensionMonths3,
            request.HasDisciplinaryWarnings,
            request.DisciplinaryWarningCount,
            request.HasDisciplinaryCommitteeResolution,
            request.DisciplinaryCommitteeResolutionCount,
            request.HasUnpaidLeave,
            request.UnpaidLeaveDays,
            request.HasSickLeave,
            request.SickLeaveDays,
            request.HasAccidentLeave,
            request.AccidentLeaveDays,
            request.HasCommendations,
            request.CommendationCount,
            request.PreviousPeriodScore1,
            request.PreviousPeriodScore2,
            request.PreviousPeriodScore1FromHistory,
            request.PreviousPeriodScore2FromHistory,
            request.FinalEvaluationScore,
            request.SecurityDecision,
            request.SecurityComment,
            request.HrProposedContractMonths,
            request.HrComment,
            request.FinalContractMonths,
            request.CeoComment,
            request.CreatedAt,
            request.CompletedAt
        };

        db.ContractRenewalRequestRevisions.Add(new ContractRenewalRequestRevision
        {
            ContractRenewalRequestId = request.Id,
            RevisionNumber = nextRevision,
            Action = action,
            SnapshotJson = System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            }),
            ChangedByEmployeeId = GetCurrentEmployeeId(),
            CreatedAt = DateTime.UtcNow
        });
    }

    private int? GetCurrentEmployeeId() =>
        int.TryParse(User.FindFirst("EmployeeId")?.Value, out var id) ? id : null;

    private async Task<bool> HasExistingRenewalForPeriodAsync(int employeeId, DateTime? contractEndDate)
    {
        if (!contractEndDate.HasValue) return false;
        var targetDate = contractEndDate.Value.Date;
        return await db.ContractRenewalRequests.AsNoTracking().AnyAsync(x =>
            x.EmployeeId == employeeId &&
            x.PreviousContractEndDate.HasValue &&
            x.PreviousContractEndDate.Value == targetDate &&
            x.Status != "حذف شده" &&
            x.Status != "متوقف شده" &&
            (x.WorkflowInstance == null || x.WorkflowInstance.Status != "Cancelled"));
    }

    private async Task<bool> HasEvaluationActivityAsync(int workflowInstanceId)
    {
        if (await db.EmployeePerformanceEvaluationScores.AnyAsync(x =>
                x.WorkflowInstanceId == workflowInstanceId && x.Score.HasValue))
            return true;
        return await db.WorkflowTasks.AnyAsync(x =>
            x.WorkflowInstanceId == workflowInstanceId &&
            x.Status == "Completed" &&
            x.WorkflowStep.Code.StartsWith("Evaluation_H"));
    }

    private static void ApplyIntakeToRequest(
        ContractRenewalRequest request, ContractRenewalIntakeViewModel model, Employee employee)
    {
        request.EmployeeNameSnapshot = $"{employee.FirstName} {employee.LastName}".Trim();
        request.PersonnelNumberSnapshot = employee.PersonnelNumber;
        request.OrganizationUnitSnapshot = employee.OrganizationUnit?.Title ?? "";
        request.PositionTitleSnapshot = employee.OrganizationPosition?.Title ?? employee.PositionTitle ?? "";
        request.PreviousContractEndDate = employee.ContractEndDate;
        request.PreviousExtensionMonths1 = model.PreviousExtensionMonths1;
        request.PreviousExtensionMonths2 = model.PreviousExtensionMonths2;
        request.PreviousExtensionMonths3 = model.PreviousExtensionMonths3;
        request.HasDisciplinaryWarnings = model.HasDisciplinaryWarnings;
        request.DisciplinaryWarningCount = model.HasDisciplinaryWarnings ? model.DisciplinaryWarningCount : 0;
        request.HasDisciplinaryCommitteeResolution = model.HasDisciplinaryCommitteeResolution;
        request.DisciplinaryCommitteeResolutionCount = model.HasDisciplinaryCommitteeResolution ? model.DisciplinaryCommitteeResolutionCount : 0;
        request.HasUnpaidLeave = model.HasUnpaidLeave;
        request.UnpaidLeaveDays = model.HasUnpaidLeave ? model.UnpaidLeaveDays : 0;
        request.HasSickLeave = model.HasSickLeave;
        request.SickLeaveDays = model.HasSickLeave ? model.SickLeaveDays : 0;
        request.HasAccidentLeave = model.HasAccidentLeave;
        request.AccidentLeaveDays = model.HasAccidentLeave ? model.AccidentLeaveDays : 0;
        request.HasCommendations = model.HasCommendations;
        request.CommendationCount = model.HasCommendations ? model.CommendationCount : 0;
        request.PreviousPeriodScore1 = model.PreviousPeriodScore1;
        request.PreviousPeriodScore2 = model.PreviousPeriodScore2;
        request.PreviousPeriodScore1FromHistory = model.PreviousPeriodScore1FromHistory;
        request.PreviousPeriodScore2FromHistory = model.PreviousPeriodScore2FromHistory;
    }

    private async Task<ContractRenewalIntakeViewModel> BuildIntakeViewModelAsync(
        ContractRenewalIntakeViewModel input, bool includeRecentRequests)
    {
        var employees = await db.Employees.AsNoTracking()
            .Include(x => x.OrganizationUnit)
            .Include(x => x.OrganizationDepartment)
            .Include(x => x.OrganizationSection)
            .Include(x => x.OrganizationPosition)
            .Where(x => x.Status == "فعال")
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .ToListAsync();

        input.Employees = employees;
        input.SelectedEmployee = employees.FirstOrDefault(x => x.Id == input.EmployeeId);
        input.ActiveQuestions = await db.EmployeePerformanceEvaluationQuestions.AsNoTracking()
            .Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        input.ActiveQuestionsTotal = input.ActiveQuestions.Sum(x => x.MaxScore);
        input.QuestionsReady = input.ActiveQuestions.Count > 0 && input.ActiveQuestionsTotal == 100m;

        var current = await CurrentRevisionAsync();
        var config = await db.ContractRenewalWorkflowSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);
        var currentCeoNodeIds = current is null ? new List<int>() : await db.OrganizationNodes.AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == current.Id && x.IsActive && x.RankType == "مدیرعامل")
            .Select(x => x.Id).ToListAsync();
        var currentCeoCount = currentCeoNodeIds.Count == 0 ? 0 : await db.Employees.CountAsync(x =>
            x.Status == "فعال" && x.OrganizationPositionId.HasValue &&
            currentCeoNodeIds.Contains(x.OrganizationPositionId.Value));
        var configuredSecurity = config?.SecurityPositionId is int securityId
            ? await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == securityId)
            : null;
        var configuredHr = config?.HumanResourcesPositionId is int hrId
            ? await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == hrId)
            : null;
        input.WorkflowSettingsReady = current is not null &&
            configuredSecurity is not null && configuredHr is not null &&
            currentCeoNodeIds.Count == 1 && currentCeoCount == 1 &&
            await db.OrganizationNodes.AnyAsync(x => x.OrganizationStructureRevisionId == current.Id &&
                x.Code == configuredSecurity.Code && x.IsActive) &&
            await db.OrganizationNodes.AnyAsync(x => x.OrganizationStructureRevisionId == current.Id &&
                x.Code == configuredHr.Code && x.IsActive);

        if (input.SelectedEmployee is not null)
        {
            var priorScores = await db.ContractRenewalRequests.AsNoTracking()
                .Where(x => x.EmployeeId == input.EmployeeId && x.Status == "تکمیل شده" && x.FinalEvaluationScore.HasValue)
                .OrderByDescending(x => x.CompletedAt).ThenByDescending(x => x.Id)
                .Select(x => x.FinalEvaluationScore!.Value)
                .Take(2)
                .ToListAsync();

            input.PreviousScoresFound = priorScores;
            input.PreviousPeriodScore1FromHistory = priorScores.Count >= 1;
            input.PreviousPeriodScore2FromHistory = priorScores.Count >= 2;
            if (priorScores.Count >= 1) input.PreviousPeriodScore1 = priorScores[0];
            if (priorScores.Count >= 2) input.PreviousPeriodScore2 = priorScores[1];
        }
        else
        {
            input.PreviousScoresFound = [];
            input.PreviousPeriodScore1FromHistory = false;
            input.PreviousPeriodScore2FromHistory = false;
        }

        if (includeRecentRequests)
        {
            var recent = db.ContractRenewalRequests.AsNoTracking()
                .Include(x => x.Employee)
                .Where(x => x.Status != "حذف شده")
                .OrderByDescending(x => x.CreatedAt);
            if (!User.HasClaim("IsAdmin", "1"))
            {
                if (int.TryParse(User.FindFirst("EmployeeId")?.Value, out var currentEmployeeId))
                    input.RecentRequests = await recent.Where(x => x.CreatedByEmployeeId == currentEmployeeId).Take(20).ToListAsync();
                else
                    input.RecentRequests = [];
            }
            else
            {
                input.RecentRequests = await recent.Take(20).ToListAsync();
            }
        }

        return input;
    }

    private void ValidateIntake(ContractRenewalIntakeViewModel model)
    {
        if (model.SelectedEmployee is null)
            ModelState.AddModelError(nameof(model.EmployeeId), "یک کارمند فعال انتخاب کنید.");
        else if (!model.IsEditMode && (model.SelectedEmployee.EmploymentType == "غیرقراردادی" || model.SelectedEmployee.EmploymentType == "غیر قراردادی"))
            ModelState.AddModelError(nameof(model.EmployeeId), "برای کارمند غیرقراردادی درخواست تمدید قرارداد ثبت نمی‌شود.");

        if (model.PreviousExtensionMonths1 is < 0 or > 120 ||
            model.PreviousExtensionMonths2 is < 0 or > 120 ||
            model.PreviousExtensionMonths3 is < 0 or > 120)
            ModelState.AddModelError("", "مدت تمدیدهای قبلی باید عددی بین صفر تا ۱۲۰ ماه باشد.");

        ValidateCount(model.HasDisciplinaryWarnings, model.DisciplinaryWarningCount, nameof(model.DisciplinaryWarningCount), "تعداد اخطارهای انضباطی");
        ValidateCount(model.HasDisciplinaryCommitteeResolution, model.DisciplinaryCommitteeResolutionCount, nameof(model.DisciplinaryCommitteeResolutionCount), "تعداد مصوبه‌های کمیته انضباطی");
        ValidateCount(model.HasUnpaidLeave, model.UnpaidLeaveDays, nameof(model.UnpaidLeaveDays), "روزهای مرخصی بدون حقوق");
        ValidateCount(model.HasSickLeave, model.SickLeaveDays, nameof(model.SickLeaveDays), "روزهای مرخصی بیماری");
        ValidateCount(model.HasAccidentLeave, model.AccidentLeaveDays, nameof(model.AccidentLeaveDays), "روزهای مرخصی حادثه");
        ValidateCount(model.HasCommendations, model.CommendationCount, nameof(model.CommendationCount), "تعداد تشویق‌ها");

        if (model.PreviousPeriodScore1 is < 0 or > 100 || model.PreviousPeriodScore2 is < 0 or > 100)
            ModelState.AddModelError("", "امتیازهای دوره‌های پیشین باید بین صفر تا ۱۰۰ باشند.");
        if (model.PreviousPeriodScore1 != decimal.Round(model.PreviousPeriodScore1, 2) ||
            model.PreviousPeriodScore2 != decimal.Round(model.PreviousPeriodScore2, 2))
            ModelState.AddModelError("", "امتیازهای دوره‌های پیشین حداکثر دو رقم اعشار دارند.");

        if (!model.IsEditMode)
        {
            if (!model.QuestionsReady)
                ModelState.AddModelError("", "مجموع سقف امتیاز پرسش‌های فعال باید دقیقاً ۱۰۰ باشد.");
            if (!model.WorkflowSettingsReady)
                ModelState.AddModelError("", "تنظیم جایگاه حراست، منابع انسانی و مدیرعامل یا نسخه نهایی ساختار سازمانی کامل نیست.");
        }
    }

    private void ValidateCount(bool hasRecord, int count, string key, string title)
    {
        if (count < 0 || count > 100000)
            ModelState.AddModelError(key, $"{title} باید عددی معتبر و نامنفی باشد.");
        else if (hasRecord && count == 0)
            ModelState.AddModelError(key, $"در صورت انتخاب وجود سابقه، {title} باید بیشتر از صفر باشد.");
    }

    private async Task<HRPortal.Models.OrganizationStructureRevision?> CurrentRevisionAsync() =>
        await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

    private async Task<ContractRenewalWorkflowSettingsViewModel> BuildWorkflowSettingsViewModelAsync(
        int? requestedSecurityPositionId = null,
        int? requestedHumanResourcesPositionId = null)
    {
        var current = await CurrentRevisionAsync();
        var persisted = await db.ContractRenewalWorkflowSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == 1) ?? new ContractRenewalWorkflowSettings { Id = 1 };
        var positions = current is null ? new List<OrganizationNode>() : await db.OrganizationNodes.AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == current.Id && x.IsActive)
            .OrderBy(x => x.RankType).ThenBy(x => x.Title).ToListAsync();
        var persistedSecurityNode = persisted.SecurityPositionId.HasValue
            ? await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == persisted.SecurityPositionId.Value)
            : null;
        var persistedHrNode = persisted.HumanResourcesPositionId.HasValue
            ? await db.OrganizationNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == persisted.HumanResourcesPositionId.Value)
            : null;
        var selectedSecurityPositionId = requestedSecurityPositionId ??
            (persistedSecurityNode is null ? null : positions.FirstOrDefault(x => x.Code == persistedSecurityNode.Code)?.Id);
        var selectedHrPositionId = requestedHumanResourcesPositionId ??
            (persistedHrNode is null ? null : positions.FirstOrDefault(x => x.Code == persistedHrNode.Code)?.Id);
        var ceoNodes = positions.Where(x => x.RankType == "مدیرعامل").ToList();
        var ceoEmployees = ceoNodes.Count != 1 ? new List<Employee>() : await db.Employees.AsNoTracking()
            .Where(x => x.Status == "فعال" && x.OrganizationPositionId == ceoNodes[0].Id)
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync();

        return new ContractRenewalWorkflowSettingsViewModel
        {
            Settings = new ContractRenewalWorkflowSettings
            {
                SecurityPositionId = selectedSecurityPositionId,
                HumanResourcesPositionId = selectedHrPositionId,
                CeoEmployeeId = ceoEmployees.Count == 1 ? ceoEmployees[0].Id : null
            },
            Positions = positions,
            ActiveEmployees = await db.Employees.AsNoTracking().Where(x => x.Status == "فعال")
                .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync(),
            ChiefExecutiveEmployee = ceoEmployees.Count == 1 ? ceoEmployees[0] : null,
            ErrorMessage = ceoEmployees.Count == 1
                ? null
                : "برای ادامه باید دقیقاً یک کارمند فعال در رده مدیرعامل نسخه نهایی ساختار سازمانی قرار گیرد."
        };
    }

    private async Task<ContractManagementBasicInfoViewModel> BuildBasicInfoViewModelAsync(
        EmployeePerformanceEvaluationQuestion? editingQuestion = null)
    {
        var questions = await db.EmployeePerformanceEvaluationQuestions.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();

        return new ContractManagementBasicInfoViewModel
        {
            Questions = questions,
            EditingQuestion = editingQuestion ?? new EmployeePerformanceEvaluationQuestion
            {
                IsActive = true,
                SortOrder = questions.Count == 0 ? 10 : questions.Max(x => x.SortOrder) + 10
            },
            ActiveQuestionCount = questions.Count(x => x.IsActive),
            ActiveMaxScoreTotal = questions.Where(x => x.IsActive).Sum(x => x.MaxScore),
            WorkflowSettings = await BuildWorkflowSettingsViewModelAsync()
        };
    }
}
