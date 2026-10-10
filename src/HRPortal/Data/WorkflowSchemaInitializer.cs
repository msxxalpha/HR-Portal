using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class WorkflowSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.WorkflowDefinitions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowDefinitions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkflowDefinitions] PRIMARY KEY,
        [Code] nvarchar(100) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_WorkflowDefinitions_IsActive] DEFAULT 1,
        [Version] int NOT NULL CONSTRAINT [DF_WorkflowDefinitions_Version] DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_WorkflowDefinitions_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_WorkflowDefinitions_UpdatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.WorkflowSteps', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowSteps](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkflowSteps] PRIMARY KEY,
        [WorkflowDefinitionId] int NOT NULL,
        [Code] nvarchar(100) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_WorkflowSteps_SortOrder] DEFAULT 0,
        [AssignmentType] nvarchar(30) NOT NULL CONSTRAINT [DF_WorkflowSteps_AssignmentType] DEFAULT N'Position',
        [OrganizationNodeId] int NULL,
        [AssignmentMode] nvarchar(20) NOT NULL CONSTRAINT [DF_WorkflowSteps_AssignmentMode] DEFAULT N'Any',
        [AllowApprove] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_AllowApprove] DEFAULT 1,
        [AllowReject] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_AllowReject] DEFAULT 1,
        [AllowReturn] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_AllowReturn] DEFAULT 1,
        [RequireCommentOnReject] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_RequireCommentOnReject] DEFAULT 1,
        [RequireCommentOnReturn] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_RequireCommentOnReturn] DEFAULT 0,
        [IsFinalStep] bit NOT NULL CONSTRAINT [DF_WorkflowSteps_IsFinalStep] DEFAULT 0
    );
END;

IF OBJECT_ID(N'dbo.WorkflowTransitions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowTransitions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkflowTransitions] PRIMARY KEY,
        [WorkflowStepId] int NOT NULL,
        [ToStepId] int NULL,
        [Action] nvarchar(30) NOT NULL,
        [Title] nvarchar(100) NULL
    );
END;

IF OBJECT_ID(N'dbo.WorkflowInstances', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowInstances](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkflowInstances] PRIMARY KEY,
        [WorkflowDefinitionId] int NOT NULL,
        [EntityType] nvarchar(100) NOT NULL,
        [EntityId] nvarchar(100) NOT NULL,
        [RequesterEmployeeId] int NOT NULL,
        [CurrentStepId] int NULL,
        [Status] nvarchar(30) NOT NULL,
        [StartedAt] datetime2 NOT NULL CONSTRAINT [DF_WorkflowInstances_StartedAt] DEFAULT SYSUTCDATETIME(),
        [CompletedAt] datetime2 NULL
    );
END;

IF OBJECT_ID(N'dbo.WorkflowTasks', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowTasks](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkflowTasks] PRIMARY KEY,
        [WorkflowInstanceId] int NOT NULL,
        [WorkflowStepId] int NOT NULL,
        [AssignedPositionId] int NULL,
        [AssignedEmployeeId] int NULL,
        [Status] nvarchar(30) NOT NULL CONSTRAINT [DF_WorkflowTasks_Status] DEFAULT N'Pending',
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_WorkflowTasks_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [ClaimedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [Action] nvarchar(30) NULL,
        [Comment] nvarchar(2000) NULL
    );
END;

IF OBJECT_ID(N'dbo.WorkflowHistory', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowHistory](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkflowHistory] PRIMARY KEY,
        [WorkflowInstanceId] int NOT NULL,
        [WorkflowStepId] int NULL,
        [FromStatus] nvarchar(30) NOT NULL,
        [ToStatus] nvarchar(30) NOT NULL,
        [Action] nvarchar(30) NOT NULL,
        [ActorEmployeeId] int NULL,
        [ActorPositionId] int NULL,
        [Comment] nvarchar(2000) NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_WorkflowHistory_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.WorkflowStepFields', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowStepFields](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkflowStepFields] PRIMARY KEY,
        [WorkflowStepId] int NOT NULL,
        [Code] nvarchar(100) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [FieldType] nvarchar(30) NOT NULL CONSTRAINT [DF_WorkflowStepFields_FieldType] DEFAULT N'Text',
        [Options] nvarchar(4000) NULL,
        [HelpText] nvarchar(1000) NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_WorkflowStepFields_SortOrder] DEFAULT 0,
        [IsRequired] bit NOT NULL CONSTRAINT [DF_WorkflowStepFields_IsRequired] DEFAULT 0,
        [MaxLength] int NULL
    );
END;

