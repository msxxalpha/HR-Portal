using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class WorkflowService(HRPortalDbContext db, AuditService audit)
{
    public async Task<List<WorkflowDefinition>> GetDefinitionsAsync() =>
        await db.WorkflowDefinitions
            .Where(x => !x.IsRuntimeInstance)
            .Include(x => x.Steps)
            .ThenInclude(x => x.OrganizationNode)
            .Include(x => x.Steps)
            .ThenInclude(x => x.Fields)
            .OrderBy(x => x.Title)
            .ToListAsync();

    public async Task<WorkflowDefinition?> GetDefinitionAsync(int id) =>
        await db.WorkflowDefinitions
            .Include(x => x.Steps)
            .ThenInclude(x => x.OrganizationNode)
            .Include(x => x.Steps)
            .ThenInclude(x => x.Fields)
            .Include(x => x.Steps)
            .ThenInclude(x => x.OutgoingTransitions)
            .ThenInclude(x => x.ToStep)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsRuntimeInstance);

    public async Task<WorkflowDefinition?> GetByCodeAsync(string code) =>
        await db.WorkflowDefinitions
            .Include(x => x.Steps.OrderBy(s => s.SortOrder))
            .Where(x => x.Code == code && x.IsActive && !x.IsRuntimeInstance).OrderByDescending(x => x.Version).FirstOrDefaultAsync();

    public async Task<int> SaveDefinitionAsync(WorkflowDefinitionEditModel model)
    {
        var code = model.Code.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(model.Title))
            throw new InvalidOperationException("کد و عنوان گردش کار الزامی است.");
        if (model.Id == 0 && await db.WorkflowDefinitions.AnyAsync(x => x.Code == code && !x.IsRuntimeInstance))
            throw new InvalidOperationException("کد گردش کار قبلاً استفاده شده است.");

        WorkflowDefinition entity;
        if (model.Id == 0)
        {
            entity = new WorkflowDefinition { Code = code, Version = 1 };
            db.WorkflowDefinitions.Add(entity);
        }
        else
        {
            var current = await db.WorkflowDefinitions
                .Where(x => !x.IsRuntimeInstance)
                .Include(x => x.Steps).ThenInclude(x => x.Fields)
                .Include(x => x.Steps).ThenInclude(x => x.OutgoingTransitions)
                .FirstOrDefaultAsync(x => x.Id == model.Id)
                ?? throw new InvalidOperationException("گردش کار یافت نشد.");

            if (await db.WorkflowInstances.AnyAsync(x => x.WorkflowDefinitionId == current.Id))
            {
                current.IsActive = false;
                current.UpdatedAt = DateTime.UtcNow;
                var maxVersion = await db.WorkflowDefinitions.Where(x => x.Code == code && !x.IsRuntimeInstance)
                    .Select(x => (int?)x.Version).MaxAsync() ?? 1;
                entity = new WorkflowDefinition
                {
                    Code = code, Title = model.Title.Trim(), Description = model.Description?.Trim(),
                    IsActive = model.IsActive, Version = maxVersion + 1,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                };
                db.WorkflowDefinitions.Add(entity);
                await db.SaveChangesAsync();

                var stepMap = new Dictionary<int, WorkflowStep>();
                foreach (var source in current.Steps.OrderBy(x => x.SortOrder))
                {
                    var copy = new WorkflowStep
                    {
                        WorkflowDefinitionId = entity.Id, Code = source.Code, Title = source.Title,
                        SortOrder = source.SortOrder, AssignmentType = source.AssignmentType,
                        HierarchyStopRankType = source.HierarchyStopRankType, OrganizationNodeId = source.OrganizationNodeId, AssignmentMode = source.AssignmentMode,
                        AllowApprove = source.AllowApprove, AllowReject = source.AllowReject, AllowReturn = source.AllowReturn,
                        RequireCommentOnReject = source.RequireCommentOnReject,
                        RequireCommentOnReturn = source.RequireCommentOnReturn, IsFinalStep = source.IsFinalStep
                    };
                    db.WorkflowSteps.Add(copy);
                    stepMap[source.Id] = copy;
                }
                await db.SaveChangesAsync();

                foreach (var source in current.Steps)
                foreach (var field in source.Fields)
                {
                    db.WorkflowStepFields.Add(new WorkflowStepField
                    {
                        WorkflowStepId = stepMap[source.Id].Id, Code = field.Code, Title = field.Title,
                        FieldType = field.FieldType, Options = field.Options, HelpText = field.HelpText,
                        SortOrder = field.SortOrder, IsRequired = field.IsRequired, MaxLength = field.MaxLength
                    });
                }
                foreach (var source in current.Steps)
                foreach (var transition in source.OutgoingTransitions)
                {
                    db.WorkflowTransitions.Add(new WorkflowTransition
                    {
                        WorkflowStepId = stepMap[source.Id].Id,
                        ToStepId = transition.ToStepId.HasValue && stepMap.TryGetValue(transition.ToStepId.Value, out var target)
                            ? target.Id : null,
                        Action = transition.Action, Title = transition.Title
                    });
                }
                await db.SaveChangesAsync();
            }
            else
            {
                entity = current;
                var otherMax = await db.WorkflowDefinitions.Where(x => x.Code == code && x.Id != current.Id && !x.IsRuntimeInstance)
                    .Select(x => (int?)x.Version).MaxAsync() ?? 0;
                entity.Version = Math.Max(entity.Version, otherMax + 1);
            }
        }

        entity.Code = code;
        entity.Title = model.Title.Trim();
        entity.Description = model.Description?.Trim();
        entity.IsActive = model.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        if (entity.IsActive)
        {
            var otherActive = await db.WorkflowDefinitions
                .Where(x => x.Code == code && x.Id != entity.Id && x.IsActive).ToListAsync();
            foreach (var older in otherActive) older.IsActive = false;
        }
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task DeleteDefinitionAsync(int id)
    {
        var definition = await db.WorkflowDefinitions
            .Include(x => x.Steps).ThenInclude(x => x.Fields)
            .Include(x => x.Steps).ThenInclude(x => x.OutgoingTransitions)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (definition is null) return;
        if (definition.IsRuntimeInstance)
            throw new InvalidOperationException("نسخه اجرایی گردش کار مستقیماً قابل حذف نیست.");
        if (await db.WorkflowInstances.AnyAsync(x => x.WorkflowDefinitionId == id) ||
            await db.WorkflowTasks.AnyAsync(x => x.WorkflowStep.WorkflowDefinitionId == id) ||
            await db.WorkflowHistory.AnyAsync(x => x.WorkflowStep != null && x.WorkflowStep.WorkflowDefinitionId == id) ||
            await db.WorkflowFieldValues.AnyAsync(x => x.WorkflowStepField.WorkflowStep.WorkflowDefinitionId == id))
            throw new InvalidOperationException("این گردش کار در درخواست‌ها یا سوابق استفاده شده است و قابل حذف نیست. فقط گردش کارهای استفاده‌نشده قابل حذف هستند.");
        db.WorkflowDefinitions.Remove(definition);
        await db.SaveChangesAsync();
    }

    public async Task SaveStepAsync(WorkflowStepEditModel model)
    {
        var definition = await db.WorkflowDefinitions.FindAsync(model.WorkflowDefinitionId)
            ?? throw new InvalidOperationException("گردش کار یافت نشد.");
        if (definition.IsRuntimeInstance)
            throw new InvalidOperationException("نسخه اجرایی یک درخواست قابل ویرایش نیست.");

        if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Title))
            throw new InvalidOperationException("کد و عنوان مرحله الزامی است.");

        var assignmentType = model.AssignmentType == "Hierarchy" ? "Hierarchy" : "Position";
        if (assignmentType == "Position" && !model.OrganizationNodeId.HasValue)
            throw new InvalidOperationException("برای مرحله جایگاه ثابت، انتخاب جایگاه سازمانی الزامی است.");
        if (assignmentType == "Hierarchy" && model.HierarchyStopRankType is not ("معاونت" or "مدیریت" or "ریاست" or "سرپرستی"))
            throw new InvalidOperationException("رده توقف مسیر پویا معتبر نیست.");
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
        entity.AssignmentType = assignmentType;
        entity.HierarchyStopRankType = model.HierarchyStopRankType;
        entity.OrganizationNodeId = assignmentType == "Position" ? model.OrganizationNodeId : null;
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
        if (await db.WorkflowFieldValues.AnyAsync(x => x.WorkflowStepId == id))
            throw new InvalidOperationException("این مرحله در درخواست‌های ثبت‌شده اطلاعات دارد و برای حفظ سوابق قابل حذف نیست.");
        db.WorkflowSteps.Remove(step);
        await db.SaveChangesAsync();
    }
    public async Task SaveStepFieldAsync(WorkflowStepFieldEditModel model)
    {
        var step = await db.WorkflowSteps.Include(x => x.WorkflowDefinition)
            .FirstOrDefaultAsync(x => x.Id == model.WorkflowStepId)
            ?? throw new InvalidOperationException("مرحله یافت نشد.");
        var code = model.Code.Trim();
        var title = model.Title.Trim();
        var type = NormalizeFieldType(model.FieldType);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("کد و عنوان فیلد الزامی است.");
        if (code.Length > 100 || title.Length > 200)
            throw new InvalidOperationException("طول کد یا عنوان فیلد بیش از حد مجاز است.");
        if (await db.WorkflowStepFields.AnyAsync(x => x.WorkflowStepId == step.Id && x.Code == code && x.Id != model.Id))
            throw new InvalidOperationException("کد این فیلد در مرحله تکراری است.");
        if (type == "Select" && string.IsNullOrWhiteSpace(model.Options))
            throw new InvalidOperationException("برای فیلد انتخابی، گزینه‌ها را وارد کنید.");
        if (model.MaxLength is < 1 or > 4000)
            throw new InvalidOperationException("حداکثر طول باید بین ۱ تا ۴۰۰۰ باشد.");

        WorkflowStepField field;
        if (model.Id == 0)
        {
            field = new WorkflowStepField { WorkflowStepId = step.Id };
            db.WorkflowStepFields.Add(field);
        }
        else
        {
            field = await db.WorkflowStepFields.FirstOrDefaultAsync(x => x.Id == model.Id && x.WorkflowStepId == step.Id)
                ?? throw new InvalidOperationException("فیلد مرحله یافت نشد.");
        }

        field.Code = code;
        field.Title = title;
        field.FieldType = type;
        field.Options = type == "Select" ? model.Options?.Trim() : null;
        field.HelpText = model.HelpText?.Trim();
        field.SortOrder = model.SortOrder;
        field.IsRequired = model.IsRequired;
        field.MaxLength = model.MaxLength;
        step.WorkflowDefinition.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeleteStepFieldAsync(int id)
    {
        var field = await db.WorkflowStepFields.FindAsync(id);
        if (field is null) return;
        if (await db.WorkflowFieldValues.AnyAsync(x => x.WorkflowStepFieldId == id))
            throw new InvalidOperationException("این فیلد در درخواست‌های ثبت‌شده استفاده شده و برای حفظ سوابق قابل حذف نیست.");
        db.WorkflowStepFields.Remove(field);
        await db.SaveChangesAsync();
    }

    public async Task<List<WorkflowTaskFieldGroup>> GetTaskFieldGroupsAsync(int taskId, int employeeId)
    {
        var task = await GetTaskAsync(taskId, employeeId)
            ?? throw new InvalidOperationException("این وظیفه در دسترس شما نیست.");
        var steps = await db.WorkflowSteps.AsNoTracking()
            .Where(x => x.WorkflowDefinitionId == task.WorkflowInstance.WorkflowDefinitionId)
            .OrderBy(x => x.SortOrder).ToListAsync();
        var fields = await db.WorkflowStepFields.AsNoTracking()
            .Where(x => steps.Select(s => s.Id).Contains(x.WorkflowStepId))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        var values = await db.WorkflowFieldValues.AsNoTracking()
            .Where(x => x.WorkflowInstanceId == task.WorkflowInstanceId)
            .OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .ToListAsync();
        var groups = new List<WorkflowTaskFieldGroup>();
        foreach (var step in steps)
        {
            var allStepFields = fields.Where(x => x.WorkflowStepId == step.Id).ToList();
            var isCurrent = step.Id == task.WorkflowStepId;
            var stepFields = isCurrent
                ? allStepFields
                : allStepFields.Where(f => values.Any(v => v.WorkflowStepFieldId == f.Id)).ToList();
            if (stepFields.Count == 0) continue;
            groups.Add(new WorkflowTaskFieldGroup
            {
                WorkflowStepId = step.Id,
                StepTitle = step.Title,
                IsCurrentStep = isCurrent,
                Fields = stepFields.Select(f =>
                {
                    var value = values.FirstOrDefault(v => v.WorkflowStepFieldId == f.Id);
                    return new WorkflowTaskFieldItem
                    {
                        FieldDefinitionId = f.Id,
                        Code = f.Code,
                        Title = value?.FieldTitle ?? f.Title,
                        FieldType = value?.FieldType ?? f.FieldType,
                        Options = f.Options,
                        HelpText = f.HelpText,
                        MaxLength = f.MaxLength,
                        IsRequired = f.IsRequired,
                        Value = value?.Value
                    };
                }).ToList()
            });
        }
        return groups;
    }

    private static string NormalizeFieldType(string? type) => type?.Trim() switch
    {
        "Text" => "Text",
        "TextArea" => "TextArea",
        "Number" => "Number",
        "Date" => "Date",
        "Checkbox" => "Checkbox",
        "Select" => "Select",
        _ => throw new InvalidOperationException("نوع فیلد نامعتبر است.")
    };

    private async Task SaveTaskFieldValuesAsync(
        WorkflowTask task, int actorEmployeeId, IDictionary<string, string?>? submitted, bool validateRequired,
        CancellationToken cancellationToken)
    {
        var fields = await db.WorkflowStepFields
            .Where(x => x.WorkflowStepId == task.WorkflowStepId)
            .OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
        var input = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (submitted is not null)
            foreach (var pair in submitted)
                input[pair.Key] = pair.Value;

        var allowedCodes = fields.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (input.Keys.Any(k => !allowedCodes.Contains(k)))
            throw new InvalidOperationException("درخواست شامل فیلدی است که متعلق به این مرحله نیست.");

        foreach (var field in fields)
        {
            input.TryGetValue(field.Code, out var raw);
            var value = field.FieldType == "Checkbox"
                ? (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "on" ? "true" : "false")
                : raw?.Trim();
            if (validateRequired && field.IsRequired &&
                (string.IsNullOrWhiteSpace(value) || (field.FieldType == "Checkbox" && value != "true")))
                throw new InvalidOperationException($"تکمیل صحیح فیلد «{field.Title}» الزامی است.");
            if (value is { Length: > 4000 })
                throw new InvalidOperationException($"مقدار فیلد «{field.Title}» بیش از حد مجاز است.");
            if (!string.IsNullOrEmpty(value))
            {
                if (field.MaxLength.HasValue && value.Length > field.MaxLength.Value)
                    throw new InvalidOperationException($"طول مقدار فیلد «{field.Title}» بیش از {field.MaxLength.Value} نویسه است.");
                if (field.FieldType == "Number" && !decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
                    throw new InvalidOperationException($"مقدار فیلد «{field.Title}» باید عددی باشد.");
                if (field.FieldType == "Date" && !DateOnly.TryParseExact(value, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                    throw new InvalidOperationException($"تاریخ فیلد «{field.Title}» معتبر نیست.");
                if (field.FieldType == "Select" &&
                    !(field.Options ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(value))
                    throw new InvalidOperationException($"گزینه انتخاب‌شده برای فیلد «{field.Title}» معتبر نیست.");
            }

            // Append a value snapshot rather than overwrite it. This preserves the
            // data entered before a return/rework cycle for later audit.
            db.WorkflowFieldValues.Add(new WorkflowFieldValue
            {
                WorkflowInstanceId = task.WorkflowInstanceId,
                WorkflowStepId = task.WorkflowStepId,
                WorkflowStepFieldId = field.Id,
                FieldCode = field.Code,
                FieldTitle = field.Title,
                FieldType = field.FieldType,
                Value = value,
                UpdatedByEmployeeId = actorEmployeeId,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }


    public async Task<WorkflowInstance> StartAsync(
        string definitionCode,
        string entityType,
        string entityId,
        int requesterEmployeeId,
        CancellationToken cancellationToken = default,
        int? subjectEmployeeId = null)
    {
        var definition = await db.WorkflowDefinitions
            .Include(x => x.Steps.OrderBy(s => s.SortOrder)).ThenInclude(x => x.Fields)
            .Include(x => x.Steps).ThenInclude(x => x.OutgoingTransitions)
            .Where(x => x.Code == definitionCode && x.IsActive && !x.IsRuntimeInstance)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("گردش کار فعال با این کد یافت نشد.");

        var requester = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == requesterEmployeeId && x.Status == "فعال", cancellationToken)
            ?? throw new InvalidOperationException("کارمند درخواست‌کننده یافت نشد.");

        if (definition.Steps.Any(x => x.AssignmentType == "Hierarchy"))
        {
            if (!subjectEmployeeId.HasValue)
                throw new InvalidOperationException("برای شروع گردش کار جایگاه‌محور، انتخاب کارمند موضوع درخواست الزامی است.");
            definition = await BuildRuntimeDefinitionAsync(definition, subjectEmployeeId.Value, cancellationToken);
        }

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

    private async Task<WorkflowDefinition> BuildRuntimeDefinitionAsync(
        WorkflowDefinition source, int subjectEmployeeId, CancellationToken cancellationToken)
    {
        var hierarchyTemplates = source.Steps.Where(x => x.AssignmentType == "Hierarchy").ToList();
        if (hierarchyTemplates.Count != 1)
            throw new InvalidOperationException("هر گردش کار می‌تواند دقیقاً یک مرحله زنجیره جایگاه‌های سازمانی داشته باشد.");

        var template = hierarchyTemplates[0];
        var route = await BuildHierarchyRouteAsync(subjectEmployeeId, template.HierarchyStopRankType, cancellationToken);
        if (route.Count == 0)
            throw new InvalidOperationException("برای کارمند انتخاب‌شده مسیر سازمانی پویا پیدا نشد.");

        var runtime = new WorkflowDefinition
        {
            Code = $"__runtime_{source.Id}_{Guid.NewGuid():N}",
            Title = source.Title,
            Description = source.Description,
            IsActive = false,
            IsRuntimeInstance = true,
            SourceDefinitionId = source.Id,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.WorkflowDefinitions.Add(runtime);
        await db.SaveChangesAsync(cancellationToken);

        var map = new Dictionary<int, List<WorkflowStep>>();
        var allRuntimeSteps = new List<WorkflowStep>();
        var order = 10;
        foreach (var original in source.Steps.OrderBy(x => x.SortOrder))
        {
            var clones = new List<WorkflowStep>();
            if (original.AssignmentType == "Hierarchy")
            {
                for (var i = 0; i < route.Count; i++)
                {
                    var position = route[i];
                    clones.Add(CloneStep(original, runtime.Id, $"{TrimCode(original.Code, 75)}_H{i + 1}",
                        $"{original.Title} — {position.Title}", order, position.Id, false));
                    order += 10;
                }
            }
            else
            {
                clones.Add(CloneStep(original, runtime.Id, original.Code, original.Title, order,
                    original.OrganizationNodeId, original.IsFinalStep));
                order += 10;
            }

            map[original.Id] = clones;
            allRuntimeSteps.AddRange(clones);
        }

        await db.WorkflowSteps.AddRangeAsync(allRuntimeSteps, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var original in source.Steps)
        {
            foreach (var clone in map[original.Id])
            {
                foreach (var field in original.Fields.OrderBy(x => x.SortOrder))
                {
                    db.WorkflowStepFields.Add(new WorkflowStepField
                    {
                        WorkflowStepId = clone.Id,
                        Code = field.Code,
                        Title = field.Title,
                        FieldType = field.FieldType,
                        Options = field.Options,
                        HelpText = field.HelpText,
                        SortOrder = field.SortOrder,
                        IsRequired = field.IsRequired,
                        MaxLength = field.MaxLength
                    });
                }
            }

            foreach (var clone in map[original.Id])
            {
                var isHierarchyClone = original.AssignmentType == "Hierarchy";
                var cloneIndex = map[original.Id].IndexOf(clone);
                foreach (var transition in original.OutgoingTransitions)
                {
                    int? targetId;
                    if (isHierarchyClone && transition.Action == "Approve" && cloneIndex < map[original.Id].Count - 1)
                    {
                        targetId = map[original.Id][cloneIndex + 1].Id;
                    }
                    else
                    {
                        targetId = transition.ToStepId.HasValue && map.TryGetValue(transition.ToStepId.Value, out var targets)
                            ? targets.FirstOrDefault()?.Id
                            : null;
                    }

                    db.WorkflowTransitions.Add(new WorkflowTransition
                    {
                        WorkflowStepId = clone.Id,
                        ToStepId = targetId,
                        Action = transition.Action,
                        Title = transition.Title
                    });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        runtime.Steps = allRuntimeSteps;
        return runtime;
    }

    private async Task<List<OrganizationNode>> BuildHierarchyRouteAsync(
        int subjectEmployeeId, string stopRankType, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subjectEmployeeId && x.Status == "فعال", cancellationToken)
            ?? throw new InvalidOperationException("کارمند موضوع گردش کار یافت نشد.");

        var assignedPositionId = employee.OrganizationSectionId
            ?? employee.OrganizationDepartmentId
            ?? employee.OrganizationUnitId;
        if (!assignedPositionId.HasValue)
            throw new InvalidOperationException("برای کارمند انتخاب‌شده جایگاه سازمانی ثبت نشده است.");

        var oldPosition = await db.OrganizationNodes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == assignedPositionId.Value, cancellationToken)
            ?? throw new InvalidOperationException("جایگاه فعلی کارمند یافت نشد.");

        var revision = await db.OrganizationStructureRevisions.AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("نسخه نهایی فعال ساختار سازمانی یافت نشد.");

        var nodes = await db.OrganizationNodes.AsNoTracking()
            .Where(x => x.OrganizationStructureRevisionId == revision.Id && x.IsActive)
            .ToListAsync(cancellationToken);
        var byId = nodes.ToDictionary(x => x.Id);
        var current = nodes.FirstOrDefault(x => x.Code == oldPosition.Code)
            ?? throw new InvalidOperationException("جایگاه کارمند در نسخه فعال ساختار سازمانی یافت نشد.");

        if (!current.ParentId.HasValue)
            throw new InvalidOperationException("جایگاه کارمند والد سازمانی ندارد؛ مسیر بالادستی ساخته نشد.");

        var route = new List<OrganizationNode>();
        var stopFound = false;
        var guard = 0;
        current = byId.GetValueOrDefault(current.ParentId.Value)!;
        while (current is not null && guard++ < 1000)
        {
            route.Add(current);
            if (current.RankType == stopRankType)
            {
                stopFound = true;
                break;
            }
            current = current.ParentId.HasValue ? byId.GetValueOrDefault(current.ParentId.Value) : null;
        }

        if (!stopFound)
            throw new InvalidOperationException($"در زنجیره بالادستی کارمند، رده توقف «{stopRankType}» پیدا نشد؛ ساختار سازمانی یا تنظیم مرحله را بررسی کنید.");

        return route;
    }

    private static WorkflowStep CloneStep(
        WorkflowStep source, int runtimeDefinitionId, string code, string title,
        int sortOrder, int? organizationNodeId, bool isFinalStep) => new()
    {
        WorkflowDefinitionId = runtimeDefinitionId,
        Code = code.Length > 100 ? code[..100] : code,
        Title = title.Length > 200 ? title[..200] : title,
        SortOrder = sortOrder,
        AssignmentType = "Position",
        HierarchyStopRankType = source.HierarchyStopRankType,
        OrganizationNodeId = organizationNodeId,
        AssignmentMode = source.AssignmentMode,
        AllowApprove = source.AllowApprove,
        AllowReject = source.AllowReject,
        AllowReturn = source.AllowReturn,
        RequireCommentOnReject = source.RequireCommentOnReject,
        RequireCommentOnReturn = source.RequireCommentOnReturn,
        IsFinalStep = isFinalStep
    };

    private static string TrimCode(string code, int maxLength) =>
        code.Length <= maxLength ? code : code[..maxLength];

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

    public async Task<List<WorkflowInboxRow>> GetMyRequestsAsync(int employeeId)
    {
        return await db.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.WorkflowDefinition)
            .Include(x => x.CurrentStep).ThenInclude(x => x.OrganizationNode)
            .Where(x => x.RequesterEmployeeId == employeeId)
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new WorkflowInboxRow
            {
                WorkflowInstanceId = x.Id,
                WorkflowTitle = x.WorkflowDefinition.Title,
                EntityType = x.EntityType,
                EntityId = x.EntityId,
                StepTitle = x.CurrentStep == null ? "پایان گردش" : x.CurrentStep.Title,
                PositionTitle = x.CurrentStep == null || x.CurrentStep.OrganizationNode == null ? "" : x.CurrentStep.OrganizationNode.Title,
                Status = x.Status,
                CreatedAt = x.StartedAt
            })
            .ToListAsync();
    }

    public async Task<WorkflowInstance?> GetInstanceAsync(int id, int employeeId)
    {
        return await db.WorkflowInstances
            .Include(x => x.WorkflowDefinition)
            .Include(x => x.RequesterEmployee)
            .Include(x => x.CurrentStep).ThenInclude(x => x.OrganizationNode)
            .FirstOrDefaultAsync(x => x.Id == id && x.RequesterEmployeeId == employeeId);
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

    public async Task CompleteTaskAsync(int taskId, int actorEmployeeId, string action, string? comment, IDictionary<string, string?>? values = null)
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

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (action == "SaveDraft")
        {
            await SaveTaskFieldValuesAsync(task, actorEmployeeId, values, false, CancellationToken.None);
            db.WorkflowHistory.Add(new WorkflowHistory
            {
                WorkflowInstanceId = instance.Id,
                WorkflowStepId = task.WorkflowStepId,
                FromStatus = instance.Status,
                ToStatus = instance.Status,
                Action = "SaveDraft",
                ActorEmployeeId = actorEmployeeId,
                ActorPositionId = ResolveEmployeePosition(actorEmployeeId, task.AssignedPositionId),
                Comment = comment?.Trim()
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await audit.WriteAsync("ذخیره پیش‌نویس فرم گردش کار", nameof(WorkflowTask), task.Id.ToString(), "SaveDraft");
            return;
        }
        await SaveTaskFieldValuesAsync(task, actorEmployeeId, values, action == "Approve", CancellationToken.None);
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

        var pendingSiblings = instance.Tasks
            .Where(x => x.WorkflowStepId == task.WorkflowStepId && x.Id != task.Id && x.Status == "Pending")
            .ToList();

        // In All mode, each approver must complete their own task. A rejection/return
        // resolves the stage immediately; otherwise only the last approval advances it.
        if (task.WorkflowStep.AssignmentMode == "All" && action == "Approve" && pendingSiblings.Count > 0)
        {
            instance.Status = "InProgress";
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            await audit.WriteAsync("اقدام مرحله گردش کار", nameof(WorkflowTask), task.Id.ToString(), action);
            return;
        }

        foreach (var sibling in pendingSiblings)
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
            var next = await ResolveTransitionAsync(task.WorkflowStepId, "Reject", instance.WorkflowDefinition.Steps);
            if (next is null)
            {
                instance.Status = "Rejected";
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
        await transaction.CommitAsync();
        await audit.WriteAsync("اقدام در گردش کار", nameof(WorkflowTask), task.Id.ToString(), action);
    }

    private async Task CreateTasksAsync(WorkflowInstance instance, WorkflowStep step, CancellationToken cancellationToken)
    {
        if (!step.OrganizationNodeId.HasValue)
            throw new InvalidOperationException($"برای مرحله «{step.Title}» جایگاه سازمانی تعیین نشده است.");

        var configuredPosition = await db.OrganizationNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == step.OrganizationNodeId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"جایگاه تنظیم‌شده برای مرحله «{step.Title}» یافت نشد.");

        var currentRevision = await db.OrganizationStructureRevisions
            .AsNoTracking()
            .Where(x => x.IsFinalized && x.EffectiveDate <= DateTime.Today)
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("نسخه نهایی فعال ساختار سازمانی یافت نشد.");

        var position = await db.OrganizationNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationStructureRevisionId == currentRevision.Id &&
                                       x.Code == configuredPosition.Code &&
                                       x.IsActive, cancellationToken)
            ?? throw new InvalidOperationException($"جایگاه «{configuredPosition.Title}» در نسخه فعال ساختار سازمانی یافت نشد.");

        var positionId = position.Id;
        var employees = await db.Employees
            .AsNoTracking()
            .Where(e => e.Status == "فعال" &&
                (e.OrganizationUnitId == positionId ||
                 e.OrganizationDepartmentId == positionId ||
                 e.OrganizationSectionId == positionId))
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);

        if (employees.Count == 0)
        {
            db.WorkflowTasks.Add(new WorkflowTask
            {
                WorkflowInstanceId = instance.Id,
                WorkflowStepId = step.Id,
                AssignedPositionId = positionId
            });
        }
        else
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

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorkflowStep?> ResolveTransitionAsync(int stepId, string action, ICollection<WorkflowStep> steps)
    {
        var transition = await db.WorkflowTransitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowStepId == stepId && x.Action == action);

        if (transition?.ToStepId is int id)
            return steps.FirstOrDefault(x => x.Id == id);

        if (action == "Approve")
        {
            var current = steps.FirstOrDefault(x => x.Id == stepId);
            return current is null
                ? null
                : steps.OrderBy(x => x.SortOrder).FirstOrDefault(x => x.SortOrder > current.SortOrder);
        }

        return null;
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
        "savedraft" or "ذخیره پیش‌نویس" or "ذخیره پیش نویس" => "SaveDraft",
        _ => throw new InvalidOperationException("عملیات گردش کار نامعتبر است.")
    };

    public static string ActionTitle(string action) => NormalizeAction(action) switch
    {
        "Approve" => "تأیید",
        "Reject" => "رد",
        "Return" => "برگشت",
        "SaveDraft" => "ذخیره پیش‌نویس",
        _ => action
    };
}
