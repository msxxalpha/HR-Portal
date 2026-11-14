using HRPortal.Services;
using HRPortal.Controllers;
using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Middleware;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using QuestPDF.Infrastructure;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

VerifyGranularPermissionMatrix();
VerifyPasswordLoginLockoutPolicy();
await VerifyRoleAdministrationAsync();
await VerifyImprovementActionsAsync();
await VerifyEffectiveEmployeePermissionMatrixAsync();
await VerifySuggestionsAnalyticsAsync();
await VerifyWorkflowStageEditingAndRequesterReturnAsync();
await VerifyWorkflowDirectReturnAndPriorStageEditingAsync();
VerifyWorkflowReportFormTypeAggregation();

var workflowPostedForm = new FormCollection(new Dictionary<string, StringValues>
{
    ["__RequestVerificationToken"] = new StringValues("anti-forgery-token"),
    ["id"] = new StringValues("123"),
    ["comment"] = new StringValues("ملاحظات اقدام"),
    ["action"] = new StringValues("Approve"),
    ["values[field_45]"] = new StringValues("مقدار فیلد جاری"),
    ["previousValues[21]"] = new StringValues("مقدار مرحله قبل")
});
var isolatedCurrentValues = WorkflowTaskFormValueReader.Read(workflowPostedForm, "values");
var isolatedPreviousValues = WorkflowTaskFormValueReader.Read(workflowPostedForm, "previousValues");
Check(isolatedCurrentValues.Count == 1 &&
      isolatedCurrentValues.TryGetValue("field_45", out var currentFieldValue) &&
      currentFieldValue == "مقدار فیلد جاری",
    "Only values[...] entries may be included in the current workflow field dictionary.");
Check(isolatedPreviousValues.Count == 1 &&
      isolatedPreviousValues.TryGetValue("21", out var previousFieldValue) &&
      previousFieldValue == "مقدار مرحله قبل",
    "Only previousValues[...] entries may be included in the previous workflow field dictionary.");

Check(ContractRenewalWorkflowService.MaximumAllowedContractMonths("ApproveOneMonth") == 1,
    "A one-month security approval must cap the allowed contract duration at one month.");
Check(ContractRenewalWorkflowService.MaximumAllowedContractMonths("ApproveFull") == 12 &&
      ContractRenewalWorkflowService.MaximumAllowedContractMonths(null) == 12,
    "A full or unspecified security approval must retain the normal 12-month maximum.");
Check(ContractRenewalWorkflowService.SuggestedContractMonths(29.99m) == 0 &&
      ContractRenewalWorkflowService.SuggestedContractMonths(30m) == 1 &&
      ContractRenewalWorkflowService.SuggestedContractMonths(35m) == 1 &&
      ContractRenewalWorkflowService.SuggestedContractMonths(35.01m) == 3 &&
      ContractRenewalWorkflowService.SuggestedContractMonths(52m) == 3 &&
      ContractRenewalWorkflowService.SuggestedContractMonths(52.01m) == 6 &&
      ContractRenewalWorkflowService.SuggestedContractMonths(70m) == 6 &&
      ContractRenewalWorkflowService.SuggestedContractMonths(70.01m) == 9 &&
      ContractRenewalWorkflowService.SuggestedContractMonths(87m) == 9 &&
      ContractRenewalWorkflowService.SuggestedContractMonths(87.01m) == 12,
    "Contract duration score thresholds or boundaries are incorrect.");

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
await VerifyMaintenanceModeBypassAsync();
await VerifyWorkflowServiceAuthorizationAsync();
await VerifyExplicitPositionTaskCreationAsync();
await VerifyWorkflowTransitionLifecycleAsync();
await VerifyContractRenewalRuntimeFlowAsync();

static void VerifyGranularPermissionMatrix()
{
    static ClaimsPrincipal User(params string[] permissions) =>
        new(new ClaimsIdentity(
            permissions.Select(code => new Claim("Permission", code))
                .Append(new Claim("IsAdmin", "0")), "PermissionSmoke"));
    var reportsOnly = User("ContractRenewal.Reports");
    Check(PermissionAuthorization.HasPermission(reportsOnly, "ContractRenewal.Reports"),
        "A report-only renewal role must be allowed to open the dashboard.");
    Check(!PermissionAuthorization.HasPermission(reportsOnly, "ContractRenewal.List") &&
          !PermissionAuthorization.HasPermission(reportsOnly, "ContractRenewal.Evaluation.Manage") &&
          !PermissionAuthorization.HasPermission(reportsOnly, "ContractRenewal.Settings"),
        "Renewal report access must not grant list or management access.");

    var listOnly = User("ContractRenewal.List");
    Check(PermissionAuthorization.HasPermission(listOnly, "ContractRenewal.List") &&
          !PermissionAuthorization.HasPermission(listOnly, "ContractRenewal.Reports") &&
          !PermissionAuthorization.HasPermission(listOnly, "ContractRenewal.Create"),
        "Renewal list permission must remain independent from reports and create.");

    var tracking = User("ContractRenewal.Track");
    Check(PermissionAuthorization.HasPermission(tracking, "ContractRenewal.View") &&
          PermissionAuthorization.HasPermission(tracking, "ContractRenewal.Create") &&
          PermissionAuthorization.HasPermission(tracking, "ContractRenewal.List") &&
          !PermissionAuthorization.HasPermission(tracking, "ContractRenewal.Reports"),
        "The unified renewal tracking permission must grant request registration and listing, but not reports.");

    var evaluationManager = User("ContractRenewal.Evaluation.Manage");
    Check(PermissionAuthorization.HasPermission(evaluationManager, "ContractRenewal.Evaluation.Manage") &&
          !PermissionAuthorization.HasPermission(evaluationManager, "ContractRenewal.Settings") &&
          !PermissionAuthorization.HasPermission(evaluationManager, "ContractRenewal.Workflow"),
        "Evaluation management must not implicitly grant workflow settings or stop rights.");

    var legacyRenewalManager = User("ContractRenewal.Manage");
    foreach (var code in new[]
    {
        "ContractRenewal.View", "ContractRenewal.List", "ContractRenewal.Reports",
        "ContractRenewal.Create", "ContractRenewal.Edit", "ContractRenewal.Delete",
        "ContractRenewal.Settings", "ContractRenewal.Evaluation.Manage", "ContractRenewal.Workflow"
    })
        Check(PermissionAuthorization.HasPermission(legacyRenewalManager, code),
            $"Legacy ContractRenewal.Manage must retain compatibility with '{code}'.");

    var suggestionReports = User("Suggestions.Reports");
    Check(PermissionAuthorization.HasPermission(suggestionReports, "Suggestions.Reports") &&
          !PermissionAuthorization.HasPermission(suggestionReports, "Suggestions.Manage") &&
          !PermissionAuthorization.HasPermission(suggestionReports, "Suggestions.Submit"),
        "Suggestion reporting must not grant suggestion administration or submission.");
    var suggestionManager = User("Suggestions.Manage");
    Check(PermissionAuthorization.HasPermission(suggestionManager, "Suggestions.Reports") &&
          PermissionAuthorization.HasPermission(suggestionManager, "Suggestions.Export"),
        "Suggestion manager must retain report and export capability.");
    var adminIdentity = new ClaimsIdentity(new[] { new Claim("IsAdmin", "1") }, "PermissionSmoke");
    var admin = new ClaimsPrincipal(adminIdentity);
    Check(PermissionAuthorization.HasPermission(admin, "ContractRenewal.Evaluation.Manage") &&
          PermissionAuthorization.HasPermission(admin, "Suggestions.Reports"),
        "System administrator must retain access to all module actions.");

    var expectedCodes = new[]
    {
        "ContractRenewal.Track", "ContractRenewal.List", "ContractRenewal.Reports",
        "ContractRenewal.Evaluation.Manage", "Suggestions.Reports", "Suggestions.Export"
    };
    foreach (var code in expectedCodes)
        Check(RoleService.DefaultPermissions.Count(x => x.Code == code) == 1,
            $"Permission catalogue must seed exactly one '{code}' permission.");

    Console.WriteLine("PASS: granular contract-renewal list/report/evaluation permissions, legacy Manage compatibility, suggestion report/export separation, and administrator access.");
}

static void VerifyPasswordLoginLockoutPolicy()
{
    var now = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
    var until = now.Add(PasswordLoginLockoutPolicy.LockoutDuration);
    Check(PasswordLoginLockoutPolicy.MaximumFailedAttempts == 5,
        "Employee password lockout must start after five failed attempts.");
    Check(PasswordLoginLockoutPolicy.LockoutDuration == TimeSpan.FromMinutes(15),
        "Employee password lockout duration must be exactly 15 minutes.");
    Check(PasswordLoginLockoutPolicy.IsLocked(until, now),
        "A future lockout expiration must be considered locked.");
    Check(PasswordLoginLockoutPolicy.GetRemainingSeconds(until, now) == 900,
        "A new 15-minute lockout must expose 900 seconds remaining.");
    Check(!PasswordLoginLockoutPolicy.IsLocked(until, until) &&
          PasswordLoginLockoutPolicy.GetRemainingSeconds(until, until) == 0,
        "The lockout must end exactly at its UTC expiration timestamp.");
    Check(!PasswordLoginLockoutPolicy.IsLocked(null, now) &&
          PasswordLoginLockoutPolicy.GetRemainingSeconds(null, now) == 0,
        "An account without a lockout timestamp must remain unlocked.");
    Console.WriteLine("PASS: persistent account-lockout thresholds, UTC expiry boundary and remaining-time calculation.");
}