IF OBJECT_ID(N'dbo.WorkflowFieldValues', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowFieldValues](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkflowFieldValues] PRIMARY KEY,
        [WorkflowInstanceId] int NOT NULL,
        [WorkflowStepId] int NOT NULL,
        [WorkflowStepFieldId] int NOT NULL,
        [FieldCode] nvarchar(100) NOT NULL,
        [FieldTitle] nvarchar(200) NOT NULL,
        [FieldType] nvarchar(30) NOT NULL,
        [Value] nvarchar(4000) NULL,
        [UpdatedByEmployeeId] int NULL,
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_WorkflowFieldValues_UpdatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowDefinitions_Code' AND object_id=OBJECT_ID(N'dbo.WorkflowDefinitions'))
    DROP INDEX [IX_WorkflowDefinitions_Code] ON [dbo].[WorkflowDefinitions];
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowDefinitions_Code_Version' AND object_id=OBJECT_ID(N'dbo.WorkflowDefinitions'))
    EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_WorkflowDefinitions_Code_Version] ON [dbo].[WorkflowDefinitions]([Code],[Version]);';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowSteps_Definition_Sort' AND object_id=OBJECT_ID(N'dbo.WorkflowSteps'))
    EXEC sys.sp_executesql N'CREATE INDEX [IX_WorkflowSteps_Definition_Sort] ON [dbo].[WorkflowSteps]([WorkflowDefinitionId],[SortOrder]);';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowTransitions_Step_Action' AND object_id=OBJECT_ID(N'dbo.WorkflowTransitions'))
    EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_WorkflowTransitions_Step_Action] ON [dbo].[WorkflowTransitions]([WorkflowStepId],[Action]);';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowInstances_Entity' AND object_id=OBJECT_ID(N'dbo.WorkflowInstances'))
    EXEC sys.sp_executesql N'CREATE INDEX [IX_WorkflowInstances_Entity] ON [dbo].[WorkflowInstances]([EntityType],[EntityId]);';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowTasks_Inbox' AND object_id=OBJECT_ID(N'dbo.WorkflowTasks'))
    EXEC sys.sp_executesql N'CREATE INDEX [IX_WorkflowTasks_Inbox] ON [dbo].[WorkflowTasks]([AssignedEmployeeId],[Status],[CreatedAt]);';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowTasks_PositionInbox' AND object_id=OBJECT_ID(N'dbo.WorkflowTasks'))
    EXEC sys.sp_executesql N'CREATE INDEX [IX_WorkflowTasks_PositionInbox] ON [dbo].[WorkflowTasks]([AssignedPositionId],[Status],[CreatedAt]);';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowHistory_Instance_Created' AND object_id=OBJECT_ID(N'dbo.WorkflowHistory'))
    EXEC sys.sp_executesql N'CREATE INDEX [IX_WorkflowHistory_Instance_Created] ON [dbo].[WorkflowHistory]([WorkflowInstanceId],[CreatedAt]);';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowStepFields_Step_Code' AND object_id=OBJECT_ID(N'dbo.WorkflowStepFields'))
    EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_WorkflowStepFields_Step_Code] ON [dbo].[WorkflowStepFields]([WorkflowStepId],[Code]);';

IF EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowFieldValues_Instance_Field' AND object_id=OBJECT_ID(N'dbo.WorkflowFieldValues') AND is_unique=1)
    DROP INDEX [IX_WorkflowFieldValues_Instance_Field] ON [dbo].[WorkflowFieldValues];

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_WorkflowFieldValues_Instance_Field' AND object_id=OBJECT_ID(N'dbo.WorkflowFieldValues'))
    EXEC sys.sp_executesql N'CREATE INDEX [IX_WorkflowFieldValues_Instance_Field] ON [dbo].[WorkflowFieldValues]([WorkflowInstanceId],[WorkflowStepFieldId],[UpdatedAt]);';

