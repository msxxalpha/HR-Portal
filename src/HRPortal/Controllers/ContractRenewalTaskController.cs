using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Controllers;

[Authorize(Policy = "Workflow.Inbox")]
public class ContractRenewalTaskController(
    HRPortalDbContext db,
    WorkflowService workflow,
    AuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Forbid();
        var task = await workflow.GetTaskAsync(id, employeeId) ?? await workflow.GetTaskForHistoryAsync(id, employeeId);
        if (task is null) return NotFound();
        if (task.WorkflowInstance.EntityType != ContractRenewalWorkflowService.WorkflowEntityType)
            return NotFound();

        if (!int.TryParse(task.WorkflowInstance.EntityId, out var requestId)) return NotFound();
        var request = await db.ContractRenewalRequests
            .AsNoTracking()
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.Id == requestId && x.WorkflowInstanceId == task.WorkflowInstanceId);
        if (request is null) return NotFound();

        var stageKind = GetStageKind(task.WorkflowStep.Code);
        var model = await BuildStageViewModelAsync(task, request, stageKind);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        int id,
        string? comment,
        string? action,
        Dictionary<string, decimal?>? scores,
        string? securityDecision,
        int? contractMonths,
        decimal? disciplinaryDeductionPercent)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Forbid();
        var task = await workflow.GetTaskAsync(id, employeeId);
        if (task is null) return NotFound();
        if (task.WorkflowInstance.EntityType != ContractRenewalWorkflowService.WorkflowEntityType)
            return NotFound();
        if (!int.TryParse(task.WorkflowInstance.EntityId, out var requestId)) return NotFound();

        var request = await db.ContractRenewalRequests
            .FirstOrDefaultAsync(x => x.Id == requestId && x.WorkflowInstanceId == task.WorkflowInstanceId);
        if (request is null) return NotFound();

        var stageKind = GetStageKind(task.WorkflowStep.Code);
        var enableHrDisciplinaryScoreDeduction = stageKind == "HumanResources" &&
            (await db.SystemSettings.AsNoTracking()
                .Select(x => (bool?)x.EnableHrDisciplinaryScoreDeduction)
                .FirstOrDefaultAsync() ?? true);
        var isLastEvaluationStage = false;
        decimal? completedEvaluationScore = null;

        try
        {
            if (comment is { Length: > 2000 })
                throw new InvalidOperationException("توضیحات هر مرحله حداکثر می‌تواند ۲۰۰۰ نویسه باشد؛ این محدودیت برای حفظ کامل سابقه پایگاه داده اعمال می‌شود.");

            if (stageKind == "IntakeApproval")
            {
                if (action is not ("Approve" or "Return"))
                    throw new InvalidOperationException("برای این مرحله فقط تأیید یا برگشت به ثبت‌کننده مجاز است.");
                if (action == "Return" && string.IsNullOrWhiteSpace(comment))
                    throw new InvalidOperationException("برای برگشت درخواست، توضیح اصلاحات الزامی است.");
            }
            else if (stageKind == "Evaluation")
            {
                var currentRows = await db.EmployeePerformanceEvaluationScores
                    .Where(x => x.WorkflowInstanceId == task.WorkflowInstanceId && x.WorkflowStepId == task.WorkflowStepId)
                    .OrderBy(x => x.Id)
                    .ToListAsync();
                if (currentRows.Count == 0)
                    throw new InvalidOperationException("پرسش‌های ارزشیابی برای این مرحله ساخته نشده‌اند.");

                var validIds = currentRows.Select(x => x.Id).ToHashSet();
                var parsedScores = new Dictionary<int, decimal?>();
                if (scores is not null)
                {
                    foreach (var pair in scores)
                    {
                        if (!int.TryParse(pair.Key, out var scoreId) || !validIds.Contains(scoreId))
                            throw new InvalidOperationException("اطلاعات ارسالی امتیازدهی معتبر نیست.");
                        parsedScores[scoreId] = pair.Value;
                    }
                }
                if (scores is null)
                    throw new InvalidOperationException("اطلاعات ارسالی امتیازدهی معتبر نیست.");

                foreach (var row in currentRows)
                {
                    if (!parsedScores.TryGetValue(row.Id, out var value) || !value.HasValue)
                        throw new InvalidOperationException($"برای پرسش «{row.QuestionTextSnapshot}» امتیاز وارد کنید.");
                    if (value.Value < 0 || value.Value > row.MaxScoreSnapshot)
                        throw new InvalidOperationException($"امتیاز پرسش «{row.QuestionTextSnapshot}» باید بین صفر و سقف {row.MaxScoreSnapshot:0.##} باشد.");
                    if (value.Value != decimal.Round(value.Value, 2))
                        throw new InvalidOperationException("امتیازها حداکثر دو رقم اعشار دارند.");

                    row.Score = value.Value;
                    row.EvaluatorEmployeeId = employeeId;
                    row.CreatedAt = DateTime.UtcNow;
                }

                completedEvaluationScore = currentRows.Sum(x => x.Score ?? 0m);
                var laterStages = await db.WorkflowSteps
                    .AnyAsync(x => x.WorkflowDefinitionId == task.WorkflowInstance.WorkflowDefinitionId &&
                        x.Code.StartsWith("Evaluation_H") && x.SortOrder > task.WorkflowStep.SortOrder);
                isLastEvaluationStage = !laterStages;
            }
            else if (stageKind == "Security")
            {
                if (securityDecision is not ("ApproveFull" or "ApproveOneMonth" or "Reject"))
                    throw new InvalidOperationException("یکی از سه گزینه اظهار نظر حراست را انتخاب کنید.");
            }
            else if (stageKind == "HumanResources")
            {
                if (!contractMonths.HasValue || contractMonths.Value is < 0 or > 12)
                    throw new InvalidOperationException("مدت قرارداد باید عدد صحیحی بین صفر تا ۱۲ ماه باشد.");
                if (contractMonths.Value > ContractRenewalWorkflowService.MaximumAllowedContractMonths(request.SecurityDecision))
                    throw new InvalidOperationException("با توجه به تصمیم حراست، مدت تمدید قرارداد نمی‌تواند بیش از یک ماه باشد.");
                if (enableHrDisciplinaryScoreDeduction)
                {
                    if (!disciplinaryDeductionPercent.HasValue || disciplinaryDeductionPercent.Value < 0m || disciplinaryDeductionPercent.Value > 100m)
                        throw new InvalidOperationException("درصد کسر بابت سوابق انضباطی باید بین صفر تا ۱۰۰ باشد.");
                    if (disciplinaryDeductionPercent.Value != decimal.Round(disciplinaryDeductionPercent.Value, 2))
                        throw new InvalidOperationException("درصد کسر حداکثر می‌تواند دو رقم اعشار داشته باشد.");
                }
            }
            else if (stageKind == "CEO")
            {
                if (!contractMonths.HasValue || contractMonths.Value is < 0 or > 12)
                    throw new InvalidOperationException("مدت قرارداد باید عدد صحیحی بین صفر تا ۱۲ ماه باشد.");
                if (contractMonths.Value > ContractRenewalWorkflowService.MaximumAllowedContractMonths(request.SecurityDecision))
                    throw new InvalidOperationException("تصمیم حراست تمدید بیش از یک ماه را مجاز نمی‌داند.");
            }
            else
            {
                throw new InvalidOperationException("نوع مرحله گردش کار معتبر نیست.");
            }

            // Keep business data and workflow transition in the same WorkflowService transaction.
            if (stageKind == "IntakeApproval")
            {
                request.Status = action == "Return"
                    ? "برگشت برای اصلاح اطلاعات اولیه"
                    : "در حال ارزیابی سازمانی";
            }
            else if (stageKind == "Evaluation" && isLastEvaluationStage)
            {
                request.OriginalEvaluationScore = completedEvaluationScore;
                request.FinalEvaluationScore = completedEvaluationScore;
                request.HrDisciplinaryDeductionPercent = 0m;
                request.Status = "در انتظار اظهارنظر حراست";
            }
            else if (stageKind == "Evaluation")
            {
                request.Status = "در حال ارزیابی سازمانی";
            }
            else if (stageKind == "Security")
            {
                request.SecurityDecision = securityDecision;
                request.SecurityComment = comment?.Trim();
                if (securityDecision == "Reject")
                {
                    request.Status = "تمدید قرارداد تأیید نشد";
                    request.CompletedAt = DateTime.UtcNow;
                }
                else
                {
                    request.Status = "در انتظار مدیر منابع انسانی و خدمات پشتیبانی";
                }
            }
            else if (stageKind == "HumanResources")
            {
                var baseScore = request.OriginalEvaluationScore ?? request.FinalEvaluationScore ?? 0m;
                request.OriginalEvaluationScore ??= baseScore;
                // The feature flag is enforced on the server. A crafted or stale POST
                // cannot apply a deduction while the system administrator has disabled it.
                var deductionPercent = enableHrDisciplinaryScoreDeduction
                    ? disciplinaryDeductionPercent!.Value
                    : 0m;
                request.HrDisciplinaryDeductionPercent = deductionPercent;
                request.FinalEvaluationScore = enableHrDisciplinaryScoreDeduction
                    ? decimal.Round(baseScore * (100m - deductionPercent) / 100m, 2, MidpointRounding.AwayFromZero)
                    : baseScore;
                request.HrProposedContractMonths = contractMonths;
                request.HrComment = comment?.Trim();
                request.Status = "در انتظار تأیید نهایی مدیرعامل";
            }
            else if (stageKind == "CEO")
            {
                request.FinalContractMonths = contractMonths;
                request.CeoComment = comment?.Trim();
                request.Status = "تکمیل شده";
                request.CompletedAt = DateTime.UtcNow;
            }

            // Security's negative decision is a real workflow rejection, not an approval.
            var workflowAction = stageKind == "IntakeApproval"
                ? action!
                : stageKind == "Security" && securityDecision == "Reject" ? "Reject" : "Approve";
            await workflow.CompleteTaskAsync(id, employeeId, workflowAction, comment);
            await audit.WriteAsync("ثبت مرحله ارزشیابی تمدید قرارداد", nameof(ContractRenewalRequest),
                request.Id.ToString(), $"{task.WorkflowStep.Title} | {stageKind}");
            TempData["Success"] = stageKind == "IntakeApproval" && workflowAction == "Return"
                ? $"درخواست {request.RequestCode} برای اصلاح اطلاعات به ثبت‌کننده برگشت داده شد."
                : stageKind == "IntakeApproval"
                    ? $"اطلاعات اولیه درخواست {request.RequestCode} تأیید شد و وارد زنجیره ارزشیابی سازمانی شد."
                    : stageKind == "CEO"
                ? $"درخواست {request.RequestCode} با تأیید مدیرعامل تکمیل شد."
                : stageKind == "Security" && securityDecision == "Reject"
                    ? $"درخواست {request.RequestCode} با تصمیم عدم موافقت حراست خاتمه یافت."
                    : "اطلاعات مرحله جاری ذخیره و درخواست به مرحله بعد ارجاع شد.";
            return RedirectToAction("Inbox", "Workflow");
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveReturnedRequest(int id, ContractRenewalIntakeViewModel input)
    {
        if (!TryGetEmployeeId(out var employeeId)) return Forbid();
        var task = await workflow.GetTaskAsync(id, employeeId);
        if (task is null) return NotFound();
        if (task.WorkflowInstance.EntityType != ContractRenewalWorkflowService.WorkflowEntityType ||
            task.WorkflowStep.Code != "RequesterEdit" ||
            task.WorkflowInstance.RequesterEmployeeId != employeeId)
            return Forbid();

        if (!int.TryParse(task.WorkflowInstance.EntityId, out var requestId)) return NotFound();
        var request = await db.ContractRenewalRequests
            .FirstOrDefaultAsync(x => x.Id == requestId && x.WorkflowInstanceId == task.WorkflowInstanceId);
        if (request is null || request.CreatedByEmployeeId != employeeId) return Forbid();

        if (input.PreviousExtensionMonths1 is < 0 or > 120 ||
            input.PreviousExtensionMonths2 is < 0 or > 120 ||
            input.PreviousExtensionMonths3 is < 0 or > 120)
            ModelState.AddModelError("", "مدت تمدیدهای قبلی باید عددی بین صفر تا ۱۲۰ ماه باشد.");
        ValidateReturnedCount(input.HasDisciplinaryWarnings, input.DisciplinaryWarningCount, "تعداد اخطارهای انضباطی");
        ValidateReturnedCount(input.HasDisciplinaryCommitteeResolution, input.DisciplinaryCommitteeResolutionCount, "تعداد مصوبه‌های کمیته انضباطی");
        ValidateReturnedCount(input.HasUnpaidLeave, input.UnpaidLeaveDays, "روزهای مرخصی بدون حقوق");
        ValidateReturnedCount(input.HasSickLeave, input.SickLeaveDays, "روزهای مرخصی بیماری");
        ValidateReturnedCount(input.HasAccidentLeave, input.AccidentLeaveDays, "روزهای مرخصی حادثه");
        ValidateReturnedCount(input.HasCommendations, input.CommendationCount, "تعداد تشویق‌ها");
        if ((!request.PreviousPeriodScore1FromHistory && (input.PreviousPeriodScore1 is < 0 or > 100 ||
                input.PreviousPeriodScore1 != decimal.Round(input.PreviousPeriodScore1, 2))) ||
            (!request.PreviousPeriodScore2FromHistory && (input.PreviousPeriodScore2 is < 0 or > 100 ||
                input.PreviousPeriodScore2 != decimal.Round(input.PreviousPeriodScore2, 2))))
            ModelState.AddModelError("", "امتیازهای دستی دوره‌های پیشین باید بین صفر تا ۱۰۰ و حداکثر دو رقم اعشار باشند.");

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id });
        }

        await PreserveRequestRevisionAsync(request, "BeforeRequesterCorrection", employeeId);
        request.PreviousExtensionMonths1 = input.PreviousExtensionMonths1;
        request.PreviousExtensionMonths2 = input.PreviousExtensionMonths2;
        request.PreviousExtensionMonths3 = input.PreviousExtensionMonths3;
        request.HasDisciplinaryWarnings = input.HasDisciplinaryWarnings;
        request.DisciplinaryWarningCount = input.HasDisciplinaryWarnings ? input.DisciplinaryWarningCount : 0;
        request.HasDisciplinaryCommitteeResolution = input.HasDisciplinaryCommitteeResolution;
        request.DisciplinaryCommitteeResolutionCount = input.HasDisciplinaryCommitteeResolution ? input.DisciplinaryCommitteeResolutionCount : 0;
        request.HasUnpaidLeave = input.HasUnpaidLeave;
        request.UnpaidLeaveDays = input.HasUnpaidLeave ? input.UnpaidLeaveDays : 0;
        request.HasSickLeave = input.HasSickLeave;
        request.SickLeaveDays = input.HasSickLeave ? input.SickLeaveDays : 0;
        request.HasAccidentLeave = input.HasAccidentLeave;
        request.AccidentLeaveDays = input.HasAccidentLeave ? input.AccidentLeaveDays : 0;
        request.HasCommendations = input.HasCommendations;
        request.CommendationCount = input.HasCommendations ? input.CommendationCount : 0;
        if (!request.PreviousPeriodScore1FromHistory) request.PreviousPeriodScore1 = input.PreviousPeriodScore1;
        if (!request.PreviousPeriodScore2FromHistory) request.PreviousPeriodScore2 = input.PreviousPeriodScore2;
        request.Status = "در انتظار تأیید مجدد اطلاعات اولیه";

        try
        {
            await workflow.CompleteTaskAsync(id, employeeId, "Approve",
                "اطلاعات اولیه توسط ثبت‌کننده اصلاح و برای تأیید مجدد ارسال شد.");
            await audit.WriteAsync("اصلاح اطلاعات اولیه درخواست تمدید قرارداد", nameof(ContractRenewalRequest),
                request.Id.ToString(), request.RequestCode);
            TempData["Success"] = "اصلاحات ذخیره و برای تأیید مجدد به جایگاه تأییدکننده ارسال شد.";
            return RedirectToAction("Inbox", "Workflow");
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    private void ValidateReturnedCount(bool hasRecord, int count, string title)
    {
        if (count < 0 || count > 100000 || (hasRecord && count == 0))
            ModelState.AddModelError("", $"مقدار «{title}» باید نامنفی و در صورت وجود سابقه بیشتر از صفر باشد.");
    }

    private async Task PreserveRequestRevisionAsync(ContractRenewalRequest request, string action, int actorEmployeeId)
    {
        var nextRevision = (await db.ContractRenewalRequestRevisions
            .Where(x => x.ContractRenewalRequestId == request.Id)
            .Select(x => (int?)x.RevisionNumber)
            .MaxAsync() ?? 0) + 1;
        var snapshot = new
        {
            request.Id, request.RequestCode, request.EmployeeId, request.CreatedByEmployeeId,
            request.WorkflowInstanceId, request.Status, request.EmployeeNameSnapshot,
            request.PersonnelNumberSnapshot, request.OrganizationUnitSnapshot, request.PositionTitleSnapshot,
            request.PreviousContractEndDate, request.PreviousExtensionMonths1, request.PreviousExtensionMonths2,
            request.PreviousExtensionMonths3, request.HasDisciplinaryWarnings, request.DisciplinaryWarningCount,
            request.HasDisciplinaryCommitteeResolution, request.DisciplinaryCommitteeResolutionCount,
            request.HasUnpaidLeave, request.UnpaidLeaveDays, request.HasSickLeave, request.SickLeaveDays,
            request.HasAccidentLeave, request.AccidentLeaveDays, request.HasCommendations, request.CommendationCount,
            request.PreviousPeriodScore1, request.PreviousPeriodScore2,
            request.PreviousPeriodScore1FromHistory, request.PreviousPeriodScore2FromHistory,
            request.OriginalEvaluationScore, request.FinalEvaluationScore, request.HrDisciplinaryDeductionPercent,
            request.SecurityDecision, request.SecurityComment, request.HrProposedContractMonths, request.HrComment,
            request.FinalContractMonths, request.CeoComment, request.CreatedAt, request.CompletedAt
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
            ChangedByEmployeeId = actorEmployeeId,
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task<ContractRenewalStageViewModel> BuildStageViewModelAsync(
        WorkflowTask task, ContractRenewalRequest request, string stageKind)
    {
        var enableHrDisciplinaryScoreDeduction = await db.SystemSettings.AsNoTracking()
            .Select(x => (bool?)x.EnableHrDisciplinaryScoreDeduction)
            .FirstOrDefaultAsync() ?? true;

        var allSteps = await db.WorkflowSteps.AsNoTracking()
            .Include(x => x.OrganizationNode)
            .Where(x => x.WorkflowDefinitionId == task.WorkflowInstance.WorkflowDefinitionId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        var allScores = await db.EmployeePerformanceEvaluationScores.AsNoTracking()
            .Where(x => x.WorkflowInstanceId == task.WorkflowInstanceId)
            .OrderBy(x => x.Id).ToListAsync();

        var evaluationSteps = allSteps.Where(x => x.Code.StartsWith("Evaluation_H"))
            .OrderBy(x => x.SortOrder).ToList();
        var currentScores = allScores.Where(x => x.WorkflowStepId == task.WorkflowStepId)
            .OrderBy(x => x.Id).ToList();
        var previousStep = evaluationSteps.Where(x => x.SortOrder < task.WorkflowStep.SortOrder)
            .OrderByDescending(x => x.SortOrder).FirstOrDefault();
        var previousScores = previousStep is null
            ? new List<EmployeePerformanceEvaluationScore>()
            : allScores.Where(x => x.WorkflowStepId == previousStep.Id).OrderBy(x => x.Id).ToList();

        var completedStageTasks = await db.WorkflowTasks.AsNoTracking()
            .Where(x => x.WorkflowInstanceId == task.WorkflowInstanceId && x.Status == "Completed")
            .OrderByDescending(x => x.CompletedAt).ThenByDescending(x => x.Id)
            .ToListAsync();
        var groups = new List<ContractRenewalScoreStageGroup>();
        foreach (var stage in evaluationSteps)
        {
            var rows = allScores.Where(x => x.WorkflowStepId == stage.Id).OrderBy(x => x.Id).ToList();
            if (rows.Count == 0) continue;
            groups.Add(new ContractRenewalScoreStageGroup
            {
                StageTitle = stage.Title,
                PositionTitle = stage.OrganizationNode?.Title ?? stage.Title,
                ScoreTotal = rows.Sum(x => x.Score ?? 0m),
                Comment = completedStageTasks.FirstOrDefault(x => x.WorkflowStepId == stage.Id)?.Comment,
                Scores = rows
            });
        }

        var returnComment = stageKind == "RequesterEdit"
            ? await db.WorkflowHistory.AsNoTracking()
                .Where(x => x.WorkflowInstanceId == task.WorkflowInstanceId &&
                    x.WorkflowStep.Code == "IntakeApproval" && x.Action == "Return")
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => x.Comment)
                .FirstOrDefaultAsync()
            : null;

        var returnedEdit = stageKind == "RequesterEdit" ? new ContractRenewalIntakeViewModel
        {
            RequestId = request.Id, IsEditMode = true, EmployeeId = request.EmployeeId,
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
        } : null;

        return new ContractRenewalStageViewModel
        {
            Task = task,
            ReturnedEdit = returnedEdit,
            ReturnComment = returnComment,
            IsReadOnly = task.Status != "Pending",
            Request = request,
            SubjectEmployee = request.Employee,
            StageKind = stageKind,
            IsHrDisciplinaryScoreDeductionEnabled = enableHrDisciplinaryScoreDeduction,
            CurrentScores = currentScores,
            PreviousStageScores = previousScores,
            AllScoreStages = groups,
            CurrentQuestionCeilingTotal = currentScores.Sum(x => x.MaxScoreSnapshot),
            PreviousStageScoreTotal = previousScores.Count > 0 && previousScores.All(x => x.Score.HasValue)
                ? previousScores.Sum(x => x.Score!.Value)
                : null,
            PreviousStageComment = previousStep is null
                ? null
                : completedStageTasks.FirstOrDefault(x => x.WorkflowStepId == previousStep.Id)?.Comment,
            MaximumAllowedContractMonths = ContractRenewalWorkflowService.MaximumAllowedContractMonths(request.SecurityDecision),
            SuggestedContractMonths = Math.Min(
                ContractRenewalWorkflowService.SuggestedContractMonths(
                    stageKind == "HumanResources" && !enableHrDisciplinaryScoreDeduction && task.Status == "Pending"
                        ? request.OriginalEvaluationScore ?? request.FinalEvaluationScore
                        : request.FinalEvaluationScore),
                ContractRenewalWorkflowService.MaximumAllowedContractMonths(request.SecurityDecision))
        };
    }

    private static string GetStageKind(string code)
    {
        if (code.StartsWith("Evaluation_H", StringComparison.Ordinal)) return "Evaluation";
        return code switch
        {
            "IntakeApproval" => "IntakeApproval",
            "RequesterEdit" => "RequesterEdit",
            "Security" => "Security",
            "HumanResources" => "HumanResources",
            "CEO" => "CEO",
            _ => "Unknown"
        };
    }

    private bool TryGetEmployeeId(out int employeeId) =>
        int.TryParse(User.FindFirst("EmployeeId")?.Value, out employeeId);
}