static async Task VerifyEffectiveEmployeePermissionMatrixAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("effective-employee-permission-matrix-smoke")
        .Options;
    await using var db = new HRPortalDbContext(options);
    var activeEmployee = new Employee
    {
        Id = 901, PersonnelNumber = "M-901", NationalId = "9019019010",
        FirstName = "کارمند", LastName = "فعال", Mobile = "09120000901",
        Gender = "مرد", Status = "فعال", IsSystemUser = true,
        OrganizationPositionId = 100, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
    var inactiveSystemEmployee = new Employee
    {
        Id = 902, PersonnelNumber = "M-902", NationalId = "9029029020",
        FirstName = "کاربر", LastName = "غیرفعال", Mobile = "09120000902",
        Gender = "مرد", Status = "غیرفعال", IsSystemUser = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
    var revision = new OrganizationStructureRevision
    {
        Id = 90, RevisionCode = "PERMISSION-SMOKE", Title = "ساختار آزمایشی",
        EffectiveDate = DateTime.Today.AddDays(-10), IsFinalized = true,
        FinalizedAt = DateTime.UtcNow.AddDays(-10)
    };
    var position = new OrganizationNode
    {
        Id = 100, OrganizationStructureRevisionId = revision.Id, Revision = revision,
        Code = "POSITION-MANAGER", Title = "جایگاه مدیریت آزمایشی",
        RankType = "مدیریت", IsActive = true,
        PositionHolderEmployeeId = activeEmployee.Id
    };
    revision.Nodes.Add(position);

    var activeDirectRole = new Role { Id = 11, Code = "DIRECT", Title = "نقش مستقیم", IsActive = true };
    var inactiveRole = new Role { Id = 12, Code = "DISABLED", Title = "نقش غیرفعال", IsActive = false };
    var rankRole = new Role { Id = 13, Code = "RANK", Title = "نقش رده سازمانی", IsActive = true };
    var directPermission = new Permission { Id = 21, Code = "Smoke.Direct", Title = "مجوز مستقیم", Module = "Smoke" };
    var rankPermission = new Permission { Id = 22, Code = "Smoke.Rank", Title = "مجوز رده", Module = "Smoke" };
    var personalPermission = new Permission { Id = 23, Code = "Smoke.Personal", Title = "مجوز شخصی", Module = "Smoke" };
    var disabledPermission = new Permission { Id = 24, Code = "Smoke.Disabled", Title = "مجوز نقش غیرفعال", Module = "Smoke" };

    db.AddRange(activeEmployee, inactiveSystemEmployee, revision, position,
        activeDirectRole, inactiveRole, rankRole,
        directPermission, rankPermission, personalPermission, disabledPermission);
    db.EmployeeRoles.AddRange(
        new EmployeeRole { EmployeeId = activeEmployee.Id, RoleId = activeDirectRole.Id, Employee = activeEmployee, Role = activeDirectRole },
        new EmployeeRole { EmployeeId = activeEmployee.Id, RoleId = inactiveRole.Id, Employee = activeEmployee, Role = inactiveRole });
    db.RolePermissions.AddRange(
        new RolePermission { RoleId = activeDirectRole.Id, PermissionId = directPermission.Id, Role = activeDirectRole, Permission = directPermission },
        new RolePermission { RoleId = inactiveRole.Id, PermissionId = disabledPermission.Id, Role = inactiveRole, Permission = disabledPermission },
        new RolePermission { RoleId = rankRole.Id, PermissionId = rankPermission.Id, Role = rankRole, Permission = rankPermission });
    db.RoleOrganizationRanks.Add(new RoleOrganizationRank { RoleId = rankRole.Id, RankType = "مدیریت" });
    db.EmployeePermissions.Add(new EmployeePermission
    {
        EmployeeId = activeEmployee.Id, PermissionId = personalPermission.Id,
        Permission = personalPermission
    });
    await db.SaveChangesAsync();

    var service = new RoleService(db);
    var effective = await service.GetEmployeePermissionsAsync(activeEmployee.Id);
    Check(effective.SetEquals(new[] { "Smoke.Direct", "Smoke.Rank", "Smoke.Personal" }),
        "Effective permissions must be the union of active direct roles, current organizational-rank roles and personal grants; inactive roles must be ignored.");
    var inactive = await service.GetEmployeePermissionsAsync(inactiveSystemEmployee.Id);
    Check(inactive.Count == 0,
        "An inactive employee must receive no effective permissions even when role records exist.");

    var adminClaims = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("IsAdmin", "1") }, "PermissionSmoke"));
    Check(PermissionAuthorization.HasPermission(adminClaims, "Smoke.Direct") &&
          PermissionAuthorization.HasPermission(adminClaims, "ContractRenewal.Reports"),
        "Administrator access must bypass granular module checks.");

    Console.WriteLine("PASS: effective permission matrix combines active direct roles, current organizational-rank roles and personal grants; ignores inactive roles/employees.");
}

static async Task VerifySuggestionsAnalyticsAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("suggestions-analytics-smoke")
        .Options;
    await using var db = new HRPortalDbContext(options);
    var now = DateTime.UtcNow;
    var employee = new Employee
    {
        Id = 77, PersonnelNumber = "S-077", NationalId = "0770770770",
        FirstName = "پیشنهاددهنده", LastName = "آزمایشی", Mobile = "09120000077",
        Gender = "مرد", Status = "فعال", IsSystemUser = true,
        CreatedAt = now, UpdatedAt = now
    };
    var collaborator = new Employee
    {
        Id = 78, PersonnelNumber = "S-078", NationalId = "0780780780",
        FirstName = "همکار", LastName = "آزمایشی", Mobile = "09120000078",
        Gender = "مرد", Status = "فعال", IsSystemUser = true,
        CreatedAt = now, UpdatedAt = now
    };
    db.Employees.AddRange(employee, collaborator);
    await db.SaveChangesAsync();

    var draft = new Suggestion
    {
        SuggestionNumber = "PISH-SMOKE-001", Title = "پیش‌نویس آزمایشی",
        Category = "کاهش هزینه و صرفه‌جویی", ShortDescription = "پیش‌نویس",
        ProblemDescription = "شرح مسئله", ProposedSolution = "راه‌حل",
        BenefitsJson = "[\"کاهش هزینه\"]", ResourceTypesJson = "[\"بودجه مالی\"]",
        Status = "Draft", ProposerEmployeeId = employee.Id,
        CreatedAtUtc = now.AddDays(-2), UpdatedAtUtc = now.AddDays(-2)
    };
    var submitted = new Suggestion
    {
        SuggestionNumber = "PISH-SMOKE-002", Title = "پیشنهاد ارسالی",
        Category = "افزایش بهره‌وری و کاهش زمان", ShortDescription = "ارسال‌شده",
        ProblemDescription = "شرح مسئله", ProposedSolution = "راه‌حل",
        BenefitsJson = "[\"کاهش هزینه\",\"افزایش بهره‌وری\"]",
        ResourceTypesJson = "[\"بودجه مالی\"]", Status = "Submitted",
        ProposerEmployeeId = employee.Id, CreatedAtUtc = now.AddDays(-1),
        UpdatedAtUtc = now, SubmittedAtUtc = now
    };
    var accepted = new Suggestion
    {
        SuggestionNumber = "PISH-SMOKE-003", Title = "پیشنهاد پذیرفته‌شده",
        Category = "کاهش هزینه و صرفه‌جویی", ShortDescription = "پذیرفته‌شده",
        ProblemDescription = "شرح مسئله", ProposedSolution = "راه‌حل",
        BenefitsJson = "[\"کاهش هزینه\"]", ResourceTypesJson = "[]",
        Status = "Accepted", ProposerEmployeeId = employee.Id,
        CreatedAtUtc = now.AddDays(-10), UpdatedAtUtc = now.AddDays(-3),
        SubmittedAtUtc = now.AddDays(-3)
    };
    db.Suggestions.AddRange(draft, submitted, accepted);
    await db.SaveChangesAsync();

    // Persist child records explicitly after the parent IDs exist, mirroring the
    // production workflow's separate upload/member persistence steps.
    db.SuggestionMembers.Add(new SuggestionMember
    {
        SuggestionId = submitted.Id, EmployeeId = collaborator.Id,
        EmployeeNameSnapshot = collaborator.FirstName + " " + collaborator.LastName,
        PersonnelNumberSnapshot = collaborator.PersonnelNumber
    });
    db.SuggestionAttachments.Add(new SuggestionAttachment
    {
        SuggestionId = submitted.Id, OriginalFileName = "idea.pdf",
        StoredFileName = "smoke.pdf", ContentType = "application/pdf", FileSize = 1024
    });
    await db.SaveChangesAsync();
    Check(await db.SuggestionMembers.CountAsync(x => x.SuggestionId == submitted.Id) == 1 &&
          await db.SuggestionAttachments.CountAsync(x => x.SuggestionId == submitted.Id) == 1,
        "Suggestion analytics fixture must persist one collaborative member and one attachment.");

    var controller = new SuggestionsReportsController(db);
    var http = new DefaultHttpContext();
    http.User = new ClaimsPrincipal(new ClaimsIdentity(
        new[] { new Claim("Permission", "Suggestions.Reports"), new Claim("Permission", "Suggestions.Export") },
        "SuggestionsReportSmoke"));
    controller.ControllerContext = new ControllerContext { HttpContext = http };

    var response = await controller.Index(null, null, "all", "all");
    var model = (response as ViewResult)?.Model as SuggestionsDashboardViewModel;
    Check(model is not null, "Suggestion reporting endpoint must return its dashboard view model.");
    Check(model!.TotalCount == 3 && model.DraftCount == 1 &&
          model.SubmittedCount == 1 && model.AcceptedCount == 1 && model.InEvaluationCount == 0,
        "Suggestion dashboard status KPIs must match the stored records.");
    Check(model.UniqueProposers == 1 && model.CollaborativeCount == 1 &&
          model.AttachmentCount == 1 && model.SubmittedLast30Days == 2,
        $"Suggestion dashboard must correctly aggregate proposers, collaborators, attachments and recent submissions. Actual: proposers={model.UniqueProposers}, collaborative={model.CollaborativeCount}, attachments={model.AttachmentCount}, recent={model.SubmittedLast30Days}.");
    Check(model.CategoryBuckets.Sum(x => x.Count) == 3 &&
          model.BenefitBuckets.Single(x => x.Label == "کاهش هزینه").Count == 3 &&
          model.ResourceBuckets.Single(x => x.Label == "بودجه مالی").Count == 2 &&
          model.MonthlyBuckets.Sum(x => x.Count) == 2,
        "Suggestion dashboard category, benefit, resource and Jalali monthly aggregates must match source data.");

    var filteredResponse = await controller.Index(now.AddDays(-1).Date, now.Date, "all", "Submitted");
    var filteredModel = (filteredResponse as ViewResult)?.Model as SuggestionsDashboardViewModel;
    Check(filteredModel?.TotalCount == 1 && filteredModel.SubmittedCount == 1,
        "Suggestion report date and status filters must return only matching records.");

    var csvResponse = await controller.Export(null, null, "all", "all");
    var csv = csvResponse as FileContentResult;
    Check(csv is not null && csv.ContentType.StartsWith("text/csv", StringComparison.OrdinalIgnoreCase) &&
          csv.FileContents.Length > 3 && csv.FileContents[0] == 0xEF &&
          csv.FileContents[1] == 0xBB && csv.FileContents[2] == 0xBF,
        "Suggestion CSV export must return UTF-8 BOM content for correct Persian Excel display.");

    Console.WriteLine("PASS: suggestions dashboard KPIs, category/benefit/resource/Jalali aggregates, filters and UTF-8 CSV export.");
}

static async Task VerifyMaintenanceModeBypassAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("maintenance-mode-access-smoke")
        .Options;
    await using var db = new HRPortalDbContext(options);
    db.SystemSettings.Add(new SystemSettings { Id = 1, MaintenanceMode = true });
    await db.SaveChangesAsync();

    static ClaimsPrincipal Principal(string? isAdmin) =>
        new(new ClaimsIdentity(
            isAdmin is null ? Array.Empty<Claim>() : new[] { new Claim("IsAdmin", isAdmin) },
            "MaintenanceSmoke"));

    async Task<(bool NextCalled, bool Redirected)> RunAsync(ClaimsPrincipal user, string path)
    {
        var context = new DefaultHttpContext();
        context.User = user;
        context.Request.Path = path;
        var nextCalled = false;
        var middleware = new MaintenanceModeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);
        var redirected = context.Response.StatusCode == StatusCodes.Status302Found &&
            string.Equals(context.Response.Headers["Location"].ToString(), "/Home/Maintenance", StringComparison.Ordinal);
        return (nextCalled, redirected);
    }

    var administrator = await RunAsync(Principal("1"), "/Employees");
    Check(administrator.NextCalled && !administrator.Redirected,
        "An authenticated system administrator must bypass maintenance mode.");

    var employee = Principal("0");
    var employeeRequest = await RunAsync(employee, "/Employees");
    Check(!employeeRequest.NextCalled && employeeRequest.Redirected,
        "An ordinary employee must be redirected to the maintenance page.");

    var anonymousRequest = await RunAsync(new ClaimsPrincipal(new ClaimsIdentity()), "/");
    Check(!anonymousRequest.NextCalled && anonymousRequest.Redirected,
        "An anonymous application request must be redirected during maintenance.");

    var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
    var adminLoginPage = await RunAsync(anonymous, "/Account/AdminLogin");
    Check(adminLoginPage.NextCalled && !adminLoginPage.Redirected,
        "The dedicated administrator sign-in page must remain reachable during maintenance.");

    foreach (var blockedPath in new[] { "/", "/Account/Login", "/Account/EmployeeLogin", "/Account/EmployeePasswordLogin", "/Account/Verify", "/SystemSettings" })
    {
        var blockedRequest = await RunAsync(anonymous, blockedPath);
        Check(!blockedRequest.NextCalled && blockedRequest.Redirected,
            $"The ordinary endpoint '{blockedPath}' must be redirected to the maintenance notice.");
        Check(!MaintenanceModeMiddleware.IsExemptPath(new PathString(blockedPath)),
            $"The endpoint '{blockedPath}' must not bypass the maintenance gate.");
    }

    foreach (var exemptPath in new[] { "/Account/AdminLogin", "/Account/Logout", "/Home/Maintenance" })
    {
        var exemptRequest = await RunAsync(employee, exemptPath);
        Check(exemptRequest.NextCalled && !exemptRequest.Redirected,
            $"The intended maintenance endpoint '{exemptPath}' must remain reachable.");
        Check(MaintenanceModeMiddleware.IsExemptPath(new PathString(exemptPath)),
            $"The intended maintenance endpoint '{exemptPath}' must be explicitly exempt.");
    }

    var employeeLogout = await RunAsync(employee, "/Account/Logout");
    Check(employeeLogout.NextCalled && !employeeLogout.Redirected,
        "Users must retain access to safe logout during maintenance.");

    var settings = await db.SystemSettings.SingleAsync();
    settings.MaintenanceMode = false;
    await db.SaveChangesAsync();
    var normalOperation = await RunAsync(employee, "/Employees");
    Check(normalOperation.NextCalled && !normalOperation.Redirected,
        "Normal employee access must resume after maintenance mode is disabled.");
    var normalLogin = await RunAsync(anonymous, "/Account/Login");
    Check(normalLogin.NextCalled && !normalLogin.Redirected,
        "The regular login page must become available again after maintenance mode is disabled.");

    Console.WriteLine("PASS: maintenance mode routes direct entry/employee login to the notice, allows admin-only sign-in and safe logout, and restores normal login when disabled.");
}

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
    var employeeStep = new WorkflowStep
    {
        Id = 2, WorkflowDefinitionId = 1, WorkflowDefinition = definition,
        Code = "EMPLOYEE", Title = "اقدام کارمند مشخص", SortOrder = 2,
        AssignmentType = "Employee", AssignedEmployeeId = 2
    };
    definition.Steps.Add(step);
    definition.Steps.Add(employeeStep);

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
    instance102.CurrentStepId = employeeStep.Id;
    instance102.CurrentStep = employeeStep;
    var instance103 = Instance(103);
    instance103.CurrentStepId = employeeStep.Id;
    instance103.CurrentStep = employeeStep;

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
        WorkflowStepId = 2, WorkflowStep = employeeStep,
        AssignedEmployeeId = 2, Status = "Pending"
    };
    var inactiveTask = new WorkflowTask
    {
        Id = 1004, WorkflowInstanceId = 103, WorkflowInstance = instance103,
        WorkflowStepId = 2, WorkflowStep = employeeStep,
        AssignedEmployeeId = 6, Status = "Pending"
    };
    db.AddRange(requester, priorActor, currentHolder, formerHolder, outsider, inactiveAssignee,
        oldRevision, currentRevision, oldPosition, currentPosition, definition, step, employeeStep,
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

    var audit = new AuditService(new AuditLogQueue(), new HttpContextAccessor());
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

    var positionStep = await db.WorkflowSteps.SingleAsync(x => x.Id == 1);
    positionStep.AssignmentMode = "All";
    await db.SaveChangesAsync();
    var allInstance = await service.StartAsync(
        "POSITION-TASK-TEST", "Smoke", "all", 11);
    var allTasks = await db.WorkflowTasks
        .Where(x => x.WorkflowInstanceId == allInstance.Id && x.Status == "Pending")
        .OrderBy(x => x.AssignedEmployeeId).ToListAsync();
    Check(allTasks.Count == 2 &&
          allTasks.All(x => x.AssignedPositionId == 20 && x.AssignedEmployeeId.HasValue),
        "All mode must create one position-owned task per explicitly assigned employee.");
    Check(await service.GetTaskAsync(allTasks[0].Id, allTasks[0].AssignedEmployeeId!.Value) is not null &&
          await service.GetTaskAsync(allTasks[0].Id, allTasks[1].AssignedEmployeeId!.Value) is null,
        "An All-mode task must be accessible only to its designated employee.");
    await service.CompleteTaskAsync(allTasks[0].Id, allTasks[0].AssignedEmployeeId!.Value,
        "Approve", "تأیید نفر اول");
    Check(await db.WorkflowInstances.AnyAsync(x => x.Id == allInstance.Id && x.Status == "InProgress") &&
          await db.WorkflowTasks.CountAsync(x => x.WorkflowInstanceId == allInstance.Id && x.Status == "Pending") == 1,
        "All mode must wait for every assigned employee before advancing.");
    await service.CompleteTaskAsync(allTasks[1].Id, allTasks[1].AssignedEmployeeId!.Value,
        "Approve", "تأیید نفر دوم");
    Check(await db.WorkflowInstances.AnyAsync(x => x.Id == allInstance.Id && x.Status == "Approved"),
        "All mode must advance only after the last assigned employee approves.");

    Console.WriteLine("PASS: actual task creation honors explicit single/multi assignees and Any/All approval modes.");
}

static async Task VerifyWorkflowTransitionLifecycleAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("workflow-transition-lifecycle")
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    await using var db = new HRPortalDbContext(options);

    static Employee Person(int id) => new()
    {
        Id = id, PersonnelNumber = $"PL{id}", NationalId = $"NL{id}",
        FirstName = $"کارمند{id}", LastName = "آزمایشی",
        Mobile = "09120000000", Gender = "مرد", Status = "فعال"
    };

    var requester = Person(11);
    var reviewer = Person(12);
    var definition = new WorkflowDefinition
    {
        Id = 1, Code = "LIFECYCLE", Title = "چرخه عمر آزمون",
        IsActive = true, Version = 1
    };
    var first = new WorkflowStep
    {
        Id = 1, WorkflowDefinitionId = 1, WorkflowDefinition = definition,
        Code = "FIRST", Title = "مرحله اول", SortOrder = 10,
        AssignmentType = "Employee", AssignedEmployeeId = 11,
        AllowApprove = true, AllowReject = false, AllowReturn = false
    };
    var second = new WorkflowStep
    {
        Id = 2, WorkflowDefinitionId = 1, WorkflowDefinition = definition,
        Code = "SECOND", Title = "مرحله دوم", SortOrder = 20,
        AssignmentType = "Employee", AssignedEmployeeId = 12,
        AllowApprove = true, AllowReject = true, AllowReturn = true
    };
    first.Fields.Add(new WorkflowStepField
    {
        Id = 1, WorkflowStepId = 1, WorkflowStep = first,
        Code = "ReviewNote", Title = "یادداشت بررسی",
        FieldType = "Text", IsRequired = true, MaxLength = 200
    });
    definition.Steps.Add(first);
    definition.Steps.Add(second);
    db.AddRange(requester, reviewer, definition, first, second,
        new WorkflowTransition
        {
            Id = 1, WorkflowStepId = 2, WorkflowStep = second,
            ToStepId = 1, ToStep = first, Action = "Return", Title = "بازگشت به مرحله اول"
        });
    await db.SaveChangesAsync();

    var audit = new AuditService(new AuditLogQueue(), new HttpContextAccessor());
    var service = new WorkflowService(db, audit);
    var instance = await service.StartAsync("LIFECYCLE", "Smoke", "lifecycle", 11);

    var firstTask = await db.WorkflowTasks.SingleAsync(x => x.WorkflowInstanceId == instance.Id);
    Check(await service.GetTaskAsync(firstTask.Id, 12) is null,
        "An employee not assigned to the first step must not open its action form.");
    Check(await service.GetTaskAsync(firstTask.Id, 11) is not null,
        "The assigned employee must be able to open the first action form.");

    await service.CompleteTaskAsync(firstTask.Id, 11, "SaveDraft", "ذخیره موقت",
        new Dictionary<string, string?> { ["field_1"] = "پیش‌نویس" });
    Check(await db.WorkflowFieldValues.AnyAsync(x => x.WorkflowInstanceId == instance.Id &&
        x.WorkflowStepFieldId == 1 && x.FieldCode == "ReviewNote" && x.Value == "پیش‌نویس"),
        "A stable field ID posted by the task form must be mapped to its workflow field.");
    Check(await db.WorkflowTasks.AnyAsync(x => x.Id == firstTask.Id && x.Status == "Pending"),
        "Saving a draft must not complete or advance the task.");
    Check(await db.WorkflowHistory.AnyAsync(x => x.WorkflowInstanceId == instance.Id && x.Action == "SaveDraft"),
        "Saving a draft must create an auditable workflow history entry.");

    var requiredFieldRejected = false;
    try
    {
        await service.CompleteTaskAsync(firstTask.Id, 11, "Approve", "تأیید");
    }
    catch (InvalidOperationException)
    {
        requiredFieldRejected = true;
    }
    Check(requiredFieldRejected &&
          await db.WorkflowTasks.AnyAsync(x => x.Id == firstTask.Id && x.Status == "Pending"),
        "An approval with a missing required field must be rejected without completing the task.");

    await service.CompleteTaskAsync(firstTask.Id, 11, "Approve", "تأیید مرحله اول",
        new Dictionary<string, string?> { ["ReviewNote"] = "بررسی انجام شد" });
    var secondTask = await db.WorkflowTasks.SingleAsync(x =>
        x.WorkflowInstanceId == instance.Id && x.WorkflowStepId == second.Id && x.Status == "Pending");
    Check(await db.WorkflowTasks.AnyAsync(x => x.Id == firstTask.Id && x.Status == "Completed"),
        "Approval must complete the first task.");
    Check((await db.WorkflowInstances.SingleAsync(x => x.Id == instance.Id)).CurrentStepId == second.Id,
        "Approval must advance the workflow to its configured next step.");

    await service.CompleteTaskAsync(secondTask.Id, 12, "Return", "نیازمند اصلاح");
    var returnedInstance = await db.WorkflowInstances.SingleAsync(x => x.Id == instance.Id);
    Check(returnedInstance.Status == "InProgress" && returnedInstance.CurrentStepId == first.Id,
        "Return must route to the configured previous step without terminating the instance.");
    var returnedTask = await db.WorkflowTasks.SingleAsync(x =>
        x.WorkflowInstanceId == instance.Id && x.WorkflowStepId == first.Id && x.Status == "Pending");
    Check(await db.WorkflowHistory.AnyAsync(x => x.WorkflowInstanceId == instance.Id && x.Action == "Return"),
        "Return must be recorded in workflow history.");

    await service.CompleteTaskAsync(returnedTask.Id, 11, "Approve", "تأیید مجدد",
        new Dictionary<string, string?> { ["ReviewNote"] = "اصلاح انجام شد" });
    var finalReviewTask = await db.WorkflowTasks.SingleAsync(x =>
        x.WorkflowInstanceId == instance.Id && x.WorkflowStepId == second.Id && x.Status == "Pending");
    await service.CompleteTaskAsync(finalReviewTask.Id, 12, "Reject", "عدم تأیید");
    var rejectedInstance = await db.WorkflowInstances.SingleAsync(x => x.Id == instance.Id);
    Check(rejectedInstance.Status == "Rejected" && rejectedInstance.CurrentStepId is null,
        "Reject without a configured route must terminate the workflow as Rejected.");
    Check(await db.WorkflowHistory.CountAsync(x => x.WorkflowInstanceId == instance.Id) >= 6,
        "The full draft/approve/return/re-approve/reject lifecycle must retain its history.");

    Console.WriteLine("PASS: workflow lifecycle draft validation, approval, return, rejection, task creation and audit history.");
}

