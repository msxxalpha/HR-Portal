using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

// Idempotent, additive-only upgrade: existing employee, payroll, workflow and contract tables are untouched.
public static class MonthlyPerformanceSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.Employees', N'IsPerformanceEvaluator') IS NULL
    ALTER TABLE dbo.Employees ADD IsPerformanceEvaluator bit NOT NULL CONSTRAINT DF_Employees_IsPerformanceEvaluator DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.Employees', N'MustChangeInitialPassword') IS NULL
    ALTER TABLE dbo.Employees ADD MustChangeInitialPassword bit NOT NULL CONSTRAINT DF_Employees_MustChangeInitialPassword DEFAULT(0) WITH VALUES;

IF OBJECT_ID(N'dbo.MonthlyPerformancePeriods',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformancePeriods(
 Id int IDENTITY(1,1) PRIMARY KEY, Code nvarchar(100) NOT NULL, Title nvarchar(150) NOT NULL,
 [Year] int NOT NULL, [Month] int NOT NULL, StartAt datetime2 NOT NULL, EndAt datetime2 NOT NULL,
 IsEnabled bit NOT NULL DEFAULT(1), Description nvarchar(1000) NULL, CreatedAt datetime2 NOT NULL,
 CONSTRAINT UQ_MonthlyPerformancePeriods_YearMonth UNIQUE ([Year],[Month]),
 CONSTRAINT UQ_MonthlyPerformancePeriods_Code UNIQUE(Code),
 CONSTRAINT CK_MonthlyPerformancePeriods_Dates CHECK (StartAt < EndAt),
 CONSTRAINT CK_MonthlyPerformancePeriods_Month CHECK ([Month] BETWEEN 1 AND 12));
IF OBJECT_ID(N'dbo.MonthlyPerformanceDomains',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceDomains(
 Id int IDENTITY(1,1) PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE,
 Title nvarchar(200) NOT NULL, IsActive bit NOT NULL DEFAULT(1), SortOrder int NOT NULL DEFAULT(0));
IF OBJECT_ID(N'dbo.MonthlyPerformanceRanks',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceRanks(
 Id int IDENTITY(1,1) PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE,
 Title nvarchar(100) NOT NULL UNIQUE, IsActive bit NOT NULL DEFAULT(1));
IF OBJECT_ID(N'dbo.MonthlyPerformanceQuestions',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceQuestions(
 Id int IDENTITY(1,1) PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE,
 Title nvarchar(500) NOT NULL, DomainId int NOT NULL,
 IsActive bit NOT NULL DEFAULT(1), SortOrder int NOT NULL DEFAULT(0),
 CONSTRAINT FK_MPQuestions_Domain FOREIGN KEY(DomainId) REFERENCES dbo.MonthlyPerformanceDomains(Id));
IF OBJECT_ID(N'dbo.MonthlyPerformanceRankQuestions',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceRankQuestions(
 Id int IDENTITY(1,1) PRIMARY KEY, RankId int NOT NULL, QuestionId int NOT NULL,
 MaxScore decimal(12,2) NOT NULL, IsActive bit NOT NULL DEFAULT(1),
 CONSTRAINT UQ_MPRankQuestion UNIQUE(RankId,QuestionId),
 CONSTRAINT CK_MPRankQuestion_Max CHECK(MaxScore > 0),
 CONSTRAINT FK_MPRankQuestion_Rank FOREIGN KEY(RankId) REFERENCES dbo.MonthlyPerformanceRanks(Id),
 CONSTRAINT FK_MPRankQuestion_Question FOREIGN KEY(QuestionId) REFERENCES dbo.MonthlyPerformanceQuestions(Id));
IF OBJECT_ID(N'dbo.MonthlyPerformanceEvaluations',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceEvaluations(
 Id int IDENTITY(1,1) PRIMARY KEY, PeriodId int NOT NULL, EmployeeId int NOT NULL,
 OriginalEvaluatorId int NOT NULL, LastEvaluatorId int NOT NULL,
 RankTitleSnapshot nvarchar(100) NOT NULL, MaxScore decimal(12,2) NOT NULL,
 TotalScore decimal(12,2) NOT NULL, RowVersion rowversion NOT NULL, CreatedAt datetime2 NOT NULL, UpdatedAt datetime2 NOT NULL,
 CONSTRAINT UQ_MPEvaluations_PeriodEmployee UNIQUE(PeriodId,EmployeeId),
 CONSTRAINT CK_MPEvaluations_Range CHECK(TotalScore >= 0 AND TotalScore <= MaxScore),
 CONSTRAINT FK_MPEvaluations_Period FOREIGN KEY(PeriodId) REFERENCES dbo.MonthlyPerformancePeriods(Id),
 CONSTRAINT FK_MPEvaluations_Employee FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(Id));
IF COL_LENGTH(N'dbo.MonthlyPerformanceEvaluations', N'RowVersion') IS NULL
 ALTER TABLE dbo.MonthlyPerformanceEvaluations ADD RowVersion rowversion;
IF OBJECT_ID(N'dbo.MonthlyPerformanceScores',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceScores(
 Id int IDENTITY(1,1) PRIMARY KEY, EvaluationId int NOT NULL, QuestionId int NOT NULL,
 QuestionTitleSnapshot nvarchar(500) NOT NULL, DomainTitleSnapshot nvarchar(200) NOT NULL,
 MaxScore decimal(12,2) NOT NULL, Score decimal(12,2) NOT NULL, Note nvarchar(1000) NULL,
 CONSTRAINT UQ_MPScores_EvalQuestion UNIQUE(EvaluationId,QuestionId),
 CONSTRAINT CK_MPScores_Range CHECK(Score >= 0 AND MaxScore > 0 AND Score <= MaxScore),
 CONSTRAINT FK_MPScores_Eval FOREIGN KEY(EvaluationId) REFERENCES dbo.MonthlyPerformanceEvaluations(Id),
 CONSTRAINT FK_MPScores_Question FOREIGN KEY(QuestionId) REFERENCES dbo.MonthlyPerformanceQuestions(Id));
IF OBJECT_ID(N'dbo.MonthlyPerformanceRevisions',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceRevisions(
 Id bigint IDENTITY(1,1) PRIMARY KEY, EvaluationId int NOT NULL, QuestionId int NOT NULL,
 ChangedByEmployeeId int NOT NULL, PreviousEvaluatorId int NOT NULL,
 OldScore decimal(12,2) NULL, NewScore decimal(12,2) NOT NULL,
 OldNote nvarchar(1000) NULL, NewNote nvarchar(1000) NULL,
 Reason nvarchar(500) NOT NULL, ChangedAt datetime2 NOT NULL,
 CONSTRAINT FK_MPRevisions_Eval FOREIGN KEY(EvaluationId) REFERENCES dbo.MonthlyPerformanceEvaluations(Id));
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_MPRevisions_EvaluationDate' AND object_id = OBJECT_ID(N'dbo.MonthlyPerformanceRevisions'))
 CREATE INDEX IX_MPRevisions_EvaluationDate ON dbo.MonthlyPerformanceRevisions(EvaluationId,ChangedAt);
");

        // Only add missing rank titles. Existing codes and mappings are never rewritten.
        var titles = await db.OrganizationRankDefinitions.AsNoTracking().Select(x => x.Title).ToListAsync();
        var existing = (await db.MonthlyPerformanceRanks.AsNoTracking().Select(x => x.Title).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var codes = (await db.MonthlyPerformanceRanks.AsNoTracking().Select(x => x.Code).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        foreach (var title in titles.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (existing.Contains(title)) continue;
            string code;
            do { code = $"R-{index++:000}"; } while (!codes.Add(code));
            db.MonthlyPerformanceRanks.Add(new Models.MonthlyPerformanceRank { Code = code, Title = title });
            existing.Add(title);
        }
        await db.SaveChangesAsync();
    }
}
