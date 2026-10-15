using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class ContractManagementSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.EmployeePerformanceEvaluationQuestions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeePerformanceEvaluationQuestions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EmployeePerformanceEvaluationQuestions] PRIMARY KEY,
        [QuestionText] nvarchar(1000) NOT NULL,
        [MaxScore] decimal(5,2) NOT NULL,
        [ScoringGuidance] nvarchar(4000) NOT NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_EmployeePerformanceEvaluationQuestions_SortOrder] DEFAULT 10,
        [IsActive] bit NOT NULL CONSTRAINT [DF_EmployeePerformanceEvaluationQuestions_IsActive] DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_EmployeePerformanceEvaluationQuestions_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_EmployeePerformanceEvaluationQuestions_UpdatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.EmployeePerformanceEvaluationScores', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeePerformanceEvaluationScores](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EmployeePerformanceEvaluationScores] PRIMARY KEY,
        [QuestionId] int NOT NULL,
        [WorkflowInstanceId] int NOT NULL,
        [WorkflowStepId] int NOT NULL,
        [EvaluatorEmployeeId] int NULL,
        [Score] decimal(5,2) NULL,
        [MaxScoreSnapshot] decimal(5,2) NOT NULL,
        [QuestionTextSnapshot] nvarchar(1000) NOT NULL,
        [ScoringGuidanceSnapshot] nvarchar(4000) NOT NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_EmployeePerformanceEvaluationScores_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.EmployeePerformanceEvaluationScores', N'U') IS NOT NULL AND COLUMNPROPERTY(OBJECT_ID(N'dbo.EmployeePerformanceEvaluationScores'), N'Score', 'AllowsNull') = 0
    ALTER TABLE [dbo].[EmployeePerformanceEvaluationScores] ALTER COLUMN [Score] decimal(5,2) NULL;

IF OBJECT_ID(N'dbo.ContractRenewalWorkflowSettings', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ContractRenewalWorkflowSettings](
        [Id] int NOT NULL CONSTRAINT [PK_ContractRenewalWorkflowSettings] PRIMARY KEY,
        [SecurityPositionId] int NULL,
        [HumanResourcesPositionId] int NULL,
        [CeoEmployeeId] int NULL,
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_ContractRenewalWorkflowSettings_UpdatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ContractRenewalRequests](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContractRenewalRequests] PRIMARY KEY,
        [RequestCode] nvarchar(40) NOT NULL,
        [EmployeeId] int NOT NULL,
        [CreatedByEmployeeId] int NULL,
        [WorkflowInstanceId] int NULL,
        [Status] nvarchar(100) NOT NULL,
        [EmployeeNameSnapshot] nvarchar(200) NOT NULL,
        [PersonnelNumberSnapshot] nvarchar(50) NOT NULL,
        [OrganizationUnitSnapshot] nvarchar(200) NOT NULL,
        [PositionTitleSnapshot] nvarchar(300) NOT NULL,
        [PreviousContractEndDate] date NULL,
        [PreviousExtensionMonths1] int NOT NULL,
        [PreviousExtensionMonths2] int NOT NULL,
        [PreviousExtensionMonths3] int NOT NULL,
        [HasDisciplinaryWarnings] bit NOT NULL,
        [DisciplinaryWarningCount] int NOT NULL,
        [HasDisciplinaryCommitteeResolution] bit NOT NULL,
        [DisciplinaryCommitteeResolutionCount] int NOT NULL,
        [HasUnpaidLeave] bit NOT NULL,
        [UnpaidLeaveDays] int NOT NULL,
        [HasSickLeave] bit NOT NULL,
        [SickLeaveDays] int NOT NULL,
        [HasAccidentLeave] bit NOT NULL,
        [AccidentLeaveDays] int NOT NULL,
        [HasCommendations] bit NOT NULL,
        [CommendationCount] int NOT NULL,
        [PreviousPeriodScore1] decimal(5,2) NOT NULL,
        [PreviousPeriodScore2] decimal(5,2) NOT NULL,
        [PreviousPeriodScore1FromHistory] bit NOT NULL,
        [PreviousPeriodScore2FromHistory] bit NOT NULL,
        [OriginalEvaluationScore] decimal(5,2) NULL,
        [FinalEvaluationScore] decimal(5,2) NULL,
        [HrDisciplinaryDeductionPercent] decimal(5,2) NOT NULL CONSTRAINT [DF_ContractRenewalRequests_HrDisciplinaryDeductionPercent] DEFAULT 0,
        [SecurityDecision] nvarchar(30) NULL,
        [SecurityComment] nvarchar(3000) NULL,
        [HrProposedContractMonths] int NULL,
        [HrComment] nvarchar(3000) NULL,
        [FinalContractMonths] int NULL,
        [CeoComment] nvarchar(3000) NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ContractRenewalRequests_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [CompletedAt] datetime2 NULL
    );
END;

IF OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ContractRenewalRequests', N'OriginalEvaluationScore') IS NULL
    ALTER TABLE [dbo].[ContractRenewalRequests] ADD [OriginalEvaluationScore] decimal(5,2) NULL;
IF OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ContractRenewalRequests', N'HrDisciplinaryDeductionPercent') IS NULL
    ALTER TABLE [dbo].[ContractRenewalRequests] ADD [HrDisciplinaryDeductionPercent] decimal(5,2) NOT NULL CONSTRAINT [DF_ContractRenewalRequests_HrDisciplinaryDeductionPercent] DEFAULT 0;