static async Task VerifyContractRenewalRuntimeFlowAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("contract-renewal-runtime-flow")
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    await using var db = new HRPortalDbContext(options);

    static Employee Person(int id) => new()
    {
        Id = id, PersonnelNumber = $"CR{id}", NationalId = $"CN{id}",
        FirstName = $"کارمند{id}", LastName = "آزمایشی",
        Mobile = "09120000000", Gender = "مرد", Status = "فعال"
    };

    var subject = Person(1);
    var supervisor = Person(2);
    var manager = Person(3);
    var deputy = Person(4);
    var security = Person(5);
    var hr = Person(6);
    var ceo = Person(7);
    var requester = Person(10);
    var initialApprover = Person(8);

    var revision = new OrganizationStructureRevision
    {
        Id = 1, RevisionCode = "RENEWAL-E2E", Title = "ساختار آزمون تمدید",
        EffectiveDate = DateTime.Today.AddDays(-1), IsFinalized = true
    };
    var subjectPosition = new OrganizationNode
    {
        Id = 101, OrganizationStructureRevisionId = 1, Revision = revision,
        Code = "SUBJECT", Title = "بخش کارمند", RankType = "بخش",
        IsActive = true, AssignedEmployeeIdsCsv = "1", ParentId = 102
    };
    var supervisorPosition = new OrganizationNode
    {
        Id = 102, OrganizationStructureRevisionId = 1, Revision = revision,
        Code = "SUPERVISOR", Title = "سرپرستی", RankType = "سرپرستی",
        IsActive = true, AssignedEmployeeIdsCsv = "2", PositionHolderEmployeeId = 2, ParentId = 103
    };
    var managerPosition = new OrganizationNode
    {
        Id = 103, OrganizationStructureRevisionId = 1, Revision = revision,
        Code = "MANAGER", Title = "ریاست", RankType = "ریاست",
        IsActive = true, AssignedEmployeeIdsCsv = "3", PositionHolderEmployeeId = 3, ParentId = 104
    };
    var deputyPosition = new OrganizationNode
    {
        Id = 104, OrganizationStructureRevisionId = 1, Revision = revision,
        Code = "DEPUTY", Title = "معاونت", RankType = "معاونت",
        IsActive = true, AssignedEmployeeIdsCsv = "4", PositionHolderEmployeeId = 4, ParentId = 105
    };
    var ceoPosition = new OrganizationNode
    {
        Id = 105, OrganizationStructureRevisionId = 1, Revision = revision,
        Code = "CEO", Title = "مدیرعامل", RankType = "مدیرعامل",
        IsActive = true, PositionHolderEmployeeId = 7
    };
    var securityPosition = new OrganizationNode
    {
        Id = 106, OrganizationStructureRevisionId = 1, Revision = revision,
        Code = "SECURITY", Title = "حراست", RankType = "مدیریت",
        IsActive = true, AssignedEmployeeIdsCsv = "5", PositionHolderEmployeeId = 5
    };
    var hrPosition = new OrganizationNode
    {
        Id = 107, OrganizationStructureRevisionId = 1, Revision = revision,
        Code = "HR", Title = "مدیریت منابع انسانی", RankType = "مدیریت",
        IsActive = true, AssignedEmployeeIdsCsv = "6", PositionHolderEmployeeId = 6
    };
    var approvalPosition = new OrganizationNode
    {
        Id = 108, OrganizationStructureRevisionId = 1, Revision = revision,
        Code = "REQUEST-APPROVAL", Title = "تأییدکننده اطلاعات اولیه", RankType = "مدیریت",
        IsActive = true, AssignedEmployeeIdsCsv = "8", PositionHolderEmployeeId = 8
    };
    revision.Nodes.Add(subjectPosition);
    revision.Nodes.Add(supervisorPosition);
    revision.Nodes.Add(managerPosition);
    revision.Nodes.Add(deputyPosition);
    revision.Nodes.Add(ceoPosition);
    revision.Nodes.Add(securityPosition);
    revision.Nodes.Add(hrPosition);
    revision.Nodes.Add(approvalPosition);

    var request = new ContractRenewalRequest
    {
        Id = 1, RequestCode = "CR-E2E-001", EmployeeId = 1,
        Employee = subject, CreatedByEmployeeId = 10,
        EmployeeNameSnapshot = "کارمند آزمایشی",
        PersonnelNumberSnapshot = "CR1",
        OrganizationUnitSnapshot = "واحد آزمون",
        PositionTitleSnapshot = "کارشناس",
        Status = "Submitted"
    };
    var settings = new ContractRenewalWorkflowSettings
    {
        Id = 1, RequestApprovalPositionId = 108,
        SecurityPositionId = 106, HumanResourcesPositionId = 107,
        CeoEmployeeId = 7
    };
    var question = new EmployeePerformanceEvaluationQuestion
    {
        Id = 1, QuestionText = "کیفیت انجام وظایف",
        MaxScore = 100m, ScoringGuidance = "ارزیابی کلی",
        IsActive = true, SortOrder = 1
    };
    // The hierarchy route uses these master-data sort orders to retain all
    // positions from the supervisor rank upward, while excluding the subject's
    // lower rank. Seed the same rank vocabulary used by the organization fixture.
    var rankDefinitions = new[]
    {
        new OrganizationRankDefinition { Id = 1, Title = "مدیرعامل", SortOrder = 10 },
        new OrganizationRankDefinition { Id = 2, Title = "معاونت", SortOrder = 20 },
        new OrganizationRankDefinition { Id = 3, Title = "مدیریت", SortOrder = 30 },
        new OrganizationRankDefinition { Id = 4, Title = "ریاست", SortOrder = 40 },
        new OrganizationRankDefinition { Id = 5, Title = "سرپرستی", SortOrder = 50 },
        new OrganizationRankDefinition { Id = 6, Title = "بخش", SortOrder = 60 }
    };
    db.AddRange(subject, supervisor, manager, deputy, security, hr, ceo, requester, initialApprover,
        revision, subjectPosition, supervisorPosition, managerPosition, deputyPosition,
        ceoPosition, securityPosition, hrPosition, approvalPosition, request, settings, question);
    db.OrganizationRankDefinitions.AddRange(rankDefinitions);
    await db.SaveChangesAsync();

    var audit = new AuditService(new AuditLogQueue(), new HttpContextAccessor());
    var workflow = new WorkflowService(db, audit);
    var renewal = new ContractRenewalWorkflowService(db, workflow);
    var instance = await renewal.StartAsync(request, requesterEmployeeId: 10);

    var evaluationSteps = await db.WorkflowSteps.AsNoTracking()
        .Where(x => x.WorkflowDefinitionId == instance.WorkflowDefinitionId && x.Code.StartsWith("Evaluation_H"))
        .OrderBy(x => x.SortOrder).ToListAsync();
    Check(evaluationSteps.Count == 3,
        "The contract renewal route must include every intermediate supervisor/manager/deputy position.");
    Check(await db.EmployeePerformanceEvaluationScores.CountAsync(x => x.WorkflowInstanceId == instance.Id) == 3,
        "Each evaluation stage must receive a question score snapshot.");
    var firstApproval = await db.WorkflowTasks.Include(x => x.WorkflowStep)
        .SingleAsync(x => x.WorkflowInstanceId == instance.Id && x.Status == "Pending");
    Check(firstApproval.WorkflowStep.Code == "IntakeApproval" &&
          await workflow.GetTaskAsync(firstApproval.Id, 8) is not null,
        "A new request must first be assigned to the configured organizational approver.");
    Check(await workflow.GetTaskAsync(firstApproval.Id, 10) is null,
        "The requester must not be able to act on the organizational approval task.");

    await workflow.CompleteTaskAsync(firstApproval.Id, 8, "Return", "لطفاً سوابق را تکمیل کنید.");
    var correctionTask = await db.WorkflowTasks.Include(x => x.WorkflowStep)
        .SingleAsync(x => x.WorkflowInstanceId == instance.Id && x.Status == "Pending");
    Check(correctionTask.WorkflowStep.Code == "RequesterEdit" &&
          correctionTask.AssignedEmployeeId == 10 &&
          await workflow.GetTaskAsync(correctionTask.Id, 10) is not null,
        "Returning the request must create a correction task for the original requester.");
    Check(await workflow.GetTaskAsync(correctionTask.Id, 8) is null,
        "The organizational approver must not be able to act on the requester's correction task.");

    await workflow.CompleteTaskAsync(correctionTask.Id, 10, "Approve", "اطلاعات اصلاح شد.");
    var secondApproval = await db.WorkflowTasks.Include(x => x.WorkflowStep)
        .SingleAsync(x => x.WorkflowInstanceId == instance.Id && x.Status == "Pending");
    Check(secondApproval.WorkflowStep.Code == "IntakeApproval" &&
          await workflow.GetTaskAsync(secondApproval.Id, 8) is not null,
        "A corrected request must return to the same organizational approver.");

    await workflow.CompleteTaskAsync(secondApproval.Id, 8, "Approve", "اطلاعات اولیه تأیید شد.");
    var firstEvaluation = await db.WorkflowTasks.Include(x => x.WorkflowStep)
        .SingleAsync(x => x.WorkflowInstanceId == instance.Id && x.Status == "Pending");
    Check(firstEvaluation.WorkflowStep.Code.StartsWith("Evaluation_H"),
        "Only after approval may the hierarchical evaluation chain begin.");

    var evaluationActors = new[] { 2, 3, 4 };
    foreach (var actorId in evaluationActors)
    {
        var task = await db.WorkflowTasks
            .Include(x => x.WorkflowStep)
            .Where(x => x.WorkflowInstanceId == instance.Id && x.Status == "Pending")
            .SingleAsync();
        Check(await workflow.GetTaskAsync(task.Id, actorId) is not null,
            $"Expected evaluation stage to be assigned to employee {actorId}.");
        var scoreRows = await db.EmployeePerformanceEvaluationScores
            .Where(x => x.WorkflowInstanceId == instance.Id && x.WorkflowStepId == task.WorkflowStepId)
            .ToListAsync();
        foreach (var score in scoreRows)
        {
            score.Score = 90m;
            score.EvaluatorEmployeeId = actorId;
        }
        await db.SaveChangesAsync();
        await workflow.CompleteTaskAsync(task.Id, actorId, "Approve", "ارزیابی تأیید شد");
    }

    async Task CompletePositionStageAsync(string code, int actorId)
    {
        var task = await db.WorkflowTasks
            .Include(x => x.WorkflowStep)
            .Where(x => x.WorkflowInstanceId == instance.Id && x.Status == "Pending")
            .SingleAsync();
        Check(task.WorkflowStep.Code == code,
            $"Expected {code} stage, but current stage was {task.WorkflowStep.Code}.");
        Check(await workflow.GetTaskAsync(task.Id, actorId) is not null,
            $"Expected {code} stage to be assigned to employee {actorId}.");
        await workflow.CompleteTaskAsync(task.Id, actorId, "Approve", $"تأیید مرحله {code}");
    }

    await CompletePositionStageAsync("Security", 5);
    await CompletePositionStageAsync("HumanResources", 6);

    var ceoTask = await db.WorkflowTasks
        .Include(x => x.WorkflowStep)
        .SingleAsync(x => x.WorkflowInstanceId == instance.Id && x.Status == "Pending");
    Check(ceoTask.WorkflowStep.Code == "CEO" &&
          await workflow.GetTaskAsync(ceoTask.Id, 7) is not null,
        "The final task must be routed to the active CEO employee.");
    await workflow.CompleteTaskAsync(ceoTask.Id, 7, "Approve", "تصمیم نهایی تأیید شد");

    var completed = await db.WorkflowInstances.SingleAsync(x => x.Id == instance.Id);
    Check(completed.Status == "Approved" && completed.CurrentStepId is null && completed.CompletedAt.HasValue,
        "The renewal workflow must finish only after the CEO approves.");
    Check(await db.WorkflowHistory.CountAsync(x => x.WorkflowInstanceId == instance.Id) == 10,
        "The workflow history must retain submission, the return/correction/re-approval loop, and all later approvals.");
    Check(await db.WorkflowTasks.CountAsync(x => x.WorkflowInstanceId == instance.Id && x.Status == "Completed") == 9,
        "The initial approval, requester correction, re-approval, three evaluations, security, HR and CEO tasks must be completed.");

    request.Status = "تکمیل شده";
    request.CompletedAt = DateTime.UtcNow;
    request.SecurityDecision = "ApproveFull";
    request.OriginalEvaluationScore = 90m;
    request.FinalEvaluationScore = 90m;
    request.HrProposedContractMonths = 6;
    request.FinalContractMonths = 6;
    await db.SaveChangesAsync();

    var pdfSteps = await db.WorkflowSteps.AsNoTracking()
        .Include(x => x.OrganizationNode)
        .Where(x => x.WorkflowDefinitionId == instance.WorkflowDefinitionId)
        .OrderBy(x => x.SortOrder).ToListAsync();
    var pdfTasks = await db.WorkflowTasks.AsNoTracking()
        .Include(x => x.WorkflowStep)
        .Include(x => x.AssignedEmployee)
        .Include(x => x.AssignedPosition)
        .Where(x => x.WorkflowInstanceId == instance.Id)
        .OrderBy(x => x.CreatedAt).ToListAsync();
    var pdfScores = await db.EmployeePerformanceEvaluationScores.AsNoTracking()
        .Include(x => x.EvaluatorEmployee)
        .Where(x => x.WorkflowInstanceId == instance.Id).ToListAsync();
    var history = await workflow.GetHistoryAsync(instance.Id);
    QuestPDF.Settings.License = LicenseType.Professional;
    var pdf = new ContractRenewalPdfService().Generate(request, pdfSteps, pdfTasks, pdfScores, history);
    Check(pdf.Length > 1000 && System.Text.Encoding.ASCII.GetString(pdf, 0, 4) == "%PDF",
        "The completed renewal case must generate a valid PDF report.");

    Console.WriteLine("PASS: contract renewal runtime flow from hierarchical evaluation through security, HR, CEO and PDF output.");
}


static async Task VerifyRoleAdministrationAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("role-administration-null-safe")
        .Options;
    await using (var db = new HRPortalDbContext(options))
    {
        var permission = new Permission
        {
            Code = "RoleTest.View", Title = "مشاهده اطلاعات آزمون", Module = "آزمون"
        };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();

        var service = new RoleService(db);
        var roleId = await service.SaveRoleAsync(new RoleEditModel
        {
            Code = "test_role",
            Title = "نقش آزمون",
            Description = null!,
            PermissionIds = null!
        });

        var created = await db.Roles.SingleAsync(x => x.Id == roleId);
        Check(created.Code == "TEST_ROLE" && created.Description == string.Empty && created.IsActive,
            "Creating a role with an empty description or no selected permissions must succeed and normalize the code.");
        Check(!await db.RolePermissions.AnyAsync(x => x.RoleId == roleId),
            "A role with no selected permissions must be saved without creating invalid permission links.");

        await service.SaveRoleAsync(new RoleEditModel
        {
            Id = roleId,
            Code = "TEST_ROLE",
            Title = "نقش آزمون ویرایش‌شده",
            Description = "شرح",
            PermissionIds = [permission.Id]
        });
        Check(await db.RolePermissions.AnyAsync(x => x.RoleId == roleId && x.PermissionId == permission.Id),
            "Editing a role must save its selected permissions.");

        await service.SaveRoleAsync(new RoleEditModel
        {
            Id = roleId,
            Code = "TEST_ROLE",
            Title = "نقش آزمون ویرایش‌شده",
            Description = string.Empty,
            PermissionIds = null!
        });
        Check(!await db.RolePermissions.AnyAsync(x => x.RoleId == roleId),
            "Clearing all role permissions must be supported.");

        db.Roles.Add(new Role { Code = "DUPLICATE_ROLE", Title = "نقش تکراری", IsActive = true });
        await db.SaveChangesAsync();
        var duplicateRejected = false;
        try
        {
            await service.SaveRoleAsync(new RoleEditModel
            {
                Code = "duplicate_role", Title = "نقش دیگر", PermissionIds = []
            });
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        Check(duplicateRejected, "Duplicate role codes must be rejected with a controlled validation error.");
    }

    var migrationOptions = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("role-administration-contract-permission-merge")
        .Options;
    await using (var db = new HRPortalDbContext(migrationOptions))
    {
        var legacyPermission = new Permission
        {
            Code = "EmployeeContracts.Renewal",
            Title = "ثبت و پیگیری تمدید قرارداد",
            Module = "مدیریت قراردادهای کارکنان"
        };
        var misplacedGranularPermission = new Permission
        {
            Code = "ContractRenewal.View",
            Title = "عنوان قدیمی مشاهده تمدید",
            Module = "مدیریت قراردادهای کارکنان"
        };
        var misplacedCompatibilityAlias = new Permission
        {
            Code = "ContractRenewal.Manage",
            Title = "ثبت و پیگیری تمدید قرارداد",
            Module = "مدیریت قراردادهای کارکنان"
        };
        var legacyRole = new Role
        {
            Code = "OLD_RENEWAL_ROLE", Title = "نقش قدیمی تمدید", IsActive = true
        };
        var employee = new Employee
        {
            PersonnelNumber = "ROLE-MIG-01", NationalId = "ROLE-MIG-NID-01",
            FirstName = "کارمند", LastName = "آزمون", Mobile = "09120000000",
            Gender = "مرد", Status = "فعال", IsSystemUser = true
        };
        db.AddRange(legacyPermission, misplacedGranularPermission, misplacedCompatibilityAlias, legacyRole, employee);
        await db.SaveChangesAsync();
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = legacyRole.Id, PermissionId = legacyPermission.Id
        });
        db.RolePermissions.Add(new RolePermission
        {
            RoleId = legacyRole.Id, PermissionId = misplacedCompatibilityAlias.Id
        });
        db.EmployeePermissions.Add(new EmployeePermission
        {
            EmployeeId = employee.Id, PermissionId = legacyPermission.Id
        });
        await db.SaveChangesAsync();

        await new RoleService(db).SeedAsync();

        var trackId = await db.Permissions.Where(x => x.Code == "ContractRenewal.Track")
            .Select(x => x.Id).SingleAsync();
        Check(!await db.Permissions.AnyAsync(x => x.Code == "EmployeeContracts.Renewal"),
            "The legacy employee-contract module permission must be removed after consolidation.");
        Check(await db.RolePermissions.AnyAsync(x => x.RoleId == legacyRole.Id && x.PermissionId == trackId),
            "Legacy role assignments must be transferred to the unified contract renewal tracking permission.");
        Check(await db.EmployeePermissions.AnyAsync(x => x.EmployeeId == employee.Id && x.PermissionId == trackId),
            "Legacy personal permissions must be transferred without losing contract renewal access.");
        Check(!(await new RoleService(db).GetPermissionsAsync()).Any(x => x.Code == "ContractRenewal.Manage"),
            "The technical compatibility permission must not be shown as a configurable user permission.");
        var visibleRenewalPermissions = await db.Permissions.AsNoTracking()
            .Where(x => x.Module == "تمدید قرارداد" && x.Code != "ContractRenewal.Manage")
            .CountAsync();
        Check(visibleRenewalPermissions == 10,
            "The legacy tracking permission must appear alongside the nine existing granular contract-renewal permissions in one module.");
        var unifiedTrackingPermission = await db.Permissions.SingleAsync(x => x.Code == "ContractRenewal.Track");
        Check(unifiedTrackingPermission.Module == "تمدید قرارداد" &&
              unifiedTrackingPermission.Title == "ثبت و پیگیری تمدید قرارداد",
            "The merged permission must remain a separately configurable permission with its requested title.");
        var repairedViewPermission = await db.Permissions.SingleAsync(x => x.Code == "ContractRenewal.View");
        Check(repairedViewPermission.Module == "تمدید قرارداد" &&
              repairedViewPermission.Title == "مشاهده جزئیات و سوابق درخواست تمدید",
            "An existing granular renewal permission under the old module must be repaired in place, not deleted.");
        var repairedCompatibilityAlias = await db.Permissions.SingleAsync(x => x.Code == "ContractRenewal.Manage");
        Check(repairedCompatibilityAlias.Module == "تمدید قرارداد" &&
              await db.RolePermissions.AnyAsync(x => x.RoleId == legacyRole.Id && x.PermissionId == repairedCompatibilityAlias.Id),
            "The hidden renewal compatibility alias must remain assigned for older role checks while belonging to the unified module.");
    }

    Console.WriteLine("PASS: role creation/update with empty optional fields, duplicate-code validation and legacy contract permission consolidation.");
}


