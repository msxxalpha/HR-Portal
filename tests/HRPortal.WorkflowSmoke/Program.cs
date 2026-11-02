using HRPortal.Services;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// Multi-assignee position: every active listed employee can act on the same
// position task, duplicates and malformed tokens cannot create extra identities.
var shared = WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds("101, 102,101,broken,-5", 999);
Check(shared.SequenceEqual(new[] { 101, 102 }), "Explicit multi-assignee list was not normalized.");
Check(WorkflowPositionAssignmentResolver.IsPositionAssignee(101, true, "101,102", 999),
    "First explicit position assignee was denied.");
Check(WorkflowPositionAssignmentResolver.IsPositionAssignee(102, true, "101,102", 999),
    "Second explicit position assignee was denied.");
Check(!WorkflowPositionAssignmentResolver.IsPositionAssignee(999, true, "101,102", 999),
    "Stale position holder must not override a non-empty explicit assignee list.");
Check(!WorkflowPositionAssignmentResolver.IsPositionAssignee(101, false, "101,102", null),
    "Inactive employees must never be position assignees.");

// Single-holder fallback is used only when the explicit CSV has no valid IDs.
Check(WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds(null, 201).SequenceEqual(new[] { 201 }),
    "Position-holder fallback failed.");
Check(WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds("invalid,0,-1", 201).SequenceEqual(new[] { 201 }),
    "Invalid CSV tokens should fall back to the designated holder.");
Check(WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds("301,302", null).SequenceEqual(new[] { 301, 302 }),
    "A position with multiple explicit assignees was not resolved.");
Check(WorkflowPositionAssignmentResolver.GetAssignedEmployeeIds("301,301,302", null).SequenceEqual(new[] { 301, 302 }),
    "Duplicate employee IDs were not removed.");

// Employee-routed tasks are deliberately person-specific. Being a position
// holder or sharing the same department must not grant access to these tasks.
Check(WorkflowPositionAssignmentResolver.IsExplicitEmployeeAssignee(401, 401, true),
    "The explicitly assigned active employee was denied.");
Check(!WorkflowPositionAssignmentResolver.IsExplicitEmployeeAssignee(402, 401, true),
    "A colleague was granted an employee-specific task.");
Check(!WorkflowPositionAssignmentResolver.IsExplicitEmployeeAssignee(401, 401, false),
    "An inactive employee retained access to an employee-specific task.");
Check(!WorkflowPositionAssignmentResolver.IsExplicitEmployeeAssignee(0, 0, true),
    "An invalid employee identity was accepted.");



Check(WorkflowPositionAssignmentResolver.CanViewInstance(
        isRequester: true, hasActedPreviously: false, isCurrentAssigneeOfPendingTask: false),
    "A requester must be able to view their own request.");
Check(WorkflowPositionAssignmentResolver.CanViewInstance(
        isRequester: false, hasActedPreviously: true, isCurrentAssigneeOfPendingTask: false),
    "A previous actor must retain read access to the workflow history.");
Check(WorkflowPositionAssignmentResolver.CanViewInstance(
        isRequester: false, hasActedPreviously: false, isCurrentAssigneeOfPendingTask: true),
    "The current assignee of a pending task must be able to view the instance.");
Check(!WorkflowPositionAssignmentResolver.CanViewInstance(
        isRequester: false, hasActedPreviously: false, isCurrentAssigneeOfPendingTask: false),
    "An unrelated employee must not view another employee's workflow instance.");



var activeHealthEmployees = new[]
{
    new HRPortal.Models.Employee { Id = 3, Status = "فعال" },
    new HRPortal.Models.Employee { Id = 4, Status = "فعال" }
};
var healthPosition = new HRPortal.Models.OrganizationNode
{
    Id = 30, Code = "ORG-OPS", Title = "عملیات",
    AssignedEmployeeIdsCsv = "3,4", IsActive = true
};
var healthPositions = new Dictionary<string, HRPortal.Models.OrganizationNode>(
    StringComparer.OrdinalIgnoreCase) { ["ORG-OPS"] = healthPosition };
var healthTask = new HRPortal.Models.WorkflowTask
{
    Id = 50, Status = "Pending", AssignedPositionId = 9,
    AssignedPosition = new HRPortal.Models.OrganizationNode { Id = 9, Code = "ORG-OPS" },
    WorkflowStep = new HRPortal.Models.WorkflowStep()
};
Check(WorkflowTaskAssignmentHealthResolver.GetIssueCode(
        healthTask, healthPositions, activeHealthEmployees) is null,
    "A position task with valid active assignees must not appear in the unassigned report.");