IF OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.ContractRenewalRequests', N'Status') IS NOT NULL
AND COL_LENGTH(N'dbo.ContractRenewalRequests', N'Status') < 200
    ALTER TABLE [dbo].[ContractRenewalRequests] ALTER COLUMN [Status] nvarchar(100) NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_ContractRenewalRequests_RequestCode' AND object_id=OBJECT_ID(N'dbo.ContractRenewalRequests'))
    CREATE UNIQUE INDEX [IX_ContractRenewalRequests_RequestCode] ON [dbo].[ContractRenewalRequests]([RequestCode]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_ContractRenewalRequests_Employee_CreatedAt' AND object_id=OBJECT_ID(N'dbo.ContractRenewalRequests'))
    CREATE INDEX [IX_ContractRenewalRequests_Employee_CreatedAt] ON [dbo].[ContractRenewalRequests]([EmployeeId],[CreatedAt]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_ContractRenewalRequests_Status_CreatedAt' AND object_id=OBJECT_ID(N'dbo.ContractRenewalRequests'))
    CREATE INDEX [IX_ContractRenewalRequests_Status_CreatedAt] ON [dbo].[ContractRenewalRequests]([Status],[CreatedAt]);

IF OBJECT_ID(N'dbo.ContractRenewalWorkflowSettings', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.OrganizationNodes', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ContractRenewalWorkflowSettings_SecurityPosition')
    ALTER TABLE [dbo].[ContractRenewalWorkflowSettings] ADD CONSTRAINT [FK_ContractRenewalWorkflowSettings_SecurityPosition]
    FOREIGN KEY([SecurityPositionId]) REFERENCES [dbo].[OrganizationNodes]([Id]) ON DELETE NO ACTION;
IF OBJECT_ID(N'dbo.ContractRenewalWorkflowSettings', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.OrganizationNodes', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ContractRenewalWorkflowSettings_HumanResourcesPosition')
    ALTER TABLE [dbo].[ContractRenewalWorkflowSettings] ADD CONSTRAINT [FK_ContractRenewalWorkflowSettings_HumanResourcesPosition]
    FOREIGN KEY([HumanResourcesPositionId]) REFERENCES [dbo].[OrganizationNodes]([Id]) ON DELETE NO ACTION;
IF OBJECT_ID(N'dbo.ContractRenewalWorkflowSettings', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ContractRenewalWorkflowSettings_CeoEmployee')
    ALTER TABLE [dbo].[ContractRenewalWorkflowSettings] ADD CONSTRAINT [FK_ContractRenewalWorkflowSettings_CeoEmployee]
    FOREIGN KEY([CeoEmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ContractRenewalRequests_Employee')
    ALTER TABLE [dbo].[ContractRenewalRequests] ADD CONSTRAINT [FK_ContractRenewalRequests_Employee]
    FOREIGN KEY([EmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE NO ACTION;
IF OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ContractRenewalRequests_CreatedByEmployee')
    ALTER TABLE [dbo].[ContractRenewalRequests] ADD CONSTRAINT [FK_ContractRenewalRequests_CreatedByEmployee]
    FOREIGN KEY([CreatedByEmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE SET NULL;
IF OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.WorkflowInstances', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ContractRenewalRequests_WorkflowInstance')
    ALTER TABLE [dbo].[ContractRenewalRequests] ADD CONSTRAINT [FK_ContractRenewalRequests_WorkflowInstance]
    FOREIGN KEY([WorkflowInstanceId]) REFERENCES [dbo].[WorkflowInstances]([Id]) ON DELETE NO ACTION;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_EmployeePerformanceEvaluationQuestions_Active_Order' AND object_id=OBJECT_ID(N'dbo.EmployeePerformanceEvaluationQuestions'))
    CREATE INDEX [IX_EmployeePerformanceEvaluationQuestions_Active_Order] ON [dbo].[EmployeePerformanceEvaluationQuestions]([IsActive],[SortOrder],[Id]);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_EmployeePerformanceEvaluationScores_Question_Instance' AND object_id=OBJECT_ID(N'dbo.EmployeePerformanceEvaluationScores'))
    CREATE INDEX [IX_EmployeePerformanceEvaluationScores_Question_Instance] ON [dbo].[EmployeePerformanceEvaluationScores]([QuestionId],[WorkflowInstanceId]);

IF OBJECT_ID(N'dbo.EmployeePerformanceEvaluationScores', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.EmployeePerformanceEvaluationQuestions', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_EmployeePerformanceEvaluationScores_Question')
    ALTER TABLE [dbo].[EmployeePerformanceEvaluationScores] ADD CONSTRAINT [FK_EmployeePerformanceEvaluationScores_Question]
    FOREIGN KEY([QuestionId]) REFERENCES [dbo].[EmployeePerformanceEvaluationQuestions]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.EmployeePerformanceEvaluationScores', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.WorkflowInstances', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_EmployeePerformanceEvaluationScores_Instance')
    ALTER TABLE [dbo].[EmployeePerformanceEvaluationScores] ADD CONSTRAINT [FK_EmployeePerformanceEvaluationScores_Instance]
    FOREIGN KEY([WorkflowInstanceId]) REFERENCES [dbo].[WorkflowInstances]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.EmployeePerformanceEvaluationScores', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.WorkflowSteps', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_EmployeePerformanceEvaluationScores_Step')
    ALTER TABLE [dbo].[EmployeePerformanceEvaluationScores] ADD CONSTRAINT [FK_EmployeePerformanceEvaluationScores_Step]
    FOREIGN KEY([WorkflowStepId]) REFERENCES [dbo].[WorkflowSteps]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.EmployeePerformanceEvaluationScores', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_EmployeePerformanceEvaluationScores_Evaluator')
    ALTER TABLE [dbo].[EmployeePerformanceEvaluationScores] ADD CONSTRAINT [FK_EmployeePerformanceEvaluationScores_Evaluator]
    FOREIGN KEY([EvaluatorEmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE SET NULL;
";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
