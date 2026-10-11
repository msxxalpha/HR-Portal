using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class ContractRenewalWorkflowService(HRPortalDbContext db, WorkflowService workflow)
{
    public const string WorkflowEntityType = "ContractRenewalRequest";

    public async Task<WorkflowInstance> StartAsync(
        ContractRenewalRequest request,
        int requesterEmployeeId,
        CancellationToken cancellationToken = default)
    {
        var settings = await db.ContractRenewalWorkflowSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken)
            ?? throw new InvalidOperationException("تنظیمات گردش کار تمدید قرارداد تکمیل نشده است.");

        var revision = await db.OrganizationStructureRevisions
            .AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("نسخه نهایی فعال ساختار سازمانی یافت نشد.");

        if (!settings.SecurityPositionId.HasValue ||
            !await db.OrganizationNodes.AnyAsync(x => x.Id == settings.SecurityPositionId.Value &&
                x.OrganizationStructureRevisionId == revision.Id && x.IsActive, cancellationToken))
            throw new InvalidOperationException("جایگاه مدیر حراست را در تنظیمات گردش کار انتخاب کنید.");

        if (!settings.HumanResourcesPositionId.HasValue ||
            !await db.OrganizationNodes.AnyAsync(x => x.Id == settings.HumanResourcesPositionId.Value &&
                x.OrganizationStructureRevisionId == revision.Id && x.IsActive, cancellationToken))
            throw new InvalidOperationException("جایگاه مدیر منابع انسانی و خدمات پشتیبانی را در تنظیمات گردش کار انتخاب کنید.");

        var ceoNodes = await db.OrganizationNodes.AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == revision.Id && x.IsActive && x.RankType == "مدیرعامل")
            .ToListAsync(cancellationToken);
        if (ceoNodes.Count != 1)
            throw new InvalidOperationException("در آخرین نسخه نهایی ساختار سازمانی باید دقیقاً یک گره فعال با رده مدیرعامل وجود داشته باشد.");

        var ceoEmployees = await db.Employees.AsNoTracking()
            .Where(x => x.Status == "فعال" && x.OrganizationPositionId == ceoNodes[0].Id)
            .ToListAsync(cancellationToken);
        if (ceoEmployees.Count != 1)
            throw new InvalidOperationException("باید دقیقاً یک کارمند فعال به جایگاه مدیرعامل در نسخه نهایی ساختار سازمانی متصل باشد.");

        var ceoEmployeeId = ceoEmployees[0].Id;

        var questions = await db.EmployeePerformanceEvaluationQuestions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (questions.Count == 0 || questions.Sum(x => x.MaxScore) != 100m)
            throw new InvalidOperationException("شروع گردش کار ممکن نیست؛ مجموع سقف امتیاز پرسش‌های فعال باید دقیقاً ۱۰۰ باشد.");

        var code = $"__internal_contractrenewal_{request.Id}";
        var definition = new WorkflowDefinition
        {
            Code = code,
            Title = $"ارزشیابی عملکرد و تمدید قرارداد - {request.RequestCode}",
            Description = "تعریف داخلی مختص همین درخواست؛ شامل زنجیره پویا، حراست، منابع انسانی و مدیرعامل.",
            Version = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync(cancellationToken);

        db.WorkflowSteps.AddRange(
            new WorkflowStep
            {
                WorkflowDefinitionId = definition.Id,
                Code = "Evaluation",
                Title = "ارزشیابی عملکرد",
                SortOrder = 10,
                AssignmentType = "Hierarchy",
                HierarchyStopRankType = "مدیرعامل",
                AssignmentMode = "Any",
                AllowApprove = true,
                AllowReject = false,
                AllowReturn = false,
                IsFinalStep = false
            },
            new WorkflowStep
            {
                WorkflowDefinitionId = definition.Id,
                Code = "Security",
                Title = "اظهارنظر مدیر حراست",
                SortOrder = 1000,
                AssignmentType = "Position",
                OrganizationNodeId = settings.SecurityPositionId,
                AllowApprove = true,
                AllowReject = true,
                AllowReturn = false,
                RequireCommentOnReject = false,
                IsFinalStep = false
            },
            new WorkflowStep
            {
                WorkflowDefinitionId = definition.Id,
                Code = "HumanResources",
                Title = "تعیین پیشنهاد مدت قرارداد - مدیر منابع انسانی و خدمات پشتیبانی",
                SortOrder = 1010,
                AssignmentType = "Position",
                OrganizationNodeId = settings.HumanResourcesPositionId,
                AllowApprove = true,
                AllowReject = false,
                AllowReturn = true,
                RequireCommentOnReject = false,
                IsFinalStep = false
            },
            new WorkflowStep
            {
                WorkflowDefinitionId = definition.Id,
                Code = "CEO",
                Title = "تصمیم نهایی مدیرعامل",
                SortOrder = 1020,
                AssignmentType = "Employee",
                AssignedEmployeeId = ceoEmployeeId,
                AllowApprove = true,
                AllowReject = false,
                AllowReturn = true,
                RequireCommentOnReject = false,
                IsFinalStep = true
            });

        await db.SaveChangesAsync(cancellationToken);

        var instance = await workflow.StartAsync(
            code,
            WorkflowEntityType,
            request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            requesterEmployeeId,
            cancellationToken,
            subjectEmployeeId: request.EmployeeId);

        var runtimeSteps = await db.WorkflowSteps
            .Where(x => x.WorkflowDefinitionId == instance.WorkflowDefinitionId &&
                x.Code.StartsWith("Evaluation_H"))
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        foreach (var step in runtimeSteps)
        {
            foreach (var question in questions)
            {
                db.EmployeePerformanceEvaluationScores.Add(new EmployeePerformanceEvaluationScore
                {
                    QuestionId = question.Id,
                    WorkflowInstanceId = instance.Id,
                    WorkflowStepId = step.Id,
                    MaxScoreSnapshot = question.MaxScore,
                    QuestionTextSnapshot = question.QuestionText,
                    ScoringGuidanceSnapshot = question.ScoringGuidance,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return instance;
    }

    public static int SuggestedContractMonths(decimal? score)
    {
        if (!score.HasValue) return 0;
        // Unspecified boundary scores are handled without gaps: 30 -> 1 month, 88 -> 12 months.
        if (score.Value < 30m) return 0;
        if (score.Value <= 35m) return 1;
        if (score.Value <= 52m) return 3;
        if (score.Value <= 70m) return 6;
        if (score.Value <= 87m) return 9;
        return 12;
    }
}
