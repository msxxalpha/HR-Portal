using HRPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Modules.Performance;

/// <summary>
/// Additive, rerunnable SQL Server bootstrap. Never drops or alters existing HRPortal tables.
/// Run only after HRPortal's existing initializer completes.
/// </summary>
public static class PerformanceSchemaInitializer
{
    public static async Task EnsureAsync(HRPortalDbContext db)
    {
        const string sql = """
IF OBJECT_ID(N'dbo.PerformanceDomains', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformanceDomains(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Code nvarchar(50) NOT NULL,
        Title nvarchar(200) NOT NULL,
        Description nvarchar(1000) NULL,
        IsActive bit NOT NULL DEFAULT 1
    );
END;
IF OBJECT_ID(N'dbo.PerformancePositions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformancePositions(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Code nvarchar(50) NOT NULL,
        Title nvarchar(200) NOT NULL,
        IsActive bit NOT NULL DEFAULT 1
    );
END;
IF OBJECT_ID(N'dbo.PerformanceQuestions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformanceQuestions(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        DomainId int NOT NULL,
        Code nvarchar(50) NOT NULL,
        Title nvarchar(250) NOT NULL,
        [Text] nvarchar(2000) NOT NULL,
        IsActive bit NOT NULL DEFAULT 1,
        CONSTRAINT FK_PerformanceQuestions_Domains FOREIGN KEY(DomainId) REFERENCES dbo.PerformanceDomains(Id)
    );
END;
IF OBJECT_ID(N'dbo.PerformancePositionQuestions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformancePositionQuestions(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PositionId int NOT NULL,
        QuestionId int NOT NULL,
        MaxScore decimal(12,2) NOT NULL,
        IsActive bit NOT NULL DEFAULT 1,
        SortOrder int NOT NULL DEFAULT 1,
        CONSTRAINT FK_PerformancePositionQuestions_Positions FOREIGN KEY(PositionId) REFERENCES dbo.PerformancePositions(Id),
        CONSTRAINT FK_PerformancePositionQuestions_Questions FOREIGN KEY(QuestionId) REFERENCES dbo.PerformanceQuestions(Id)
    );
END;
IF OBJECT_ID(N'dbo.PerformanceEmployeePositions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformanceEmployeePositions(
        EmployeeId int NOT NULL PRIMARY KEY,
        PositionId int NOT NULL,
        CONSTRAINT FK_PerformanceEmployeePositions_Employees FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(Id),
        CONSTRAINT FK_PerformanceEmployeePositions_Positions FOREIGN KEY(PositionId) REFERENCES dbo.PerformancePositions(Id)
    );
END;
IF OBJECT_ID(N'dbo.PerformancePeriods', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformancePeriods(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title nvarchar(200) NOT NULL,
        StartAt datetime2 NOT NULL,
        EndAt datetime2 NOT NULL,
        OrganizationStructureRevisionId int NOT NULL,
        IsOpen bit NOT NULL DEFAULT 1,
        IsDeleted bit NOT NULL DEFAULT 0,
        DeletedAt datetime2 NULL,
        CONSTRAINT FK_PerformancePeriods_Revisions FOREIGN KEY(OrganizationStructureRevisionId) REFERENCES dbo.OrganizationStructureRevisions(Id)
    );
END;
IF OBJECT_ID(N'dbo.PerformanceAssignments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformanceAssignments(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PeriodId int NOT NULL,
        EmployeeId int NOT NULL,
        EvaluatorEmployeeId int NOT NULL,
        CONSTRAINT FK_PerformanceAssignments_Periods FOREIGN KEY(PeriodId) REFERENCES dbo.PerformancePeriods(Id),
        CONSTRAINT FK_PerformanceAssignments_Employees FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(Id),
        CONSTRAINT FK_PerformanceAssignments_Evaluators FOREIGN KEY(EvaluatorEmployeeId) REFERENCES dbo.Employees(Id)
    );
END;
IF OBJECT_ID(N'dbo.PerformanceEvaluations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformanceEvaluations(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PeriodId int NOT NULL,
        EmployeeId int NOT NULL,
        EvaluatorEmployeeId int NOT NULL,
        FinalScore decimal(12,2) NOT NULL DEFAULT 0,
        FinalMaxScore decimal(12,2) NOT NULL DEFAULT 0,
        Status nvarchar(30) NOT NULL DEFAULT N'Submitted',
        UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PerformanceEvaluations_Periods FOREIGN KEY(PeriodId) REFERENCES dbo.PerformancePeriods(Id),
        CONSTRAINT FK_PerformanceEvaluations_Employees FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(Id),
        CONSTRAINT FK_PerformanceEvaluations_Evaluators FOREIGN KEY(EvaluatorEmployeeId) REFERENCES dbo.Employees(Id)
    );
END;
IF OBJECT_ID(N'dbo.PerformanceScores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformanceScores(
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EvaluationId int NOT NULL,
        QuestionId int NOT NULL,
        Score decimal(12,2) NOT NULL,
        MaxScore decimal(12,2) NOT NULL,
        QuestionTitleSnapshot nvarchar(250) NOT NULL,
        DomainTitleSnapshot nvarchar(200) NOT NULL,
        Comment nvarchar(2000) NULL,
        CONSTRAINT FK_PerformanceScores_Evaluations FOREIGN KEY(EvaluationId) REFERENCES dbo.PerformanceEvaluations(Id) ON DELETE CASCADE,
        CONSTRAINT FK_PerformanceScores_Questions FOREIGN KEY(QuestionId) REFERENCES dbo.PerformanceQuestions(Id)
    );
END;
IF OBJECT_ID(N'dbo.PerformanceScoreChanges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PerformanceScoreChanges(
        Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EvaluationId int NOT NULL,
        QuestionId int NOT NULL,
        ChangedByEmployeeId int NOT NULL,
        OldScore decimal(12,2) NOT NULL,
        NewScore decimal(12,2) NOT NULL,
        Reason nvarchar(1000) NOT NULL,
        ChangedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PerformanceScoreChanges_Evaluations FOREIGN KEY(EvaluationId) REFERENCES dbo.PerformanceEvaluations(Id),
        CONSTRAINT FK_PerformanceScoreChanges_Actors FOREIGN KEY(ChangedByEmployeeId) REFERENCES dbo.Employees(Id)
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PerformanceDomains') AND name=N'IX_PerformanceDomains_Code')
    CREATE UNIQUE INDEX IX_PerformanceDomains_Code ON dbo.PerformanceDomains(Code);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PerformancePositions') AND name=N'IX_PerformancePositions_Code')
    CREATE UNIQUE INDEX IX_PerformancePositions_Code ON dbo.PerformancePositions(Code);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PerformanceQuestions') AND name=N'IX_PerformanceQuestions_Code')
    CREATE UNIQUE INDEX IX_PerformanceQuestions_Code ON dbo.PerformanceQuestions(Code);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PerformancePositionQuestions') AND name=N'IX_PerformancePositionQuestions_PositionId_QuestionId')
    CREATE UNIQUE INDEX IX_PerformancePositionQuestions_PositionId_QuestionId ON dbo.PerformancePositionQuestions(PositionId,QuestionId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PerformanceAssignments') AND name=N'IX_PerformanceAssignments_PeriodId_EmployeeId')
    CREATE UNIQUE INDEX IX_PerformanceAssignments_PeriodId_EmployeeId ON dbo.PerformanceAssignments(PeriodId,EmployeeId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PerformanceEvaluations') AND name=N'IX_PerformanceEvaluations_PeriodId_EmployeeId')
    CREATE UNIQUE INDEX IX_PerformanceEvaluations_PeriodId_EmployeeId ON dbo.PerformanceEvaluations(PeriodId,EmployeeId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PerformanceScores') AND name=N'IX_PerformanceScores_EvaluationId_QuestionId')
    CREATE UNIQUE INDEX IX_PerformanceScores_EvaluationId_QuestionId ON dbo.PerformanceScores(EvaluationId,QuestionId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PerformanceScoreChanges') AND name=N'IX_PerformanceScoreChanges_EvaluationId')
    CREATE INDEX IX_PerformanceScoreChanges_EvaluationId ON dbo.PerformanceScoreChanges(EvaluationId);
""";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