Check(WorkflowTaskAssignmentHealthResolver.GetIssueCode(
        healthTask, new Dictionary<string, HRPortal.Models.OrganizationNode>(),
        activeHealthEmployees) == "PositionMissing",
    "A position missing from the current finalized revision must be flagged.");
healthPosition.AssignedEmployeeIdsCsv = "3,6";
Check(WorkflowTaskAssignmentHealthResolver.GetIssueCode(
        healthTask, healthPositions, activeHealthEmployees) == "InvalidAssignment",
    "A mixed active/inactive assignment list must be flagged for repair.");
healthPosition.AssignedEmployeeIdsCsv = "6";
Check(WorkflowTaskAssignmentHealthResolver.GetIssueCode(
        healthTask, healthPositions, activeHealthEmployees) == "NoActiveHolder",
    "A position with no active assignee must be flagged.");
var directHealthTask = new HRPortal.Models.WorkflowTask { Status = "Pending", AssignedEmployeeId = 99 };
Check(WorkflowTaskAssignmentHealthResolver.GetIssueCode(
        directHealthTask, healthPositions, activeHealthEmployees) == "EmployeeMissing",
    "A missing or inactive direct assignee must be flagged.");
directHealthTask.AssignedEmployeeId = 3;
Check(WorkflowTaskAssignmentHealthResolver.GetIssueCode(
        directHealthTask, healthPositions, activeHealthEmployees) is null,
    "A valid direct assignment must not be eligible for managerial reassignment.");

var reportRows = new[]
{
    new HRPortal.Models.UnassignedWorkflowTaskRow
    {
        TaskId = 1, WorkflowId = 10, WorkflowTitle = "تمدید قرارداد",
        EntityType = "ContractRenewal", EntityId = "CR-100",
        RequesterName = "کارمند الف", StepTitle = "حراست",
        PositionCode = "SEC-01", PositionTitle = "حراست",
        CreatedAt = new DateTime(2026, 10, 10, 9, 0, 0),
        ReasonCode = "NoActiveHolder", Reason = "بدون متصدی"
    },
    new HRPortal.Models.UnassignedWorkflowTaskRow
    {
        TaskId = 2, WorkflowId = 20, WorkflowTitle = "درخواست دیگر",
        EntityType = "Other", EntityId = "REQ-200",
        RequesterName = "کارمند ب", StepTitle = "مدیر منابع انسانی",
        PositionCode = "HR-02", PositionTitle = "منابع انسانی",
        CreatedAt = new DateTime(2026, 10, 12, 9, 0, 0),
        ReasonCode = "PositionMissing", Reason = "جایگاه ناموجود"
    }
};
var filteredReport = UnassignedWorkflowTaskFilter.Apply(
    reportRows,
    query: "CR-100",
    reasonCode: "NoActiveHolder",
    workflowId: 10,
    step: "حراست",
    position: "sec-01",
    createdFrom: new DateTime(2026, 10, 10),
    createdTo: new DateTime(2026, 10, 10));
Check(filteredReport.Count == 1 && filteredReport[0].TaskId == 1,
    "Combined report filters for request, reason, workflow, step, position and inclusive date range failed.");
Check(UnassignedWorkflowTaskFilter.Apply(
        reportRows, createdFrom: new DateTime(2026, 10, 11)).Select(x => x.TaskId).SequenceEqual(new[] { 2 }),
    "Created-from date filter failed.");
Check(UnassignedWorkflowTaskFilter.Apply(
        reportRows, createdTo: new DateTime(2026, 10, 10)).Select(x => x.TaskId).SequenceEqual(new[] { 1 }),
    "Created-to date filter failed.");

var selectedRevision = WorkflowPositionAssignmentResolver.SelectCurrentRevisionId(
    new[]
    {
        (Id: 10, EffectiveDate: new DateTime(2026, 1, 1), IsFinalized: true),
        (Id: 11, EffectiveDate: new DateTime(2026, 3, 1), IsFinalized: true),
        (Id: 12, EffectiveDate: new DateTime(2026, 3, 1), IsFinalized: true),
        (Id: 13, EffectiveDate: new DateTime(2026, 4, 1), IsFinalized: false),
        (Id: 14, EffectiveDate: new DateTime(2026, 5, 1), IsFinalized: true)
    },
    new DateTime(2026, 3, 1));