static async Task VerifyImprovementActionsAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("monthly-performance-improvement-actions")
        .Options;

    await using var db = new HRPortalDbContext(options);
    var now = MonthlyPerformanceService.NowIran();
    var employee = new Employee
    {
        PersonnelNumber = "IMP-001", NationalId = "IMP-NID-001",
        FirstName = "کارمند", LastName = "آزمایشی", Mobile = "09120000001",
        Gender = "مرد", Status = "فعال", IsSystemUser = true
    };
    var period = new MonthlyPerformancePeriod
    {
        Code = "MP-IMP-1405-08", Title = "آبان ۱۴۰۵", Year = 1405, Month = 8,
        StartAt = now.AddDays(-10), EndAt = now.AddDays(20), IsEnabled = true
    };
    var domain = new MonthlyPerformanceDomain
    {
        Code = "IMP-DOMAIN", Title = "مهارت‌های تخصصی", IsActive = true
    };
    var partialQuestion = new MonthlyPerformanceQuestion
    {
        Code = "IMP-PARTIAL", Title = "تسلط بر ابزارهای کاری",
        Domain = domain, IsActive = true, SortOrder = 1
    };
    var fullQuestion = new MonthlyPerformanceQuestion
    {
        Code = "IMP-FULL", Title = "رعایت نظم کاری",
        Domain = domain, IsActive = true, SortOrder = 2
    };
    db.AddRange(employee, period, domain, partialQuestion, fullQuestion);
    await db.SaveChangesAsync();

    var evaluation = new MonthlyPerformanceEvaluation
    {
        PeriodId = period.Id, EmployeeId = employee.Id,
        OriginalEvaluatorId = 900, LastEvaluatorId = 900,
        RankTitleSnapshot = "کارشناس", MaxScore = 20, TotalScore = 17,
        RowVersion = new byte[8]
    };
    db.MonthlyPerformanceEvaluations.Add(evaluation);
    await db.SaveChangesAsync();

    var partialScore = new MonthlyPerformanceScore
    {
        EvaluationId = evaluation.Id, QuestionId = partialQuestion.Id,
        QuestionTitleSnapshot = partialQuestion.Title, DomainTitleSnapshot = domain.Title,
        MaxScore = 10, Score = 7
    };
    var fullScore = new MonthlyPerformanceScore
    {
        EvaluationId = evaluation.Id, QuestionId = fullQuestion.Id,
        QuestionTitleSnapshot = fullQuestion.Title, DomainTitleSnapshot = domain.Title,
        MaxScore = 10, Score = 10
    };
    db.MonthlyPerformanceScores.AddRange(partialScore, fullScore);
    await db.SaveChangesAsync();

    var service = new MonthlyPerformanceImprovementService(db);
    var action = await service.CreateAsync(
        employee.Id, partialScore.Id, "شرکت در دوره آموزشی", "آموزش و تمرین ابزار تخصصی",
        now.Date.AddDays(14));
    Check(action.Status == MonthlyPerformanceImprovementStatuses.InProgress &&
          action.OwnerEmployeeId == employee.Id && action.ScoreId == partialScore.Id,
        "A new improvement action must belong to the employee and start in the InProgress state.");

    var mine = await service.ListMineAsync(employee.Id);
    Check(mine.Count == 1 && mine[0].QuestionTitle == partialQuestion.Title,
        "The employee personal dashboard must list the employee's own improvement actions.");

    var inScope = await service.ListForScopeAsync([employee.Id], period.Id);
    Check(inScope.Count == 1 && inScope[0].EmployeeId == employee.Id,
        "The evaluator dashboard query must return plans only for employee IDs in its organizational scope.");
    Check((await service.ListForScopeAsync([employee.Id + 10000], period.Id)).Count == 0,
        "The evaluator dashboard must not expose plans belonging to employees outside its organizational scope.");

    var fullScoreRejected = false;
    try
    {
        await service.CreateAsync(employee.Id, fullScore.Id, "اقدام غیرضروری", null, now.Date.AddDays(5));
    }
    catch (InvalidOperationException)
    {
        fullScoreRejected = true;
    }
    Check(fullScoreRejected, "An improvement action must not be created for a question that received its full score.");

    var pastDueRejected = false;
    try
    {
        await service.CreateAsync(employee.Id, partialScore.Id, "اقدام با موعد گذشته", null, now.Date.AddDays(-1));
    }
    catch (InvalidOperationException)
    {
        pastDueRejected = true;
    }
    Check(pastDueRejected, "A new improvement action must have a due date today or in the future.");

    var wrongOwnerRejected = false;
    try
    {
        await service.UpdateAsync(employee.Id + 1, action.Id, "تغییر غیرمجاز", null,
            now.Date.AddDays(10), MonthlyPerformanceImprovementStatuses.Completed);
    }
    catch (UnauthorizedAccessException)
    {
        wrongOwnerRejected = true;
    }
    Check(wrongOwnerRejected, "Only the improvement plan owner may update its content, due date or status.");

    var invalidStatusRejected = false;
    try
    {
        await service.UpdateAsync(employee.Id, action.Id, action.Title, action.Description,
            now.Date.AddDays(10), "UnknownStatus");
    }
    catch (InvalidOperationException)
    {
        invalidStatusRejected = true;
    }
    Check(invalidStatusRejected, "Only InProgress, Completed and Cancelled statuses may be stored.");

    var overdueInProgressRejected = false;
    try
    {
        await service.UpdateAsync(employee.Id, action.Id, action.Title, action.Description,
            now.Date.AddDays(-2), MonthlyPerformanceImprovementStatuses.InProgress);
    }
    catch (InvalidOperationException)
    {
        overdueInProgressRejected = true;
    }
    Check(overdueInProgressRejected,
        "An improvement action cannot remain InProgress with a past deadline.");

    await service.UpdateAsync(employee.Id, action.Id, action.Title, action.Description,
        now.Date.AddDays(-2), MonthlyPerformanceImprovementStatuses.Completed);
    var completed = (await service.ListMineAsync(employee.Id)).Single();
    Check(completed.Status == MonthlyPerformanceImprovementStatuses.Completed,
        "The employee must be able to move an improvement action to Completed.");

    await service.UpdateAsync(employee.Id, action.Id, action.Title, action.Description,
        now.Date.AddDays(-2), MonthlyPerformanceImprovementStatuses.Cancelled);
    var cancelled = (await service.ListMineAsync(employee.Id)).Single();
    Check(cancelled.Status == MonthlyPerformanceImprovementStatuses.Cancelled,
        "The employee must be able to move an improvement action to Cancelled.");

    period.IsDeleted = true;
    await db.SaveChangesAsync();
    var archivedPeriodRejected = false;
    try
    {
        await service.UpdateAsync(employee.Id, action.Id, action.Title, action.Description,
            now.Date.AddDays(10), MonthlyPerformanceImprovementStatuses.Completed);
    }
    catch (InvalidOperationException)
    {
        archivedPeriodRejected = true;
    }
    Check(archivedPeriodRejected, "Improvement actions from archived evaluation periods must be read-only.");

    Console.WriteLine("PASS: improvement action ownership, partial-score eligibility, due dates, status transitions, personal listing and evaluator-scope filtering.");
}



static async Task VerifyWorkflowStageEditingAndRequesterReturnAsync()
{
    var options = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("workflow-stage-editing-requester-return")
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    await using var db = new HRPortalDbContext(options);

    static Employee Person(string code, string firstName) => new()
    {
        PersonnelNumber = code,
        NationalId = "N-" + code,
        FirstName = firstName,
        LastName = "آزمایشی",
        Mobile = "09120000000",
        Gender = "مرد",
        Status = "فعال",
        IsSystemUser = true
    };

    var requester = Person("RET-REQUESTER", "درخواست‌کننده");
    var reviewer = Person("RET-REVIEWER", "بررسی‌کننده");
    var nextReviewer = Person("RET-NEXT", "مسئول بعدی");
    var definition = new WorkflowDefinition
    {
        Code = "RETURN-EDIT-SMOKE", Title = "آزمون مسیر برگشت", IsActive = true
    };
    var reviewStep = new WorkflowStep
    {
        WorkflowDefinition = definition, WorkflowDefinitionId = 1,
        Code = "Review", Title = "بررسی مسئول", SortOrder = 20,
        AssignmentType = "Employee", AssignedEmployeeId = 0,
        AllowApprove = true, AllowReject = true, AllowReturn = true,
        RequireNotes = true, RequireCommentOnReturn = true,
        AllowEditPreviousData = false
    };
    var requesterStep = new WorkflowStep
    {
        WorkflowDefinition = definition, WorkflowDefinitionId = 1,
        Code = "RequesterCorrection", Title = "اصلاح توسط درخواست‌کننده", SortOrder = 30,
        AssignmentType = "Requester", AllowApprove = true, AllowReject = false, AllowReturn = false
    };
    var nextStep = new WorkflowStep
    {
        WorkflowDefinition = definition, WorkflowDefinitionId = 1,
        Code = "NextReview", Title = "مرحله بعدی", SortOrder = 40,
        AssignmentType = "Employee", AssignedEmployeeId = 0
    };
    definition.Steps.Add(reviewStep);
    definition.Steps.Add(requesterStep);
    definition.Steps.Add(nextStep);
    await db.AddRangeAsync(requester, reviewer, nextReviewer, definition);
    await db.SaveChangesAsync();

    reviewStep.AssignedEmployeeId = reviewer.Id;
    nextStep.AssignedEmployeeId = nextReviewer.Id;
    await db.SaveChangesAsync();

    var service = new WorkflowService(db,
        new AuditService(new AuditLogQueue(), new HttpContextAccessor()));

    // The workflow designer posts false explicitly for unchecked checkboxes.
    // Saving stage properties must persist the posted flags and must not overwrite
    // the separate previous-data/notes rules that are edited by SaveStepRulesAsync.
    await service.SaveStepAsync(new WorkflowStepEditModel
    {
        Id = reviewStep.Id,
        WorkflowDefinitionId = definition.Id,
        Code = reviewStep.Code,
        Title = reviewStep.Title,
        SortOrder = reviewStep.SortOrder,
        AssignmentType = "Employee",
        AssignedEmployeeId = reviewer.Id,
        AssignmentMode = "Any",
        AllowApprove = false,
        AllowReject = false,
        AllowReturn = true,
        RequireCommentOnReject = false,
        RequireCommentOnReturn = true,
        IsFinalStep = false
    });
    var savedReviewStep = await db.WorkflowSteps.SingleAsync(x => x.Id == reviewStep.Id);
    Check(!savedReviewStep.AllowApprove && !savedReviewStep.AllowReject &&
          savedReviewStep.AllowReturn && !savedReviewStep.RequireCommentOnReject &&
          savedReviewStep.RequireCommentOnReturn,
        "Saving workflow stage checkboxes must persist both checked and unchecked states.");
    Check(!savedReviewStep.AllowEditPreviousData && savedReviewStep.RequireNotes,
        "Saving general stage properties must preserve settings owned by the separate stage-rules form.");

    var requesterCorrectionFieldDefinition = new WorkflowStepField
    {
        WorkflowStepId = requesterStep.Id,
        Code = "requesterCorrection",
        Title = "شرح اصلاحات",
        FieldType = "TextArea",
        SortOrder = 10,
        IsRequired = true
    };
    var currentReviewFieldDefinition = new WorkflowStepField
    {
        WorkflowStepId = reviewStep.Id,
        Code = "reviewCorrection",
        Title = "اصلاح مرحله بررسی",
        FieldType = "TextArea",
        SortOrder = 10
    };
    var nextReviewFieldDefinition = new WorkflowStepField
    {
        WorkflowStepId = nextStep.Id,
        Code = "nextReviewData",
        Title = "اطلاعات مرحله بازگرداننده",
        FieldType = "TextArea",
        SortOrder = 10
    };
    db.WorkflowStepFields.AddRange(
        requesterCorrectionFieldDefinition,
        currentReviewFieldDefinition,
        nextReviewFieldDefinition);
    db.WorkflowTransitions.AddRange(
        new WorkflowTransition
        {
            WorkflowStepId = reviewStep.Id,
            ToStepId = requesterStep.Id,
            Action = "Return",
            Title = "برگشت به درخواست‌کننده"
        },
        new WorkflowTransition
        {
            WorkflowStepId = requesterStep.Id,
            ToStepId = nextStep.Id,
            Action = "Approve",
            Title = "ادامه پس از اصلاح درخواست‌کننده"
        },
        new WorkflowTransition
        {
            WorkflowStepId = nextStep.Id,
            ToStepId = reviewStep.Id,
            Action = "Return",
            Title = "برگشت به بررسی مسئول برای اصلاح"
        });
    var instance = new WorkflowInstance
    {
        WorkflowDefinitionId = definition.Id,
        WorkflowDefinition = definition,
        EntityType = "Smoke",
        EntityId = "RETURN-1",
        RequesterEmployeeId = requester.Id,
        RequesterEmployee = requester,
        CurrentStepId = reviewStep.Id,
        CurrentStep = reviewStep,
        Status = "InProgress"
    };
    var initialTask = new WorkflowTask
    {
        WorkflowInstance = instance,
        WorkflowInstanceId = 1,
        WorkflowStep = reviewStep,
        WorkflowStepId = reviewStep.Id,
        AssignedEmployeeId = reviewer.Id,
        Status = "Pending"
    };
    instance.Tasks.Add(initialTask);
    await db.AddRangeAsync(instance, initialTask);
    await db.SaveChangesAsync();

    var requesterCorrectionField = await db.WorkflowStepFields
        .SingleAsync(x => x.WorkflowStepId == requesterStep.Id && x.Code == "requesterCorrection");
    db.WorkflowFieldValues.Add(new WorkflowFieldValue
    {
        WorkflowInstanceId = instance.Id,
        WorkflowInstance = instance,
        WorkflowStepId = requesterStep.Id,
        WorkflowStep = requesterStep,
        WorkflowStepFieldId = requesterCorrectionField.Id,
        WorkflowStepField = requesterCorrectionField,
        FieldCode = requesterCorrectionField.Code,
        FieldTitle = requesterCorrectionField.Title,
        FieldType = requesterCorrectionField.FieldType,
        Value = "شرح اصلاح ثبت‌شده",
        UpdatedByEmployeeId = requester.Id
    });
    await db.SaveChangesAsync();

    await service.CompleteTaskAsync(initialTask.Id, reviewer.Id, "Return",
        "اطلاعات درخواست‌کننده باید اصلاح شود.");

    var requesterInbox = await service.GetInboxAsync(requester.Id);
    var requesterTaskRow = requesterInbox.SingleOrDefault(x => x.WorkflowInstanceId == instance.Id);
    Check(requesterTaskRow is not null,
        "A Return route targeting an AssignmentType=Requester stage must create a task in the original requester's inbox.");
    var requesterTask = await service.GetTaskAsync(requesterTaskRow!.TaskId, requester.Id);
    Check(requesterTask is not null && requesterTask.WorkflowStepId == requesterStep.Id,
        "The original requester must be able to open the returned correction task.");
    var fieldGroups = await service.GetTaskFieldGroupsAsync(requesterTaskRow.TaskId, requester.Id);
    Check(fieldGroups.Count == 1 && fieldGroups[0].IsCurrentStep && fieldGroups[0].IsEditable &&
          fieldGroups[0].Fields.Any(x => x.Code == "requesterCorrection"),
        "The requester must receive an editable form for the requester-correction step.");

    await service.CompleteTaskAsync(requesterTaskRow.TaskId, requester.Id, "Approve",
        "اصلاحات ثبت و برای ادامه گردش ارسال شد.",
        values: new Dictionary<string, string?>
        {
            [$"field_{requesterCorrectionField.Id}"] = "شرح اصلاح ثبت‌شده"
        });
    var refreshedInstance = await db.WorkflowInstances.SingleAsync(x => x.Id == instance.Id);
    Check(refreshedInstance.Status == "InProgress" && refreshedInstance.CurrentStepId == nextStep.Id,
        "After requester resubmission, the workflow must continue using the requester step's configured approval route.");
    var nextTask = await db.WorkflowTasks.SingleAsync(x =>
        x.WorkflowInstanceId == instance.Id && x.WorkflowStepId == nextStep.Id && x.Status == "Pending");
    Check(nextTask.AssignedEmployeeId == nextReviewer.Id &&
          (await service.GetInboxAsync(nextReviewer.Id)).Any(x => x.TaskId == nextTask.Id),
        "The next workflow stage must be assigned to its configured employee after requester resubmission.");

    db.WorkflowFieldValues.Add(new WorkflowFieldValue
    {
        WorkflowInstanceId = instance.Id,
        WorkflowInstance = instance,
        WorkflowStepId = nextStep.Id,
        WorkflowStep = nextStep,
        WorkflowStepFieldId = nextReviewFieldDefinition.Id,
        WorkflowStepField = nextReviewFieldDefinition,
        FieldCode = nextReviewFieldDefinition.Code,
        FieldTitle = nextReviewFieldDefinition.Title,
        FieldType = nextReviewFieldDefinition.FieldType,
        Value = "اطلاعات ثبت‌شده در مرحله بعد",
        UpdatedByEmployeeId = nextReviewer.Id
    });
    await db.SaveChangesAsync();

    await service.CompleteTaskAsync(nextTask.Id, nextReviewer.Id, "Return",
        "اطلاعات مرحله قبلی نیازمند اصلاح است.");
    var previousStageInbox = await service.GetInboxAsync(reviewer.Id);
    var returnedToPreviousStage = previousStageInbox.SingleOrDefault(x => x.WorkflowInstanceId == instance.Id);
    Check(returnedToPreviousStage is not null,
        "A Return route targeting an earlier employee stage must return the request to that stage's assignee.");
    var previousStageTask = await service.GetTaskAsync(returnedToPreviousStage!.TaskId, reviewer.Id);
    Check(previousStageTask is not null && previousStageTask.Action == "ReturnedForEdit",
        "Tasks created by any Return route must be marked as returned-for-edit.");

    var previousStageGroups = await service.GetTaskFieldGroupsAsync(returnedToPreviousStage.TaskId, reviewer.Id);
    var editableDestination = previousStageGroups.SingleOrDefault(group => group.WorkflowStepId == reviewStep.Id);
    var readOnlyReturnSource = previousStageGroups.SingleOrDefault(group => group.WorkflowStepId == nextStep.Id);
    Check(previousStageGroups.Count == 2 && editableDestination is { IsCurrentStep: true, IsEditable: true } &&
          editableDestination.Fields.Any(field => field.Code == currentReviewFieldDefinition.Code),
        "The returned-to stage must be editable for its assignee.");
    Check(readOnlyReturnSource is { IsCurrentStep: false, IsEditable: false } &&
          readOnlyReturnSource.Fields.Any(field => field.Code == nextReviewFieldDefinition.Code &&
              field.Value == "اطلاعات ثبت‌شده در مرحله بعد") &&
          previousStageGroups.All(group => group.WorkflowStepId != requesterStep.Id),
        "Only the stage that issued the return is additionally displayed, and its data remains read-only.");

    var returnedSourceEditRejected = false;
    try
    {
        await service.CompleteTaskAsync(returnedToPreviousStage.TaskId, reviewer.Id, "SaveDraft",
            "تلاش برای تغییر داده مرحله بازگرداننده",
            previousValues: new Dictionary<string, string?>
            {
                [nextReviewFieldDefinition.Id.ToString()] = "نباید تغییر کند"
            });
    }
    catch (InvalidOperationException)
    {
        returnedSourceEditRejected = true;
    }
    Check(returnedSourceEditRejected,
        "A task returned to a specific step must not let its recipient edit the source step's read-only data.");

    var preservedSourceValue = await db.WorkflowFieldValues.AsNoTracking()
        .Where(x => x.WorkflowInstanceId == instance.Id && x.WorkflowStepFieldId == nextReviewFieldDefinition.Id)
        .OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
        .Select(x => x.Value).FirstAsync();
    Check(preservedSourceValue == "اطلاعات ثبت‌شده در مرحله بعد",
        "The return-source stage data must remain unchanged after an unauthorized edit attempt.");

    Console.WriteLine("PASS: stage checkbox persistence, preservation of separate stage rules, requester return, specific-stage return with editable destination and read-only source data.");
}