IF OBJECT_ID(N'dbo.WorkflowSteps', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.WorkflowDefinitions', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowSteps_WorkflowDefinitions')
    ALTER TABLE [dbo].[WorkflowSteps] ADD CONSTRAINT [FK_WorkflowSteps_WorkflowDefinitions]
    FOREIGN KEY([WorkflowDefinitionId]) REFERENCES [dbo].[WorkflowDefinitions]([Id]) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.WorkflowSteps', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.OrganizationNodes', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowSteps_OrganizationNodes')
    ALTER TABLE [dbo].[WorkflowSteps] ADD CONSTRAINT [FK_WorkflowSteps_OrganizationNodes]
    FOREIGN KEY([OrganizationNodeId]) REFERENCES [dbo].[OrganizationNodes]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowTransitions', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowTransitions_WorkflowSteps')
    ALTER TABLE [dbo].[WorkflowTransitions] ADD CONSTRAINT [FK_WorkflowTransitions_WorkflowSteps]
    FOREIGN KEY([WorkflowStepId]) REFERENCES [dbo].[WorkflowSteps]([Id]) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.WorkflowTransitions', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowTransitions_ToStep')
    ALTER TABLE [dbo].[WorkflowTransitions] ADD CONSTRAINT [FK_WorkflowTransitions_ToStep]
    FOREIGN KEY([ToStepId]) REFERENCES [dbo].[WorkflowSteps]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowInstances', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowInstances_Definition')
    ALTER TABLE [dbo].[WorkflowInstances] ADD CONSTRAINT [FK_WorkflowInstances_Definition]
    FOREIGN KEY([WorkflowDefinitionId]) REFERENCES [dbo].[WorkflowDefinitions]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowInstances', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowInstances_Requester')
    ALTER TABLE [dbo].[WorkflowInstances] ADD CONSTRAINT [FK_WorkflowInstances_Requester]
    FOREIGN KEY([RequesterEmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowInstances', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowInstances_CurrentStep')
    ALTER TABLE [dbo].[WorkflowInstances] ADD CONSTRAINT [FK_WorkflowInstances_CurrentStep]
    FOREIGN KEY([CurrentStepId]) REFERENCES [dbo].[WorkflowSteps]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowTasks', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowTasks_Instance')
    ALTER TABLE [dbo].[WorkflowTasks] ADD CONSTRAINT [FK_WorkflowTasks_Instance]
    FOREIGN KEY([WorkflowInstanceId]) REFERENCES [dbo].[WorkflowInstances]([Id]) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.WorkflowTasks', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowTasks_Step')
    ALTER TABLE [dbo].[WorkflowTasks] ADD CONSTRAINT [FK_WorkflowTasks_Step]
    FOREIGN KEY([WorkflowStepId]) REFERENCES [dbo].[WorkflowSteps]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowTasks', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowTasks_Position')
    ALTER TABLE [dbo].[WorkflowTasks] ADD CONSTRAINT [FK_WorkflowTasks_Position]
    FOREIGN KEY([AssignedPositionId]) REFERENCES [dbo].[OrganizationNodes]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowTasks', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowTasks_Employee')
    ALTER TABLE [dbo].[WorkflowTasks] ADD CONSTRAINT [FK_WorkflowTasks_Employee]
    FOREIGN KEY([AssignedEmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.WorkflowStepFields', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowStepFields_Step')
    ALTER TABLE [dbo].[WorkflowStepFields] ADD CONSTRAINT [FK_WorkflowStepFields_Step]
    FOREIGN KEY([WorkflowStepId]) REFERENCES [dbo].[WorkflowSteps]([Id]) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.WorkflowFieldValues', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowFieldValues_Instance')
    ALTER TABLE [dbo].[WorkflowFieldValues] ADD CONSTRAINT [FK_WorkflowFieldValues_Instance]
    FOREIGN KEY([WorkflowInstanceId]) REFERENCES [dbo].[WorkflowInstances]([Id]) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.WorkflowFieldValues', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowFieldValues_Step')
    ALTER TABLE [dbo].[WorkflowFieldValues] ADD CONSTRAINT [FK_WorkflowFieldValues_Step]
    FOREIGN KEY([WorkflowStepId]) REFERENCES [dbo].[WorkflowSteps]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowFieldValues', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowFieldValues_Field')
    ALTER TABLE [dbo].[WorkflowFieldValues] ADD CONSTRAINT [FK_WorkflowFieldValues_Field]
    FOREIGN KEY([WorkflowStepFieldId]) REFERENCES [dbo].[WorkflowStepFields]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowFieldValues', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowFieldValues_UpdatedBy')
    ALTER TABLE [dbo].[WorkflowFieldValues] ADD CONSTRAINT [FK_WorkflowFieldValues_UpdatedBy]
    FOREIGN KEY([UpdatedByEmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.WorkflowHistory', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowHistory_Instance')
    ALTER TABLE [dbo].[WorkflowHistory] ADD CONSTRAINT [FK_WorkflowHistory_Instance]
    FOREIGN KEY([WorkflowInstanceId]) REFERENCES [dbo].[WorkflowInstances]([Id]) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.WorkflowHistory', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowHistory_Step')
    ALTER TABLE [dbo].[WorkflowHistory] ADD CONSTRAINT [FK_WorkflowHistory_Step]
    FOREIGN KEY([WorkflowStepId]) REFERENCES [dbo].[WorkflowSteps]([Id]) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.WorkflowHistory', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowHistory_ActorEmployee')
    ALTER TABLE [dbo].[WorkflowHistory] ADD CONSTRAINT [FK_WorkflowHistory_ActorEmployee]
    FOREIGN KEY([ActorEmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.WorkflowHistory', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_WorkflowHistory_ActorPosition')
    ALTER TABLE [dbo].[WorkflowHistory] ADD CONSTRAINT [FK_WorkflowHistory_ActorPosition]
    FOREIGN KEY([ActorPositionId]) REFERENCES [dbo].[OrganizationNodes]([Id]) ON DELETE SET NULL;
";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