Check(selectedRevision == 12,
    "Current revision must be the latest effective finalized revision, using highest ID as a same-date tie-breaker.");
Check(WorkflowPositionAssignmentResolver.SelectCurrentRevisionId(
        new[] { (Id: 15, EffectiveDate: new DateTime(2026, 6, 1), IsFinalized: true) },
        new DateTime(2026, 3, 1)) is null,
    "A future-effective revision must not become current.");

var positionCandidates = new[]
{
    new { Id = 20, Code = "ORG-HR-MANAGER" },
    new { Id = 21, Code = "ORG-FINANCE" }
};
var stableMatch = WorkflowPositionAssignmentResolver.FindByStableCode(
    positionCandidates, " org-hr-manager ", x => x.Code);
Check(stableMatch?.Id == 20, "Stable position codes must match case-insensitively after trimming.");
Check(WorkflowPositionAssignmentResolver.FindByStableCode(
        positionCandidates, "ORG-REMOVED", x => x.Code) is null,
    "A removed position must not fall back to a similarly numbered or unrelated position.");

Console.WriteLine("PASS: multi-assignee position ownership, holder fallback, malformed/duplicate IDs,");
Console.WriteLine("      explicit employee-only routing, inactive/invalid employee denial,");
Console.WriteLine("      effective revision selection, stable-code mapping, and instance visibility;");
Console.WriteLine("      unassigned report filters and server-side reassignment eligibility checks.");
await VerifyWorkflowServiceAuthorizationAsync();
await VerifyExplicitPositionTaskCreationAsync();

