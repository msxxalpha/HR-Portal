using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class WorkflowService(HRPortalDbContext db, AuditService audit)
{
    public async Task<List<WorkflowDefinition>> GetDefinitionsAsync() =>
        await db.WorkflowDefinitions
            .Include(x => x.Steps)
            .ThenInclude(x => x.OrganizationNode)
            .OrderBy(x => x.Title)
            .ToListAsync();

    public async Task<WorkflowDefinition?> GetDefinitionAsync(int id) =>
        await db.WorkflowDefinitions
            .Include(x => x.Steps)
            .ThenInclude(x => x.OrganizationNode)
            .Include(x => x.Steps)
            .ThenInclude(x => x.OutgoingTransitions)
            .ThenInclude(x => x.ToStep)
            .FirstOrDefaultAsync(x => x.Id == id);

    public async Task<WorkflowDefinition?> GetByCodeAsync(string code) =>
        await db.WorkflowDefinitions
            .Include(x => x.Steps.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(x => x.Code == code && x.IsActive);

    public async Task SaveDefinitionAsync(WorkflowDefinitionEditModel model)
    {
        var code = model.Code.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(model.Title))
            throw new InvalidOperationException("کد و عنوان گردش کار الزامی است.");

        if (await db.WorkflowDefinitions.AnyAsync(x => x.Code == code && x.Id != model.Id))
            throw new InvalidOperationException("کد گردش کار قبلاً استفاده شده است.");

        WorkflowDefinition entity;
        if (model.Id == 0)
        {
            entity = new WorkflowDefinition { Code = code, Version = 1 };
            db.WorkflowDefinitions.Add(entity);
        }
        else
        {
            entity = await db.WorkflowDefinitions.FindAsync(model.Id)
                ?? throw new InvalidOperationException("گردش کار یافت نشد.");
            entity.Version++;
        }

        entity.Code = code;
        entity.Title = model.Title.Trim();
        entity.Description = model.Description?.Trim();
        entity.IsActive = model.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SaveStepAsync(WorkflowStepEditModel model)
    {
        var definition = await db.WorkflowDefinitions.FindAsync(model.WorkflowDefinitionId)
            ?? throw new InvalidOperationException("گردش کار یافت نشد.");

        if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Title))
            throw new InvalidOperationException("کد و عنوان مرحله الزامی است.");

        if (model.OrganizationNodeId.HasValue &&
            !await db.OrganizationNodes.AnyAsync(x => x.Id == model.OrganizationNodeId.Value && x.IsActive))
            throw new InvalidOperationException("جایگاه سازمانی انتخاب‌شده معتبر یا فعال نیست.");

        if (await db.WorkflowSteps.AnyAsync(x =>
                x.WorkflowDefinitionId == model.WorkflowDefinitionId &&
                x.Code == model.Code.Trim() &&
                x.Id != model.Id))
            throw new InvalidOperationException("کد مرحله در این گردش کار تکراری است.");

        WorkflowStep entity;
        if (model.Id == 0)
        {
            entity = new WorkflowStep { WorkflowDefinitionId = definition.Id };
            db.WorkflowSteps.Add(entity);
        }
        else
        {
            entity = await db.WorkflowSteps.FindAsync(model.Id)
                ?? throw new InvalidOperationException("مرحله یافت نشد.");
        }

        entity.Code = model.Code.Trim();
        entity.Title = model.Title.Trim();
        entity.SortOrder = model.SortOrder;
        entity.AssignmentType = "Position";
        entity.OrganizationNodeId = model.OrganizationNodeId;
        entity.AssignmentMode = model.AssignmentMode is "All" ? "All" : "Any";
        entity.AllowApprove = model.AllowApprove;
        entity.AllowReject = model.AllowReject;
        entity.AllowReturn = model.AllowReturn;
        entity.RequireCommentOnReject = model.RequireCommentOnReject;
        entity.RequireCommentOnReturn = model.RequireCommentOnReturn;
        entity.IsFinalStep = model.IsFinalStep;
        definition.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task SaveTransitionAsync(int stepId, string action, int? toStepId)
    {
        var step = await db.WorkflowSteps.FindAsync(stepId)
            ?? throw new InvalidOperationException("مرحله یافت نشد.");

        if (toStepId.HasValue)
        {
            var target = await db.WorkflowSteps.FirstOrDefaultAsync(x =>
                x.Id == toStepId.Value && x.WorkflowDefinitionId == step.WorkflowDefinitionId);
            if (target is null)
                throw new InvalidOperationException("مرحله مقصد معتبر نیست.");
        }

        action = NormalizeAction(action);
        var transition = await db.WorkflowTransitions
            .FirstOrDefaultAsync(x => x.WorkflowStepId == stepId && x.Action == action);

        if (transition is null)
        {
            transition = new WorkflowTransition { WorkflowStepId = stepId, Action = action };
            db.WorkflowTransitions.Add(transition);
        }

        transition.ToStepId = toStepId;
        transition.Title = ActionTitle(action);
        await db.SaveChangesAsync();
    }

    public async Task DeleteStepAsync(int id)
    {
        var step = await db.WorkflowSteps.FindAsync(id);
        if (step is null) return;
        db.WorkflowSteps.Remove(step);
        await db.SaveChangesAsync();
    }

    public async Task<WorkflowInstance> StartAsync(
        string definitionCode,
        string entityType,
        string entityId,
        int requesterEmployeeId,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.WorkflowDefinitions
            .Include(x => x.Steps.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(x => x.Code == definitionCode && x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("گردش کار فعال با این کد یافت نشد.");

        var requester = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == requesterEmployeeId && x.Status == "فعال", cancellationToken)
            ?? throw new InvalidOperationException("کارمند درخواست‌کننده یافت نشد.");

        var first = definition.Steps.OrderBy(x => x.SortOrder).FirstOrDefault()
            ?? throw new InvalidOperationException("گردش کار حداقل باید یک مرحله داشته باشد.");

        var instance = new WorkflowInstance
        {
            WorkflowDefinitionId = definition.Id,
            EntityType = entityType.Trim(),
            EntityId = entityId.Trim(),
            RequesterEmployeeId = requester.Id,
            CurrentStepId = first.Id,
            Status = "InProgress"
        };

        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync(cancellationToken);

        await CreateTasksAsync(instance, first, cancellationToken);
        db.WorkflowHistory.Add(new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            WorkflowStepId = first.Id,
            FromStatus = "New",
            ToStatus = "InProgress",
            Action = "Submit",
            ActorEmployeeId = requester.Id,
            Comment = "شروع گردش کار"
        });
        await db.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync("شروع گردش کار", nameof(WorkflowInstance), instance.Id.ToString(), definition.Title);
        return instance;
    }

    public async Task<List<WorkflowInboxRow>> GetInboxAsync(int employeeId, string? status = null)
    {
        var query = db.WorkflowTasks
            .AsNoTracking()
            .Include(x => x.WorkflowInstance).ThenInclude(x => x.WorkflowDefinition)
            .Include(x => x.WorkflowInstance).ThenInclude(x => x.RequesterEmployee)
            .Include(x => x.WorkflowStep).ThenInclude(x => x.OrganizationNode)
            .Where(x => x.Status == "Pending" &&
                        (x.AssignedEmployeeId == employeeId ||
                         (x.AssignedEmployeeId == null && x.AssignedPositionId.HasValue &&
                          db.Employees.Any(e => e.Id == employeeId && e.Status == "فعال" &&
                              (e.OrganizationUnitId == x.AssignedPositionId ||
                               e.OrganizationDepartmentId == x.AssignedPositionId ||
                               e.OrganizationSectionId == x.AssignedPositionId)))));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new WorkflowInboxRow
            {
                TaskId = x.Id,
                WorkflowInstanceId = x.WorkflowInstanceId,
                WorkflowTitle = x.WorkflowInstance.WorkflowDefinition.Title,
                EntityType = x.WorkflowInstance.EntityType,
                EntityId = x.WorkflowInstance.EntityId,
                RequesterName = x.WorkflowInstance.RequesterEmployee.FirstName + " " + x.WorkflowInstance.RequesterEmployee.LastName,
                RequesterPersonnelNumber = x.WorkflowInstance.RequesterEmployee.PersonnelNumber,
                StepTitle = x.WorkflowStep.Title,
                PositionTitle = x.AssignedPosition == null ? "" : x.AssignedPosition.Title,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                Comment = x.Comment
            })
            .ToListAsync();
    }

    public async Task<List<WorkflowHistory>> GetHistoryAsync(int instanceId) =>
        await db.WorkflowHistory
            .AsNoTracking()
            .Include(x => x.WorkflowStep)
            .Include(x => x.ActorEmployee)
            .Include(x => x.ActorPosition)
            .Where(x => x.WorkflowInstanceId == instanceId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

    public async Task<WorkflowTask?> GetTaskAsync(int id, int employeeId) =>
        await db.WorkflowTasks
            .Include(x => x.WorkflowInstance).ThenInclude(x => x.WorkflowDefinition)
            .Include(x => x.WorkflowInstance).ThenInclude(x => x.RequesterEmployee)
            .Include(x => x.WorkflowStep).ThenInclude(x => x.OrganizationNode)
            .FirstOrDefaultAsync(x => x.Id == id && x.Status == "Pending" &&
                (x.AssignedEmployeeId == employeeId ||
                 (x.AssignedEmployeeId == null && x.AssignedPositionId.HasValue &&
                  db.Employees.Any(e => e.Id == employeeId && e.Status == "فعال" &&
                      (e.OrganizationUnitId == x.AssignedPositionId ||
                       e.OrganizationDepartmentId == x.AssignedPositionId ||
                       e.OrganizationSectionId == x.AssignedPositionId)))));

    public async Task CompleteTaskAsync(int taskId, int actorEmployeeId, string action, string? comment)
    {
        action = NormalizeAction(action);
        var task = await GetTaskAsync(taskId, actorEmployeeId)
            ?? throw new InvalidOperationException("این کارتابل به شما اختصاص ندارد یا قبلاً تعیین تکلیف شده است.");

        if (action == "Approve" && !task.WorkflowStep.AllowApprove ||
            action == "Reject" && !task.WorkflowStep.AllowReject ||
            action == "Return" && !task.WorkflowStep.AllowReturn)
            throw new InvalidOperationException("این عملیات برای مرحله جاری مجاز نیست.");

        if (action == "Reject" && task.WorkflowStep.RequireCommentOnReject && string.IsNullOrWhiteSpace(comment))
            throw new InvalidOperationException("ثبت توضیح برای رد درخواست الزامی است.");
        if (action == "Return" && task.WorkflowStep.RequireCommentOnReturn && string.IsNullOrWhiteSpace(comment))
            throw new InvalidOperationException("ثبت توضیح برای برگشت الزامی است.");

        var instance = await db.WorkflowInstances
            .Include(x => x.Tasks)
            .Include(x => x.WorkflowDefinition).ThenInclude(x => x.Steps)
            .FirstAsync(x => x.Id == task.WorkflowInstanceId);

        var previousStatus = instance.Status;
        task.Status = "Completed";
        task.Action = action;
        task.Comment = comment?.Trim();
        task.CompletedAt = DateTime.UtcNow;

        var actorPositionId = ResolveEmployeePosition(actorEmployeeId, task.AssignedPositionId);
        db.WorkflowHistory.Add(new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            WorkflowStepId = task.WorkflowStepId,
            FromStatus = previousStatus,
            ToStatus = action,
            Action = action,
            ActorEmployeeId = actorEmployeeId,
            ActorPositionId = actorPositionId,
            Comment = comment?.Trim()
        });

        foreach (var sibling in instance.Tasks.Where(x => x.WorkflowStepId == task.WorkflowStepId && x.Id != task.Id && x.Status == "Pending"))
        {
            sibling.Status = "Superseded";
            sibling.CompletedAt = DateTime.UtcNow;
            sibling.Action = action;
        }

        if (action == "Approve")
        {
            var next = await ResolveTransitionAsync(task.WorkflowStepId, "Approve", instance.WorkflowDefinition.Steps);
            if (next is null || task.WorkflowStep.IsFinalStep)
            {
                instance.Status = "Approved";
                instance.CurrentStepId = null;
                instance.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                instance.CurrentStepId = next.Id;
                instance.Status = "InProgress";
                await CreateTasksAsync(instance, next, CancellationToken.None);
            }
        }
        else if (action == "Reject")
        {
            instance.Status = "Rejected";
            instance.CurrentStepId = null;
            instance.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            var next = await ResolveTransitionAsync(task.WorkflowStepId, "Return", instance.WorkflowDefinition.Steps);
            if (next is null)
            {
                instance.Status = "Returned";
                instance.CurrentStepId = null;
                instance.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                instance.CurrentStepId = next.Id;
                instance.Status = "InProgress";
                await CreateTasksAsync(instance, next, CancellationToken.None);
            }
        }

        await db.SaveChangesAsync();
        await audit.WriteAsync("اقدام در گردش کار", nameof(WorkflowTask), task.Id.ToString(), action);
    }

    private async Task CreateTasksAsync(WorkflowInstance instance, WorkflowStep step, CancellationToken cancellationToken)
    {
        if (!step.OrganizationNodeId.HasValue)
            throw new InvalidOperationException($"برای مرحله «{step.Title}» جایگاه سازمانی تعیین نشده است.");

        var positionId = step.OrganizationNodeId.Value;
        var employees = await db.Employees
            .AsNoTracking()
            .Where(e => e.Status == "فعال" &&
                (e.OrganizationUnitId == positionId ||
                 e.OrganizationDepartmentId == positionId ||
                 e.OrganizationSectionId == positionId))
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);

        if (step.AssignmentMode == "All" && employees.Count > 0)
        {
            foreach (var employee in employees)
                db.WorkflowTasks.Add(new WorkflowTask
                {
                    WorkflowInstanceId = instance.Id,
                    WorkflowStepId = step.Id,
                    AssignedPositionId = positionId,
                    AssignedEmployeeId = employee.Id
                });
        }
        else
        {
            db.WorkflowTasks.Add(new WorkflowTask
            {
                WorkflowInstanceId = instance.Id,
                WorkflowStepId = step.Id,
                AssignedPositionId = positionId,
                AssignedEmployeeId = employees.FirstOrDefault()?.Id
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorkflowStep?> ResolveTransitionAsync(int stepId, string action, ICollection<WorkflowStep> steps)
    {
        var transition = await db.WorkflowTransitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowStepId == stepId && x.Action == action);

        return transition?.ToStepId is int id
            ? steps.FirstOrDefault(x => x.Id == id)
            : steps.OrderBy(x => x.SortOrder).FirstOrDefault(x => x.SortOrder > steps.First(s => s.Id == stepId).SortOrder);
    }

    private int? ResolveEmployeePosition(int employeeId, int? preferredPositionId)
    {
        if (preferredPositionId.HasValue) return preferredPositionId;
        return null;
    }

    private static string NormalizeAction(string action) => action.Trim().ToLowerInvariant() switch
    {
        "approve" or "تایید" or "تأیید" => "Approve",
        "reject" or "رد" => "Reject",
        "return" or "برگشت" => "Return",
        _ => throw new InvalidOperationException("عملیات گردش کار نامعتبر است.")
    };

    public static string ActionTitle(string action) => NormalizeAction(action) switch
    {
        "Approve" => "تأیید",
        "Reject" => "رد",
        "Return" => "برگشت",
        _ => action
    };
}
