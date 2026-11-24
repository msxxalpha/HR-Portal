using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize]
public class WorkflowController(
    WorkflowService workflow,
    HRPortal.Data.HRPortalDbContext db,
    WorkflowProcessPdfService workflowProcessPdf,
    AuditService audit) : Controller
{

    [HttpGet]
    [Authorize(Policy = "Workflow.Manage")]
    public async Task<IActionResult> UnassignedTasks(
        string? q = null,
        string? reason = null,
        int? workflowId = null,
        string? step = null,
        string? position = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null)
    {
        var revision = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        var positions = revision is null
            ? new List<HRPortal.Models.OrganizationNode>()
            : await db.OrganizationNodes.AsNoTracking()
                .Where(x => x.OrganizationStructureRevisionId == revision.Id && x.IsActive)
                .ToListAsync();
        var positionByCode = positions
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(n => n.Id).First(), StringComparer.OrdinalIgnoreCase);
        var activeEmployees = await db.Employees.AsNoTracking()
            .Where(x => x.Status == "فعال").ToListAsync();

        var tasks = await db.WorkflowTasks.AsNoTracking()
            .Include(x => x.WorkflowInstance).ThenInclude(x => x.WorkflowDefinition)
            .Include(x => x.WorkflowInstance).ThenInclude(x => x.RequesterEmployee)
            .Include(x => x.WorkflowStep).ThenInclude(x => x.OrganizationNode)
            .Include(x => x.AssignedPosition)
            .Include(x => x.AssignedEmployee)
            .Where(x => x.Status == "Pending")
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        var rows = new List<UnassignedWorkflowTaskRow>();
        foreach (var task in tasks)
        {
            var positionCode = task.AssignedPosition?.Code ?? task.WorkflowStep.OrganizationNode?.Code;
            var positionTitle = task.AssignedPosition?.Title ?? task.WorkflowStep.OrganizationNode?.Title ?? "—";
            var issueCode = WorkflowTaskAssignmentHealthResolver.GetIssueCode(
                task, positionByCode, activeEmployees);
            if (issueCode is null) continue;

            if (!string.IsNullOrWhiteSpace(positionCode) &&
                positionByCode.TryGetValue(positionCode.Trim(), out var currentPosition))
                positionTitle = currentPosition.Title;

            var issue = issueCode switch
            {
                "PositionMissing" => "جایگاه متناظر در آخرین نسخه نهایی و مؤثر ساختار پیدا نشد.",
                "NoActiveHolder" => "جایگاه در ساختار وجود دارد، اما متصدی فعال و قابل اقدام ندارد.",
                "InvalidAssignment" => "فهرست متصدیان جایگاه شامل شناسه نامعتبر یا کارمند غیرفعال است.",
                "EmployeeMissing" => "تخصیص مستقیم کارمند خالی است یا کارمند تخصیص‌یافته فعال نیست.",
                _ => "تخصیص وظیفه نیازمند بررسی مدیریتی است."
            };

            rows.Add(new UnassignedWorkflowTaskRow
            {
                TaskId = task.Id,
                WorkflowInstanceId = task.WorkflowInstanceId,
                WorkflowId = task.WorkflowInstance.WorkflowDefinitionId,
                WorkflowTitle = task.WorkflowInstance.WorkflowDefinition.Title,
                EntityType = task.WorkflowInstance.EntityType,
                EntityId = task.WorkflowInstance.EntityId,
                RequesterName = task.WorkflowInstance.RequesterEmployee.FirstName + " " + task.WorkflowInstance.RequesterEmployee.LastName,
                StepTitle = task.WorkflowStep.Title,
                PositionCode = positionCode ?? "—",
                PositionTitle = positionTitle,
                CreatedAt = task.CreatedAt,
                ReasonCode = issueCode,
                Reason = issue
            });
        }

        rows = UnassignedWorkflowTaskFilter.Apply(
            rows, q, reason, workflowId, step, position, createdFrom, createdTo);

        ViewBag.Search = q;
        ViewBag.Reason = reason;
        ViewBag.WorkflowId = workflowId;
        ViewBag.Step = step;
        ViewBag.Position = position;
        ViewBag.CreatedFrom = createdFrom?.ToString("yyyy-MM-dd");
        ViewBag.CreatedTo = createdTo?.ToString("yyyy-MM-dd");
        ViewBag.ActiveEmployees = activeEmployees.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToList();
        ViewBag.WorkflowDefinitions = tasks.Select(x => x.WorkflowInstance.WorkflowDefinition)
            .DistinctBy(x => x.Id).OrderBy(x => x.Title).ToList();
        return View(rows);
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReassignUnassignedTask(int id, int employeeId)
    {
        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == employeeId && x.Status == "فعال");
        if (employee is null)
        {
            TempData["Error"] = "فقط می‌توان وظیفه را به کارمند فعال واگذار کرد.";
            return RedirectToAction(nameof(UnassignedTasks));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var task = await db.WorkflowTasks
            .Include(x => x.AssignedPosition)
            .Include(x => x.WorkflowStep)
            .Include(x => x.WorkflowInstance)
            .FirstOrDefaultAsync(x => x.Id == id && x.Status == "Pending");
        if (task is null)
        {
            TempData["Error"] = "وظیفه باز موردنظر پیدا نشد یا قبلاً تعیین تکلیف شده است.";
            return RedirectToAction(nameof(UnassignedTasks));
        }

        var currentRevision = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        var currentPositions = currentRevision is null
            ? new List<HRPortal.Models.OrganizationNode>()
            : await db.OrganizationNodes.AsNoTracking()
                .Where(x => x.OrganizationStructureRevisionId == currentRevision.Id && x.IsActive)
                .ToListAsync();
        var currentPositionByCode = currentPositions
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(n => n.Id).First(), StringComparer.OrdinalIgnoreCase);
        var activeEmployees = await db.Employees.AsNoTracking()
            .Where(x => x.Status == "فعال").ToListAsync();
        if (WorkflowTaskAssignmentHealthResolver.GetIssueCode(
                task, currentPositionByCode, activeEmployees) is null)
        {
            TempData["Error"] = "این وظیفه در حال حاضر تخصیص معتبر و متصدی فعال دارد و از فهرست وظایف بلاتکلیف خارج شده است.";
            return RedirectToAction(nameof(UnassignedTasks));
        }

        var previousOwner = task.AssignedPosition is not null
            ? $"جایگاه {task.AssignedPosition.Code} - {task.AssignedPosition.Title}"
            : $"کارمند شناسه {task.AssignedEmployeeId?.ToString() ?? "نامشخص"}";
        task.AssignedPositionId = null;
        task.AssignedEmployeeId = employeeId;
        task.ClaimedAt = null;
        await db.SaveChangesAsync();
        await audit.WriteAsync(
            "ReassignUnassignedWorkflowTask",
            "WorkflowTask",
            task.Id.ToString(),
            $"وظیفه باز از تخصیص قبلی ({previousOwner}) به کارمند فعال {employee.FirstName} {employee.LastName} منتقل شد. شناسه گردش: {task.WorkflowInstanceId}؛ مرحله: {task.WorkflowStep.Title}.",
            int.TryParse(User.FindFirst("EmployeeId")?.Value, out var actorId) ? actorId : null);
        await transaction.CommitAsync();

        TempData["Success"] = "وظیفه با حفظ شناسه و تاریخچه به کارمند فعال واگذار شد.";
        return RedirectToAction(nameof(UnassignedTasks));
    }

    [HttpGet]
    [Authorize(Policy = "Workflow.Inbox")]
    public async Task<IActionResult> Inbox()
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return View(new List<WorkflowInboxRow>());

        ViewBag.ActionHistory = await workflow.GetMyActionHistoryAsync(employeeId);
        return View(await workflow.GetInboxAsync(employeeId));
    }

    [HttpGet]
    [Authorize(Policy = "Workflow.Inbox")]
    public async Task<IActionResult> MyRequests()
    {
        var isAdmin = User.HasClaim("IsAdmin", "1");
        var canSkipWorkflowSteps = User.HasClaim("Permission", "Workflow.SkipStep");
        ViewBag.IsAdminRequests = isAdmin;
        ViewBag.IsSkipRequests = !isAdmin && canSkipWorkflowSteps;
        if (isAdmin)
            return View(await workflow.GetAllRequestsAsync());

        if (canSkipWorkflowSteps)
            return View(await workflow.GetActiveRequestsForSkipAsync());

        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return Forbid();

        return View(await workflow.GetMyRequestsAsync(employeeId));
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Inbox")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRequest(int id)
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId) &&
            !User.HasClaim("IsAdmin", "1"))
            return Forbid();

        var isAdmin = User.HasClaim("IsAdmin", "1");
        try
        {
            await workflow.DeleteRequestAsync(id, employeeId, isAdmin);
            TempData["Success"] = "درخواست و تمام سوابق گردش کار آن به‌طور کامل حذف شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(MyRequests));
    }

    [HttpGet]
    [Authorize(Policy = "Workflow.Inbox")]
    public async Task<IActionResult> Details(int id)
    {
        var isAdmin = User.HasClaim("IsAdmin", "1");
        var hasEmployeeId = int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId);
        if (!hasEmployeeId && !isAdmin)
            return Forbid();

        var canSkipWorkflowSteps = User.HasClaim("Permission", "Workflow.SkipStep");
        var canViewOnlyWorkflowDetails = User.HasClaim("Permission", "Workflow.ViewOnly");
        var previewBoundarySortOrder = !isAdmin && !canSkipWorkflowSteps && hasEmployeeId
            ? await workflow.GetUpcomingViewBoundarySortOrderAsync(id, employeeId)
            : null;

        var instance = isAdmin || canSkipWorkflowSteps
            ? await workflow.GetInstanceForAdminAsync(id)
            : await workflow.GetInstanceAsync(id, employeeId, canViewOnlyWorkflowDetails);
        if (instance is null) return NotFound();

        ViewBag.IsWorkflowPreviewOnly = previewBoundarySortOrder.HasValue;
        var historyItems = await workflow.GetHistoryAsync(id);
        ViewBag.History = previewBoundarySortOrder.HasValue
            ? historyItems.Where(x =>
                x.WorkflowStepId.HasValue && x.WorkflowStep != null &&
                x.WorkflowStep.SortOrder < previewBoundarySortOrder.Value ||
                !x.WorkflowStepId.HasValue &&
                (x.Action == "Created" || x.Action == "Submitted" || x.Action == "Submit")).ToList()
            : historyItems;

        IQueryable<WorkflowFieldValue> formFieldValuesQuery = db.WorkflowFieldValues.AsNoTracking()
            .Include(x => x.WorkflowStep)
            .Include(x => x.WorkflowStepField)
            .Where(x => x.WorkflowInstanceId == id && x.Value != null && x.Value != "");
        if (previewBoundarySortOrder.HasValue)
            formFieldValuesQuery = formFieldValuesQuery
                .Where(x => x.WorkflowStep.SortOrder < previewBoundarySortOrder.Value);
        ViewBag.WorkflowFormFieldValues = await formFieldValuesQuery
            .OrderBy(x => x.WorkflowStep.SortOrder)
            .ThenBy(x => x.WorkflowStepField.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();

        if (instance.EntityType == "Suggestion" && int.TryParse(instance.EntityId, out var suggestionId))
        {
            ViewBag.WorkflowSuggestion = await db.Suggestions.AsNoTracking()
                .Include(x => x.Members).Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.Id == suggestionId);
        }

        if (instance.EntityType == "OutsourcedServiceRequest" &&
            int.TryParse(instance.EntityId, out var outsourcedServiceRequestId))
        {
            var outsourcedRequest = await db.OutsourcedServiceRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == outsourcedServiceRequestId);
            ViewBag.WorkflowOutsourcedServiceRequest = outsourcedRequest;
            if (outsourcedRequest is not null)
                ViewBag.WorkflowOutsourcedServiceAttachments = await db.OutsourcedServiceRequestAttachments.AsNoTracking()
                    .Where(x => x.RequestId == outsourcedRequest.Id)
                    .OrderByDescending(x => x.UploadedAtUtc).ToListAsync();
        }

        if (instance.EntityType == ContractRenewalWorkflowService.WorkflowEntityType &&
            int.TryParse(instance.EntityId, out var renewalRequestId))
        {
            var renewalRequest = await db.ContractRenewalRequests.AsNoTracking()
                .Include(x => x.Employee)
                .FirstOrDefaultAsync(x => x.Id == renewalRequestId && x.WorkflowInstanceId == instance.Id);
            if (renewalRequest is not null)
            {
                ViewBag.ContractRenewalRequest = renewalRequest;
                IQueryable<WorkflowStep> visibleRenewalStepsQuery = db.WorkflowSteps.AsNoTracking()
                    .Include(x => x.OrganizationNode)
                    .Where(x => x.WorkflowDefinitionId == instance.WorkflowDefinitionId);
                if (previewBoundarySortOrder.HasValue)
                    visibleRenewalStepsQuery = visibleRenewalStepsQuery
                        .Where(x => x.SortOrder < previewBoundarySortOrder.Value);
                var visibleRenewalSteps = await visibleRenewalStepsQuery
                    .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
                var visibleRenewalStepIds = visibleRenewalSteps.Select(x => x.Id).ToArray();
                ViewBag.ContractRenewalSteps = visibleRenewalSteps;
                ViewBag.ContractRenewalTasks = await db.WorkflowTasks.AsNoTracking()
                    .Include(x => x.AssignedEmployee).Include(x => x.AssignedPosition)
                    .Where(x => x.WorkflowInstanceId == instance.Id &&
                                visibleRenewalStepIds.Contains(x.WorkflowStepId))
                    .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync();
                ViewBag.ContractRenewalScores = await db.EmployeePerformanceEvaluationScores.AsNoTracking()
                    .Include(x => x.EvaluatorEmployee)
                    .Where(x => x.WorkflowInstanceId == instance.Id &&
                                visibleRenewalStepIds.Contains(x.WorkflowStepId))
                    .OrderBy(x => x.WorkflowStep.SortOrder).ThenBy(x => x.Id).ToListAsync();
            }
        }

        return View(instance);
    }


    [HttpGet]
    [Authorize(Policy = "Workflow.Inbox")]
    public async Task<IActionResult> DownloadFullReport(int id)
    {
        var isAdmin = User.HasClaim("IsAdmin", "1");
        var hasEmployeeId = int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId);
        if (!hasEmployeeId && !isAdmin) return Forbid();

        var canSkipWorkflowSteps = User.HasClaim("Permission", "Workflow.SkipStep");
        var canViewOnlyWorkflowDetails = User.HasClaim("Permission", "Workflow.ViewOnly");
        var previewBoundarySortOrder = !isAdmin && !canSkipWorkflowSteps && hasEmployeeId
            ? await workflow.GetUpcomingViewBoundarySortOrderAsync(id, employeeId)
            : null;

        var instance = isAdmin || canSkipWorkflowSteps
            ? await workflow.GetInstanceForAdminAsync(id)
            : await workflow.GetInstanceAsync(id, employeeId, canViewOnlyWorkflowDetails);
        if (instance is null) return NotFound();
        if (!instance.CompletedAt.HasValue &&
            instance.Status is not ("Approved" or "Completed" or "CompletedBySkip"))
            return BadRequest("گزارش تفصیلی نهایی پس از تکمیل گردش کار قابل دریافت است.");

        var loadedInstance = await db.WorkflowInstances.AsNoTracking()
            .Include(x => x.WorkflowDefinition)
            .Include(x => x.RequesterEmployee)
            .Include(x => x.CurrentStep)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (loadedInstance is null) return NotFound();
        instance = loadedInstance;

        var stepsQuery = db.WorkflowSteps.AsNoTracking().Include(x => x.OrganizationNode)
            .Where(x => x.WorkflowDefinitionId == instance.WorkflowDefinitionId);
        if (previewBoundarySortOrder.HasValue)
            stepsQuery = stepsQuery.Where(x => x.SortOrder < previewBoundarySortOrder.Value);
        var steps = await stepsQuery.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        var visibleStepIds = steps.Select(x => x.Id).ToArray();
        var tasks = await db.WorkflowTasks.AsNoTracking().Include(x => x.WorkflowStep)
            .Include(x => x.AssignedEmployee).Include(x => x.AssignedPosition)
            .Where(x => x.WorkflowInstanceId == id && visibleStepIds.Contains(x.WorkflowStepId))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync();
        var fieldsQuery = db.WorkflowFieldValues.AsNoTracking().Include(x => x.WorkflowStep)
            .Include(x => x.WorkflowStepField)
            .Where(x => x.WorkflowInstanceId == id && x.Value != null && x.Value != "");
        if (previewBoundarySortOrder.HasValue)
            fieldsQuery = fieldsQuery.Where(x => x.WorkflowStep.SortOrder < previewBoundarySortOrder.Value);
        var fields = await fieldsQuery.OrderBy(x => x.WorkflowStep.SortOrder)
            .ThenBy(x => x.WorkflowStepField.SortOrder).ThenBy(x => x.Id).ToListAsync();
        var historyQuery = db.WorkflowHistory.AsNoTracking().Include(x => x.WorkflowStep)
            .Include(x => x.ActorEmployee).Include(x => x.ActorPosition)
            .Where(x => x.WorkflowInstanceId == id);
        if (previewBoundarySortOrder.HasValue)
            historyQuery = historyQuery.Where(x =>
                (x.WorkflowStepId.HasValue && x.WorkflowStep != null &&
                    x.WorkflowStep.SortOrder < previewBoundarySortOrder.Value) ||
                (!x.WorkflowStepId.HasValue &&
                    (x.Action == "Created" || x.Action == "Submitted" || x.Action == "Submit")));
        var history = await historyQuery.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync();

        var requestDetails = new List<(string Label, string Value)>();
        if (instance.EntityType == ContractRenewalWorkflowService.WorkflowEntityType &&
            int.TryParse(instance.EntityId, out var renewalId))
        {
            var r = await db.ContractRenewalRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == renewalId && x.WorkflowInstanceId == id);
            if (r is not null)
            {
                requestDetails.AddRange(new[]
                {
                    ("کد درخواست", r.RequestCode), ("کارمند", r.EmployeeNameSnapshot + " (" + r.PersonnelNumberSnapshot + ")"),
                    ("واحد سازمانی", r.OrganizationUnitSnapshot), ("جایگاه سازمانی", r.PositionTitleSnapshot),
                    ("تمدید قبلی", r.PreviousExtensionMonths1 + "، " + r.PreviousExtensionMonths2 + "، " + r.PreviousExtensionMonths3 + " ماه"),
                    ("امتیاز دوره قبلی اول", r.PreviousPeriodScore1.ToString("0.##") + (r.PreviousPeriodScore1FromHistory ? " (از سوابق)" : "")),
                    ("امتیاز دوره قبلی دوم", r.PreviousPeriodScore2.ToString("0.##") + (r.PreviousPeriodScore2FromHistory ? " (از سوابق)" : "")),
                    ("اخطار انضباطی", r.HasDisciplinaryWarnings ? "بله - " + r.DisciplinaryWarningCount : "خیر"),
                    ("آرای کمیته انضباطی", r.HasDisciplinaryCommitteeResolution ? r.DisciplinaryCommitteeResolutionCount.ToString() : "۰"),
                    ("مرخصی بدون حقوق", r.HasUnpaidLeave ? r.UnpaidLeaveDays + " روز" : "۰ روز"),
                    ("مرخصی استعلاجی", r.HasSickLeave ? r.SickLeaveDays + " روز" : "۰ روز"),
                    ("مرخصی حادثه", r.HasAccidentLeave ? r.AccidentLeaveDays + " روز" : "۰ روز"),
                    ("تقدیرنامه", r.HasCommendations ? r.CommendationCount.ToString() : "۰")
                });
                if (!previewBoundarySortOrder.HasValue)
                {
                    requestDetails.AddRange(new[]
                    {
                        ("تصمیم حراست", r.SecurityDecision), ("توضیحات حراست", r.SecurityComment),
                        ("درصد کسر امتیاز", r.HrDisciplinaryDeductionPercent.ToString("0.##") + "٪"),
                        ("پیشنهاد مدت منابع انسانی", r.HrProposedContractMonths?.ToString() + " ماه"),
                        ("توضیحات منابع انسانی", r.HrComment), ("امتیاز نهایی", r.FinalEvaluationScore?.ToString("0.##")),
                        ("مدت مصوب مدیرعامل", r.FinalContractMonths?.ToString() + " ماه"), ("نظر مدیرعامل", r.CeoComment)
                    });
                }
            }
        }
        else if (instance.EntityType == "OutsourcedServiceRequest" &&
                 int.TryParse(instance.EntityId, out var outsourcedId))
        {
            var r = await db.OutsourcedServiceRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == outsourcedId);
            if (r is not null)
                requestDetails.AddRange(new[] { ("شماره درخواست", r.RequestNumber), ("شرح خدمات", r.ServiceDescription),
                    ("مقدار و واحد", r.Quantity.ToString("0.###") + " " + r.UnitOfMeasure),
                    ("تاریخ نیازمندی", PersianDateService.Format(r.NeedByDate)), ("اولویت", r.Priority) });
        }

        var bytes = workflowProcessPdf.Generate(instance, steps, tasks, fields, history, requestDetails);
        return File(bytes, "application/pdf", $"Workflow-{id}-Detailed.pdf");
    }

    [HttpGet]
    [Authorize(Policy = "Workflow.Inbox")]
    public async Task<IActionResult> Task(int id)
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return Forbid();

        var task = await workflow.GetTaskAsync(id, employeeId);
        if (task is null) return NotFound();
        if (task.WorkflowInstance.EntityType == ContractRenewalWorkflowService.WorkflowEntityType)
            return RedirectToAction("Details", "ContractRenewalTask", new { id });

        ViewBag.History = await workflow.GetHistoryAsync(task.WorkflowInstanceId);
        ViewBag.FieldGroups = await workflow.GetTaskFieldGroupsAsync(id, employeeId);
        ViewBag.WorkflowPositions = await GetCurrentPositionsAsync();
        if (task.WorkflowInstance.EntityType == "Suggestion" && int.TryParse(task.WorkflowInstance.EntityId, out var suggestionId))
        {
            ViewBag.WorkflowSuggestion = await db.Suggestions.AsNoTracking()
                .Include(x => x.Members).Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.Id == suggestionId);
        }
        if (task.WorkflowInstance.EntityType == "OutsourcedServiceRequest" &&
            int.TryParse(task.WorkflowInstance.EntityId, out var outsourcedServiceRequestId))
        {
            var outsourcedRequest = await db.OutsourcedServiceRequests.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == outsourcedServiceRequestId);
            ViewBag.WorkflowOutsourcedServiceRequest = outsourcedRequest;
            if (outsourcedRequest is not null)
                ViewBag.WorkflowOutsourcedServiceAttachments = await db.OutsourcedServiceRequestAttachments.AsNoTracking()
                    .Where(x => x.RequestId == outsourcedRequest.Id)
                    .OrderByDescending(x => x.UploadedAtUtc).ToListAsync();
        }
        return View(task);
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.SkipStep")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SkipCurrentStep(int workflowInstanceId, string? reason)
    {
        int? actorEmployeeId = int.TryParse(User.FindFirst("EmployeeId")?.Value, out var parsedEmployeeId)
            ? parsedEmployeeId
            : null;
        try
        {
            await workflow.SkipCurrentStepAsync(
                workflowInstanceId,
                actorEmployeeId,
                reason,
                User.Identity?.Name);
            TempData["Success"] = "مرحله جاری با ثبت سابقه عبور تعیین تکلیف شد و گردش کار به مرحله بعد منتقل گردید.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = workflowInstanceId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Inbox")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(int id, string action, string? comment)
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId))
            return Forbid();

        var task = await workflow.GetTaskAsync(id, employeeId);
        if (task is null) return NotFound();
        if (task.WorkflowInstance.EntityType == ContractRenewalWorkflowService.WorkflowEntityType)
            return RedirectToAction("Details", "ContractRenewalTask", new { id });

        try
        {
            // Dictionary model binding can fall back to unrelated form keys when a
            // collection prefix is not recognized. Read only the explicitly named
            // values[...] and previousValues[...] fields from the posted form.
            var postedForm = await Request.ReadFormAsync(HttpContext.RequestAborted);
            var values = WorkflowTaskFormValueReader.Read(postedForm, "values");
            var previousValues = WorkflowTaskFormValueReader.Read(postedForm, "previousValues");

            await workflow.CompleteTaskAsync(id, employeeId, action, comment, values, previousValues);
            TempData["Success"] = action == "SaveDraft" ? "پیش‌نویس مرحله ذخیره شد." : "اقدام گردش کار با موفقیت ثبت شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Task), new { id });
        }

        return action == "SaveDraft"
            ? RedirectToAction(nameof(Task), new { id })
            : RedirectToAction(nameof(Inbox));
    }

    [HttpGet]
    [Authorize(Policy = "Workflow.Manage")]
    public async Task<IActionResult> Manage(int? id)
    {
        var model = new WorkflowManagementViewModel
        {
            Definitions = await workflow.GetDefinitionsAsync(),
            SelectedDefinition = id.HasValue ? await workflow.GetDefinitionAsync(id.Value) : null,
            Positions = await GetCurrentPositionsAsync(),
            AvailableEmployees = await db.Employees.AsNoTracking().Where(x => x.Status == "فعال")
                .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync(),
            AvailableForms = await db.SystemFormDefinitions.AsNoTracking()
                .Where(x => x.IsActive && x.ImplementationType == "GenericWorkflow")
                .OrderBy(x => x.Title).ToListAsync(),
            RankTitles = await db.OrganizationRankDefinitions.AsNoTracking()
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Title)
                .Select(x => x.Title).ToListAsync()
        };
        return View(model);
    }

    private async Task<List<OrganizationNode>> GetCurrentPositionsAsync()
    {
        var revision = await db.OrganizationStructureRevisions
            .AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (revision is null) return [];
        return await db.OrganizationNodes
            .AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == revision.Id && x.IsActive)
            .OrderBy(x => x.RankType)
            .ThenBy(x => x.Title)
            .ToListAsync();
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDefinition(WorkflowDefinitionEditModel model)
    {
        var selectedId = model.Id;
        try { selectedId = await workflow.SaveDefinitionAsync(model); TempData["Success"] = "تعریف گردش کار ذخیره شد. نسخه‌های استفاده‌شده حفظ شدند."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = selectedId > 0 ? selectedId : (int?)null });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDefinition(int id)
    {
        try { await workflow.DeleteDefinitionAsync(id); TempData["Success"] = "گردش کار استفاده‌نشده حذف شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage));
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStepRules(int id, int definitionId, bool allowEditPreviousData, bool requireNotes,
        string hierarchyVacancyPolicy = "Skip", int? hierarchySubstitutePositionId = null)
    {
        try { await workflow.SaveStepRulesAsync(id, allowEditPreviousData, requireNotes, hierarchyVacancyPolicy, hierarchySubstitutePositionId); TempData["Success"] = "تنظیمات مرحله ذخیره شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = definitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStep(WorkflowStepEditModel model)
    {
        try { await workflow.SaveStepAsync(model); TempData["Success"] = "مرحله ذخیره شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = model.WorkflowDefinitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStepField(WorkflowStepFieldEditModel model)
    {
        try
        {
            await workflow.SaveStepFieldAsync(model);
            TempData["Success"] = "فیلد مرحله ذخیره شد.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        var step = await db.WorkflowSteps.AsNoTracking().FirstOrDefaultAsync(x => x.Id == model.WorkflowStepId);
        return RedirectToAction(nameof(Manage), new { id = step?.WorkflowDefinitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStepField(int id, int definitionId)
    {
        try { await workflow.DeleteStepFieldAsync(id); TempData["Success"] = "فیلد حذف شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = definitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStep(int id, int definitionId)
    {
        try { await workflow.DeleteStepAsync(id); TempData["Success"] = "مرحله حذف شد؛ مسیرهای مرتبط پاک و ترتیب مراحل باقی‌مانده بازتنظیم شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = definitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTransition(int id, int definitionId)
    {
        try { await workflow.DeleteTransitionAsync(id); TempData["Success"] = "مسیر شرطی حذف شد."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = definitionId });
    }

    [HttpPost]
    [Authorize(Policy = "Workflow.Manage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTransition(int stepId, int definitionId, string action, string? toStepId,
        string? conditionFieldCode, string conditionOperator = "Equals", string? conditionValue = null)
    {
        try
        {
            var returnToRequester = string.Equals(toStepId, "__REQUESTER__", StringComparison.Ordinal);
            int? targetStepId = null;
            if (!returnToRequester && !string.IsNullOrWhiteSpace(toStepId))
            {
                if (!int.TryParse(toStepId, out var parsedStepId))
                    throw new InvalidOperationException("مقصد انتخاب‌شده برای مسیر گردش کار معتبر نیست.");
                targetStepId = parsedStepId;
            }
            await workflow.SaveTransitionAsync(stepId, action, targetStepId,
                conditionFieldCode, conditionOperator, conditionValue, returnToRequester);
            TempData["Success"] = "مسیر گردش کار ذخیره شد.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Manage), new { id = definitionId });
    }
}