static async Task VerifyWorkflowServiceAuthorizationAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("workflow-service-authorization-smoke")
        .Options;
    await using var db = new HRPortalDbContext(options);

    static Employee Person(int id, string status = "فعال") => new()
    {
        Id = id,
        PersonnelNumber = $"P{id}",
        NationalId = $"N{id}",
        FirstName = $"کارمند{id}",
        LastName = "آزمایشی",
        Mobile = "09120000000",
        Gender = "مرد",
        Status = status
    };

    var requester = Person(1);
    var priorActor = Person(2);
    var currentHolder = Person(3);
    var formerHolder = Person(4);
    var outsider = Person(5);
    var inactiveAssignee = Person(6, "غیرفعال");

    var oldRevision = new OrganizationStructureRevision
    {
        Id = 1, RevisionCode = "OLD", Title = "نسخه قبلی",
        EffectiveDate = DateTime.Today.AddDays(-60), IsFinalized = true
    };
    var currentRevision = new OrganizationStructureRevision
    {
        Id = 2, RevisionCode = "CURRENT", Title = "نسخه جاری",
        EffectiveDate = DateTime.Today.AddDays(-10), IsFinalized = true
    };
    var oldPosition = new OrganizationNode
    {
        Id = 9, OrganizationStructureRevisionId = 1, Revision = oldRevision,
        Code = "ORG-OPS", Title = "عملیات", RankType = "مدیریت",
        IsActive = true, PositionHolderEmployeeId = 4, AssignedEmployeeIdsCsv = "4"
    };
    var currentPosition = new OrganizationNode
    {
        Id = 10, OrganizationStructureRevisionId = 2, Revision = currentRevision,
        Code = "ORG-OPS", Title = "عملیات", RankType = "مدیریت",
        IsActive = true, PositionHolderEmployeeId = 3, AssignedEmployeeIdsCsv = "3,6"
    };
    oldRevision.Nodes.Add(oldPosition);
    currentRevision.Nodes.Add(currentPosition);

    var definition = new WorkflowDefinition
    {
        Id = 1, Code = "SMOKE", Title = "گردش آزمایشی", IsActive = true
    };
    var step = new WorkflowStep
    {
        Id = 1, WorkflowDefinitionId = 1, WorkflowDefinition = definition,
        Code = "REVIEW", Title = "بررسی", SortOrder = 1,
        AssignmentType = "Position", OrganizationNodeId = 9,
        OrganizationNode = oldPosition
    };
    definition.Steps.Add(step);

    WorkflowInstance Instance(int id) => new()
    {
        Id = id, WorkflowDefinitionId = 1, WorkflowDefinition = definition,
        EntityType = "Smoke", EntityId = id.ToString(),
        RequesterEmployeeId = 1, RequesterEmployee = requester,
        CurrentStepId = 1, CurrentStep = step, Status = "InProgress"
    };
    var instance100 = Instance(100);
    var instance101 = Instance(101);
    var instance102 = Instance(102);
    var instance103 = Instance(103);

    var positionTask = new WorkflowTask
    {
        Id = 1001, WorkflowInstanceId = 100, WorkflowInstance = instance100,
        WorkflowStepId = 1, WorkflowStep = step,
        AssignedPositionId = 9, AssignedPosition = oldPosition,
        AssignedEmployeeId = 4, Status = "Pending"
    };
    var priorActorTask = new WorkflowTask
    {
        Id = 1002, WorkflowInstanceId = 101, WorkflowInstance = instance101,
        WorkflowStepId = 1, WorkflowStep = step,
        AssignedPositionId = 9, AssignedPosition = oldPosition,
        AssignedEmployeeId = 4, Status = "Pending"
    };
    var employeeTask = new WorkflowTask
    {
        Id = 1003, WorkflowInstanceId = 102, WorkflowInstance = instance102,
        WorkflowStepId = 1, WorkflowStep = step,
        AssignedEmployeeId = 2, Status = "Pending"
    };
    var inactiveTask = new WorkflowTask
    {
        Id = 1004, WorkflowInstanceId = 103, WorkflowInstance = instance103,
        WorkflowStepId = 1, WorkflowStep = step,
        AssignedEmployeeId = 6, Status = "Pending"
    };
    db.AddRange(requester, priorActor, currentHolder, formerHolder, outsider, inactiveAssignee,
        oldRevision, currentRevision, oldPosition, currentPosition, definition, step,
        instance100, instance101, instance102, instance103,
        positionTask, priorActorTask, employeeTask, inactiveTask,
        new WorkflowHistory
        {
            Id = 1, WorkflowInstanceId = 101, WorkflowInstance = instance101,
            WorkflowStepId = 1, WorkflowStep = step,
            ActorEmployeeId = 2, ActorEmployee = priorActor,
            Action = "Approve", FromStatus = "InProgress", ToStatus = "Approve"
        });
    await db.SaveChangesAsync();

    var service = new WorkflowService(db, audit: null!);

    var currentInbox = await service.GetInboxAsync(3);
    Check(currentInbox.Any(x => x.TaskId == 1001),
        "The current position holder must see a task created against the previous structure revision.");
    Check(!(await service.GetInboxAsync(4)).Any(x => x.TaskId == 1001),
        "The former holder snapshot must not retain access after position reassignment.");
    Check(!(await service.GetInboxAsync(6)).Any(x => x.TaskId == 1001),
        "An inactive employee listed on a position must not see its task.");
    Check((await service.GetInboxAsync(2)).Any(x => x.TaskId == 1003),
        "The explicitly assigned employee must see an employee-routed task.");
    Check(!(await service.GetInboxAsync(3)).Any(x => x.TaskId == 1003),
        "A position colleague must not see an employee-routed task.");

    Check(await service.GetInstanceAsync(100, 1) is not null,
        "The requester must retain access to the request.");
    Check(await service.GetInstanceAsync(100, 3) is not null,
        "The current position assignee must view the pending workflow.");
    Check(await service.GetInstanceAsync(100, 4) is null,
        "A former position holder must not view the workflow solely from a stale task snapshot.");
    Check(await service.GetInstanceAsync(100, 5) is null,
        "An unrelated employee must not view the workflow.");
    Check(await service.GetInstanceAsync(100, 6) is null,
        "An inactive current-position CSV member must not view the workflow.");
    Check(await service.GetInstanceAsync(101, 2) is not null,
        "A previous actor must retain read access to workflow history.");
    Check(await service.GetInstanceAsync(102, 3) is null,
        "A position colleague must not view a workflow with an employee-specific pending task.");

    // Reading the details page must not claim a pending task. Opening the action
    // form is the explicit claim boundary and records ClaimedAt.
    var taskBeforeActionForm = await db.WorkflowTasks.SingleAsync(x => x.Id == 1001);
    Check(taskBeforeActionForm.ClaimedAt is null,
        "Viewing workflow details unexpectedly claimed the task.");
    await service.GetTaskAsync(1001, 3);
    var taskAfterActionForm = await db.WorkflowTasks.SingleAsync(x => x.Id == 1001);
    Check(taskAfterActionForm.ClaimedAt.HasValue,
        "Opening the authorized action form did not claim the task.");

    Check(await service.GetTaskAsync(1001, 3) is not null,
        "The current position assignee must be able to open the action form.");
    Check(await service.GetTaskAsync(1001, 4) is null,
        "The former position holder must be denied the action form.");
    Check(await service.GetTaskAsync(1003, 2) is not null,
        "The directly assigned employee must be able to open the action form.");
    Check(await service.GetTaskAsync(1003, 3) is null,
        "An unassigned colleague must be denied the employee-specific action form.");
    Check(await service.GetTaskAsync(1004, 6) is null,
        "An inactive employee must be denied an employee-specific action form.");

    Console.WriteLine("PASS: real WorkflowService authorization against EF Core InMemory data,");
    Console.WriteLine("      current-vs-former position holder, employee-only routing, inactive users,");
    Console.WriteLine("      requester/prior actor/current assignee visibility, and claim boundary.");
}