static async Task VerifyWorkflowDirectReturnAndPriorStageEditingAsync()
{
    static Employee Person(string code, string firstName) => new()
    {
        PersonnelNumber = code, NationalId = "N-" + code,
        FirstName = firstName, LastName = "آزمایشی", Mobile = "09120000000",
        Gender = "مرد", Status = "فعال", IsSystemUser = true
    };

    // Direct return: there is deliberately NO Requester-assigned step. The first
    // stage is position-backed, but the return-created task must belong only to
    // the original form starter.
    var directOptions = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("workflow-direct-return-to-starter")
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    await using (var db = new HRPortalDbContext(directOptions))
    {
        var starter = Person("DIR-STARTER", "شروع‌کننده");
        var positionHolder = Person("DIR-HOLDER", "متصدی جایگاه");
        var reviewer = Person("DIR-REVIEWER", "بررسی‌کننده");
        var nextReviewer = Person("DIR-NEXT", "مسئول مرحله بعد");
        var revision = new OrganizationStructureRevision
        {
            RevisionCode = "DIR-ORG-01", Title = "ساختار آزمون برگشت مستقیم",
            EffectiveDate = DateTime.Today, IsFinalized = true, FinalizedAt = DateTime.UtcNow
        };
        var firstPosition = new OrganizationNode
        {
            Revision = revision,
            Code = "DIR-FIRST", Title = "جایگاه مرحله آغازین",
            RankType = "مدیریت", IsActive = true,
            PositionHolderEmployeeId = positionHolder.Id
        };
        var definition = new WorkflowDefinition
        {
            Code = "DIRECT-RETURN-SMOKE", Title = "آزمون برگشت مستقیم", IsActive = true,
            FormCode = "DirectReturnSmoke"
        };
        var startStep = new WorkflowStep
        {
            WorkflowDefinition = definition, Code = "Start", Title = "مرحله آغازین فرم",
            SortOrder = 10, AssignmentType = "Position", OrganizationNodeId = 1,
            AllowApprove = false, AllowReject = false, AllowReturn = false
        };
        var reviewStep = new WorkflowStep
        {
            WorkflowDefinition = definition, Code = "Review", Title = "بررسی مسئول",
            SortOrder = 20, AssignmentType = "Employee", AssignedEmployeeId = 0,
            AllowApprove = true, AllowReturn = true, AllowReject = true,
            RequireCommentOnReturn = true
        };
        var nextStep = new WorkflowStep
        {
            WorkflowDefinition = definition, Code = "Next", Title = "مرحله بعدی",
            SortOrder = 30, AssignmentType = "Employee", AssignedEmployeeId = 0
        };
        definition.Steps.Add(startStep);
        definition.Steps.Add(reviewStep);
        definition.Steps.Add(nextStep);
        var starterField = new WorkflowStepField
        {
            WorkflowStep = startStep, WorkflowStepId = 0, Code = "starterDescription",
            Title = "شرح درخواست", FieldType = "TextArea", IsRequired = true, SortOrder = 10
        };
        var reviewField = new WorkflowStepField
        {
            WorkflowStep = reviewStep, WorkflowStepId = 0, Code = "reviewOpinion",
            Title = "نظر بررسی‌کننده", FieldType = "TextArea", SortOrder = 10
        };
        var nextField = new WorkflowStepField
        {
            WorkflowStep = nextStep, WorkflowStepId = 0, Code = "nextOpinion",
            Title = "اطلاعات مرحله بعد", FieldType = "TextArea", SortOrder = 10
        };
        startStep.Fields.Add(starterField);
        reviewStep.Fields.Add(reviewField);
        nextStep.Fields.Add(nextField);
        db.AddRange(starter, positionHolder, reviewer, nextReviewer, revision,
            firstPosition, definition);
        await db.SaveChangesAsync();
        startStep.OrganizationNodeId = firstPosition.Id;
        reviewStep.AssignedEmployeeId = reviewer.Id;
        nextStep.AssignedEmployeeId = nextReviewer.Id;
        await db.SaveChangesAsync();

        var service = new WorkflowService(db,
            new AuditService(new AuditLogQueue(), new HttpContextAccessor()));
        await service.SaveTransitionAsync(reviewStep.Id, "Return", null, returnToRequester: true);
        await service.SaveTransitionAsync(startStep.Id, "Approve", nextStep.Id);

        var returnRoute = await db.WorkflowTransitions.SingleAsync(x =>
            x.WorkflowStepId == reviewStep.Id && x.Action == "Return");
        Check(returnRoute.ToStepId is null &&
              returnRoute.Title == WorkflowService.ReturnToRequesterTransitionTitle,
            "The workflow designer must store direct-to-starter routing without a separate Requester step.");

        await service.SaveStepAsync(new WorkflowStepEditModel
        {
            Id = startStep.Id, WorkflowDefinitionId = definition.Id,
            Code = startStep.Code, Title = startStep.Title, SortOrder = startStep.SortOrder,
            AssignmentType = "Position", OrganizationNodeId = firstPosition.Id,
            AssignmentMode = "Any", AllowApprove = false, AllowReject = false,
            AllowReturn = false, IsFinalStep = false
        });
        var savedStartStep = await db.WorkflowSteps.SingleAsync(x => x.Id == startStep.Id);

        var instance = new WorkflowInstance
        {
            WorkflowDefinitionId = definition.Id, WorkflowDefinition = definition,
            EntityType = "DirectReturnSmoke", EntityId = "1",
            RequesterEmployeeId = starter.Id, RequesterEmployee = starter,
            CurrentStepId = reviewStep.Id, CurrentStep = reviewStep, Status = "InProgress"
        };
        var reviewTask = new WorkflowTask
        {
            WorkflowInstance = instance, WorkflowStep = reviewStep,
            WorkflowStepId = reviewStep.Id, AssignedEmployeeId = reviewer.Id, Status = "Pending"
        };
        instance.Tasks.Add(reviewTask);
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        db.WorkflowFieldValues.AddRange(
            new WorkflowFieldValue
            {
                WorkflowInstanceId = instance.Id, WorkflowStepId = reviewStep.Id,
                WorkflowStepFieldId = reviewField.Id, FieldCode = reviewField.Code,
                FieldTitle = reviewField.Title, FieldType = reviewField.FieldType,
                Value = "نظر اولیه بررسی", UpdatedByEmployeeId = reviewer.Id
            },
            new WorkflowFieldValue
            {
                WorkflowInstanceId = instance.Id, WorkflowStepId = nextStep.Id,
                WorkflowStepFieldId = nextField.Id, FieldCode = nextField.Code,
                FieldTitle = nextField.Title, FieldType = nextField.FieldType,
                Value = "اطلاعات ثبت‌شده مرحله بعد", UpdatedByEmployeeId = nextReviewer.Id
            });
        await db.SaveChangesAsync();

        await service.CompleteTaskAsync(reviewTask.Id, reviewer.Id, "Return", "برای اصلاح به شروع‌کننده برگشت داده شود.");
        var starterInbox = await service.GetInboxAsync(starter.Id);
        var returnedStarterTask = starterInbox.SingleOrDefault(x => x.WorkflowInstanceId == instance.Id);
        Check(returnedStarterTask is not null,
            "A direct return must appear in the original form starter's inbox even though the reused first stage is position-backed.");
        Check(!(await service.GetInboxAsync(positionHolder.Id)).Any(x => x.WorkflowInstanceId == instance.Id),
            "A direct-to-starter task must not leak into the configured position holder's inbox.");
        var starterTask = await service.GetTaskAsync(returnedStarterTask!.TaskId, starter.Id);
        Check(starterTask is not null && starterTask.WorkflowStepId == startStep.Id &&
              starterTask.Action == "ReturnedForEdit" &&
              starterTask.AssignedEmployeeId == starter.Id,
            "The direct-return task must be assigned to the workflow instance's original requester at the first form step.");

        var starterGroups = await service.GetTaskFieldGroupsAsync(returnedStarterTask.TaskId, starter.Id);
        var startGroup = starterGroups.SingleOrDefault(x => x.WorkflowStepId == startStep.Id);
        var sourceGroup = starterGroups.SingleOrDefault(x => x.WorkflowStepId == reviewStep.Id);
        Check(starterGroups.Count == 2 && startGroup is { IsCurrentStep: true, IsEditable: true } &&
              sourceGroup is { IsCurrentStep: false, IsEditable: false } &&
              sourceGroup.Fields.Any(x => x.Code == reviewField.Code && x.Value == "نظر اولیه بررسی") &&
              starterGroups.All(x => x.WorkflowStepId != nextStep.Id),
            "A direct-return form must show the starter step as editable and the return-source stage as read-only, without unrelated stages.");

        await service.CompleteTaskAsync(returnedStarterTask.TaskId, starter.Id, "Approve",
            "اصلاحات انجام و درخواست ثبت مجدد شد.",
            values: new Dictionary<string, string?>
            {
                [$"field_{starterField.Id}"] = "شرح اصلاح‌شده درخواست"
            });
        var refreshedInstance = await db.WorkflowInstances.SingleAsync(x => x.Id == instance.Id);
        Check(refreshedInstance.Status == "InProgress" && refreshedInstance.CurrentStepId == nextStep.Id,
            "The starter must be able to resubmit even when the reused first stage's normal AllowApprove flag is false.");
        var nextTask = await db.WorkflowTasks.SingleAsync(x =>
            x.WorkflowInstanceId == instance.Id && x.WorkflowStepId == nextStep.Id && x.Status == "Pending");
        Check(nextTask.AssignedEmployeeId == nextReviewer.Id &&
              (await service.GetInboxAsync(nextReviewer.Id)).Any(x => x.TaskId == nextTask.Id),
            "After direct requester resubmission, the workflow must continue on its configured approval route.");
    }

    // AllowEditPreviousData: verify the independent setting survives general stage
    // edits, prior populated fields are displayed as editable and are actually saved.
    var previousOptions = new DbContextOptionsBuilder<HRPortalDbContext>()
        .UseInMemoryDatabase("workflow-allow-edit-previous-data")
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    await using (var db = new HRPortalDbContext(previousOptions))
    {
        var requester = Person("PRE-REQUESTER", "ثبت‌کننده");
        var priorOwner = Person("PRE-OWNER", "مسئول مرحله قبل");
        var currentOwner = Person("PRE-CURRENT", "مسئول مرحله جاری");
        var definition = new WorkflowDefinition
        {
            Code = "ALLOW-EDIT-PREVIOUS-SMOKE", Title = "آزمون ویرایش مراحل قبل", IsActive = true
        };
        var priorStep = new WorkflowStep
        {
            WorkflowDefinition = definition, Code = "Prior", Title = "مرحله قبلی",
            SortOrder = 10, AssignmentType = "Employee", AssignedEmployeeId = 0
        };
        var currentStep = new WorkflowStep
        {
            WorkflowDefinition = definition, Code = "Current", Title = "مرحله جاری",
            SortOrder = 20, AssignmentType = "Employee", AssignedEmployeeId = 0,
            AllowApprove = true
        };
        var futureStep = new WorkflowStep
        {
            WorkflowDefinition = definition, Code = "Future", Title = "مرحله بعدی",
            SortOrder = 30, AssignmentType = "Employee", AssignedEmployeeId = 0
        };
        definition.Steps.Add(priorStep);
        definition.Steps.Add(currentStep);
        definition.Steps.Add(futureStep);
        await db.AddRangeAsync(requester, priorOwner, currentOwner, definition);
        await db.SaveChangesAsync();
        priorStep.AssignedEmployeeId = priorOwner.Id;
        currentStep.AssignedEmployeeId = currentOwner.Id;
        futureStep.AssignedEmployeeId = priorOwner.Id;
        await db.SaveChangesAsync();

        var service = new WorkflowService(db,
            new AuditService(new AuditLogQueue(), new HttpContextAccessor()));
        await service.SaveStepRulesAsync(currentStep.Id, allowEditPreviousData: true, requireNotes: false);
        await service.SaveStepAsync(new WorkflowStepEditModel
        {
            Id = currentStep.Id, WorkflowDefinitionId = definition.Id,
            Code = currentStep.Code, Title = currentStep.Title + " اصلاح‌شده",
            SortOrder = currentStep.SortOrder, AssignmentType = "Employee",
            AssignedEmployeeId = currentOwner.Id, AssignmentMode = "Any",
            AllowApprove = true, AllowReject = false, AllowReturn = false,
            RequireCommentOnReject = false, RequireCommentOnReturn = false, IsFinalStep = false
        });
        var persistedCurrentStep = await db.WorkflowSteps.SingleAsync(x => x.Id == currentStep.Id);
        Check(persistedCurrentStep.AllowEditPreviousData,
            "The general stage editor must not overwrite the saved AllowEditPreviousData rule.");

        var priorField = new WorkflowStepField
        {
            WorkflowStepId = priorStep.Id, Code = "priorValue", Title = "اطلاعات مرحله قبلی",
            FieldType = "Text", SortOrder = 10
        };
        var currentField = new WorkflowStepField
        {
            WorkflowStepId = currentStep.Id, Code = "currentValue", Title = "اطلاعات مرحله جاری",
            FieldType = "Text", SortOrder = 10
        };
        var futureField = new WorkflowStepField
        {
            WorkflowStepId = futureStep.Id, Code = "futureValue", Title = "اطلاعات مرحله بعدی",
            FieldType = "Text", SortOrder = 10
        };
        db.WorkflowStepFields.AddRange(priorField, currentField, futureField);
        var instance = new WorkflowInstance
        {
            WorkflowDefinitionId = definition.Id, WorkflowDefinition = definition,
            EntityType = "AllowEditPreviousSmoke", EntityId = "1",
            RequesterEmployeeId = requester.Id, RequesterEmployee = requester,
            CurrentStepId = currentStep.Id, CurrentStep = currentStep, Status = "InProgress"
        };
        var currentTask = new WorkflowTask
        {
            WorkflowInstance = instance, WorkflowStep = currentStep, WorkflowStepId = currentStep.Id,
            AssignedEmployeeId = currentOwner.Id, Status = "Pending"
        };
        instance.Tasks.Add(currentTask);
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        db.WorkflowFieldValues.AddRange(
            new WorkflowFieldValue
            {
                WorkflowInstanceId = instance.Id, WorkflowStepId = priorStep.Id,
                WorkflowStepFieldId = priorField.Id, FieldCode = priorField.Code,
                FieldTitle = priorField.Title, FieldType = priorField.FieldType,
                Value = "مقدار قبلی", UpdatedByEmployeeId = priorOwner.Id
            },
            new WorkflowFieldValue
            {
                WorkflowInstanceId = instance.Id, WorkflowStepId = futureStep.Id,
                WorkflowStepFieldId = futureField.Id, FieldCode = futureField.Code,
                FieldTitle = futureField.Title, FieldType = futureField.FieldType,
                Value = "مقدار آینده", UpdatedByEmployeeId = priorOwner.Id
            });
        await db.SaveChangesAsync();

        var groups = await service.GetTaskFieldGroupsAsync(currentTask.Id, currentOwner.Id);
        var priorGroup = groups.SingleOrDefault(x => x.WorkflowStepId == priorStep.Id);
        Check(priorGroup is { IsCurrentStep: false, IsEditable: true } &&
              groups.All(x => x.WorkflowStepId != futureStep.Id),
            "When AllowEditPreviousData is enabled, a current-stage assignee must see earlier populated fields as editable, but not future stages.");

        await service.CompleteTaskAsync(currentTask.Id, currentOwner.Id, "SaveDraft",
            "اصلاح داده مرحله قبل",
            previousValues: new Dictionary<string, string?>
            {
                [priorField.Id.ToString()] = "مقدار اصلاح‌شده قبلی"
            });
        var updatedPriorValue = await db.WorkflowFieldValues.AsNoTracking()
            .Where(x => x.WorkflowInstanceId == instance.Id && x.WorkflowStepFieldId == priorField.Id)
            .OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .Select(x => x.Value).FirstAsync();
        Check(updatedPriorValue == "مقدار اصلاح‌شده قبلی",
            "A stage with AllowEditPreviousData enabled must persist the changed value of an earlier stage field.");
    }

    Console.WriteLine("PASS: direct return to original form starter without a requester step; editable destination/read-only return source; workflow resubmission; and verified AllowEditPreviousData persistence.");
}

