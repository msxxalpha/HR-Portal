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