static async Task VerifyExplicitPositionTaskCreationAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("workflow-explicit-position-task-creation")
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    await using var db = new HRPortalDbContext(options);

    static Employee Person(int id, int? legacyPositionId = null) => new()
    {
        Id = id,
        PersonnelNumber = $"PX{id}",
        NationalId = $"NX{id}",
        FirstName = $"کارمند{id}",
        LastName = "آزمایشی",
        Mobile = "09120000000",
        Gender = "مرد",
        Status = "فعال",
        OrganizationPositionId = legacyPositionId
    };

    var first = Person(11, 20);
    var legacyHolder = Person(12, 20);
    var revision = new OrganizationStructureRevision
    {
        Id = 1, RevisionCode = "TASK-TEST", Title = "نسخه آزمون",
        EffectiveDate = DateTime.Today.AddDays(-1), IsFinalized = true
    };
    var position = new OrganizationNode
    {
        Id = 20, OrganizationStructureRevisionId = 1, Revision = revision,
        Code = "TEST-POS", Title = "جایگاه آزمون", RankType = "مدیریت",
        IsActive = true, PositionHolderEmployeeId = 12, AssignedEmployeeIdsCsv = "11"
    };
    revision.Nodes.Add(position);
    var definition = new WorkflowDefinition
    {
        Id = 1, Code = "POSITION-TASK-TEST", Title = "آزمون تخصیص صریح",
        IsActive = true, Version = 1
    };
    var step = new WorkflowStep
    {
        Id = 1, WorkflowDefinitionId = 1, WorkflowDefinition = definition,
        Code = "REVIEW", Title = "بررسی", SortOrder = 1,
        AssignmentType = "Position", OrganizationNodeId = 20
    };
    definition.Steps.Add(step);
    db.AddRange(first, legacyHolder, revision, position, definition, step);
    await db.SaveChangesAsync();

    var audit = new AuditService(db, new HttpContextAccessor());
    var service = new WorkflowService(db, audit);

    var singleInstance = await service.StartAsync(
        "POSITION-TASK-TEST", "Smoke", "single", 11);
    var singleTask = await db.WorkflowTasks.SingleAsync(x => x.WorkflowInstanceId == singleInstance.Id);
    Check(singleTask.AssignedPositionId == 20 && singleTask.AssignedEmployeeId == 11,
        "A single explicit CSV assignee must override a different legacy position holder.");
    Check(await service.GetTaskAsync(singleTask.Id, 11) is not null,
        "The explicitly assigned single employee must be able to open the task.");
    Check(await service.GetTaskAsync(singleTask.Id, 12) is null,
        "The legacy holder must not gain access when a different explicit single assignee exists.");

    position.AssignedEmployeeIdsCsv = "11,12";
    await db.SaveChangesAsync();
    var multiInstance = await service.StartAsync(
        "POSITION-TASK-TEST", "Smoke", "multi", 11);
    var sharedTask = await db.WorkflowTasks.SingleAsync(x => x.WorkflowInstanceId == multiInstance.Id);
    Check(sharedTask.AssignedPositionId == 20 && sharedTask.AssignedEmployeeId is null,
        "Multiple explicit position assignees must share one position-owned task.");
    Check(await service.GetTaskAsync(sharedTask.Id, 11) is not null &&
          await service.GetTaskAsync(sharedTask.Id, 12) is not null,
        "Every active explicit assignee must be able to open the shared position task.");

    Console.WriteLine("PASS: actual task creation honors explicit single and multi-assignee lists over legacy holder data.");
}