static void VerifyWorkflowReportFormTypeAggregation()
{
    var formTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Suggestions"] = "ثبت و بررسی پیشنهادات"
    };
    var samples = new[]
    {
        (FormCode: (string?)null, EntityType: "ContractRenewalRequest"),
        (FormCode: (string?)null, EntityType: "ContractRenewalRequest"),
        (FormCode: (string?)null, EntityType: "ContractRenewalRequest"),
        (FormCode: (string?)"Suggestions", EntityType: "Suggestion"),
        (FormCode: (string?)"OutsourcedServices", EntityType: "OutsourcedServiceRequest")
    };
    var options = samples
        .GroupBy(x => WorkflowReportFormTypeResolver.ResolveCode(x.FormCode, x.EntityType),
            StringComparer.OrdinalIgnoreCase)
        .Select(group => new
        {
            Code = group.Key,
            Title = WorkflowReportFormTypeResolver.ResolveTitle(group.Key, formTitles),
            Count = group.Count()
        }).ToList();

    var renewal = options.Single(x => x.Code == "ContractRenewalRequest");
    var suggestions = options.Single(x => x.Code == "Suggestions");
    var outsourced = options.Single(x => x.Code == "OutsourcedServices");
    Check(options.Count == 3 && renewal.Count == 3 && renewal.Title == "تمدید قرارداد",
        "Separate internal contract-renewal workflow definitions must aggregate under one form option with the total request count.");
    Check(suggestions.Count == 1 && suggestions.Title == "ثبت و بررسی پیشنهادات" &&
          outsourced.Count == 1 && outsourced.Title == "سفارش خدمات برونسپاری",
        "Different operational forms must remain separate and use their registered or stable Persian form titles.");
    Console.WriteLine("PASS: activity report form-type aggregation consolidates runtime definitions and exposes per-form counts.");
}
