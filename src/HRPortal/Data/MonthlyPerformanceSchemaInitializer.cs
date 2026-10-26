using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

// Safe additive upgrade for existing HRPortal databases. Every DDL statement
// is executed as a separate SQL Server batch: SQL Server compiles an entire batch
// before ALTER TABLE executes and cannot bind newly added columns in later DDL.
public static class MonthlyPerformanceSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        // Transaction + application lock protects simultaneous application startup
        // and rolls back partial schema changes if any step cannot finish.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(@"
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = N'HRPortal.MonthlyPerformance.SchemaUpgrade',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 30000;
IF @lockResult < 0
    THROW 51080, N'Unable to acquire the HRPortal monthly performance schema upgrade lock.', 1;
");

        // Separate database calls mean separate SQL compilation batches, including
        // when the original schema has only part of the new columns installed.
        var schemaSteps = new[]
        {
            // Step 01
            @"IF COL_LENGTH(N'dbo.Employees', N'IsPerformanceEvaluator') IS NULL
    ALTER TABLE dbo.Employees ADD IsPerformanceEvaluator bit NOT NULL CONSTRAINT DF_Employees_IsPerformanceEvaluator DEFAULT(0) WITH VALUES;",
            // Step 02
            @"IF COL_LENGTH(N'dbo.Employees', N'PerformanceRankId') IS NULL
    ALTER TABLE dbo.Employees ADD PerformanceRankId int NULL;",
            // Step 03
            @"IF COL_LENGTH(N'dbo.Employees', N'MustChangeInitialPassword') IS NULL
    ALTER TABLE dbo.Employees ADD MustChangeInitialPassword bit NOT NULL CONSTRAINT DF_Employees_MustChangeInitialPassword DEFAULT(0) WITH VALUES;",
            // Step 04
            @"IF OBJECT_ID(N'dbo.MonthlyPerformancePeriods',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformancePeriods(
 Id int IDENTITY(1,1) PRIMARY KEY, Code nvarchar(100) NOT NULL, Title nvarchar(150) NOT NULL,
 [Year] int NOT NULL, [Month] int NOT NULL, StartAt datetime2 NOT NULL, EndAt datetime2 NOT NULL,
 IsEnabled bit NOT NULL DEFAULT(1), IsDeleted bit NOT NULL DEFAULT(0), DeletedAt datetime2 NULL,
 DeletedByAdminId int NULL, DeletionReason nvarchar(500) NULL,
 Description nvarchar(1000) NULL, CreatedAt datetime2 NOT NULL,
 CONSTRAINT UQ_MonthlyPerformancePeriods_Code UNIQUE(Code),
 CONSTRAINT CK_MonthlyPerformancePeriods_Dates CHECK (StartAt < EndAt),
 CONSTRAINT CK_MonthlyPerformancePeriods_Month CHECK ([Month] BETWEEN 1 AND 12));",
            // Step 05
            @"IF COL_LENGTH(N'dbo.MonthlyPerformancePeriods',N'IsDeleted') IS NULL
 ALTER TABLE dbo.MonthlyPerformancePeriods ADD IsDeleted bit NOT NULL CONSTRAINT DF_MPPeriods_IsDeleted DEFAULT(0) WITH VALUES;",
            // Step 06
            @"IF COL_LENGTH(N'dbo.MonthlyPerformancePeriods',N'DeletedAt') IS NULL
 ALTER TABLE dbo.MonthlyPerformancePeriods ADD DeletedAt datetime2 NULL;",
            // Step 07
            @"IF COL_LENGTH(N'dbo.MonthlyPerformancePeriods',N'DeletedByAdminId') IS NULL
 ALTER TABLE dbo.MonthlyPerformancePeriods ADD DeletedByAdminId int NULL;",
            // Step 08
            @"IF COL_LENGTH(N'dbo.MonthlyPerformancePeriods',N'DeletionReason') IS NULL
 ALTER TABLE dbo.MonthlyPerformancePeriods ADD DeletionReason nvarchar(500) NULL;",
            // Step 09
            @"IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id=OBJECT_ID(N'dbo.MonthlyPerformancePeriods')
                 AND name=N'UX_MPPeriods_ActiveYearMonth')
 CREATE UNIQUE INDEX UX_MPPeriods_ActiveYearMonth
 ON dbo.MonthlyPerformancePeriods([Year],[Month]) WHERE IsDeleted=0;",
            // Step 10
            @"-- Permit reusing a Jalali month after an administrator archives its old period.
-- The archived row (with all evaluations and audit history) still exists.
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE parent_object_id=OBJECT_ID(N'dbo.MonthlyPerformancePeriods')
             AND name=N'UQ_MonthlyPerformancePeriods_YearMonth')
 ALTER TABLE dbo.MonthlyPerformancePeriods DROP CONSTRAINT UQ_MonthlyPerformancePeriods_YearMonth;",
            // Step 11
            @"IF OBJECT_ID(N'dbo.MonthlyPerformanceDomains',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceDomains(
 Id int IDENTITY(1,1) PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE,
 Title nvarchar(200) NOT NULL, IsActive bit NOT NULL DEFAULT(1), SortOrder int NOT NULL DEFAULT(0));",
            // Step 12
            @"IF OBJECT_ID(N'dbo.MonthlyPerformanceRanks',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceRanks(
 Id int IDENTITY(1,1) PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE,
 Title nvarchar(100) NOT NULL UNIQUE, IsActive bit NOT NULL DEFAULT(1));",
            // Step 13
            @"IF OBJECT_ID(N'dbo.MonthlyPerformanceQuestions',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceQuestions(
 Id int IDENTITY(1,1) PRIMARY KEY, Code nvarchar(50) NOT NULL UNIQUE,
 Title nvarchar(500) NOT NULL, [Text] nvarchar(1000) NULL, Description nvarchar(1000) NULL, DomainId int NOT NULL,
 IsActive bit NOT NULL DEFAULT(1), SortOrder int NOT NULL DEFAULT(0),
 CONSTRAINT FK_MPQuestions_Domain FOREIGN KEY(DomainId) REFERENCES dbo.MonthlyPerformanceDomains(Id));",
            // Step 14
            @"IF COL_LENGTH(N'dbo.MonthlyPerformanceQuestions',N'Text') IS NULL
 ALTER TABLE dbo.MonthlyPerformanceQuestions ADD [Text] nvarchar(1000) NULL;",
            // Step 15
            @"IF COL_LENGTH(N'dbo.MonthlyPerformanceQuestions',N'Description') IS NULL
 ALTER TABLE dbo.MonthlyPerformanceQuestions ADD Description nvarchar(1000) NULL;",
            // Step 16
            @"IF OBJECT_ID(N'dbo.MonthlyPerformanceRankQuestions',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceRankQuestions(
 Id int IDENTITY(1,1) PRIMARY KEY, RankId int NOT NULL, QuestionId int NOT NULL,
 MaxScore decimal(12,2) NOT NULL, SortOrder int NOT NULL DEFAULT(1), IsActive bit NOT NULL DEFAULT(1),
 CONSTRAINT UQ_MPRankQuestion UNIQUE(RankId,QuestionId),
 CONSTRAINT CK_MPRankQuestion_Max CHECK(MaxScore > 0),
 CONSTRAINT FK_MPRankQuestion_Rank FOREIGN KEY(RankId) REFERENCES dbo.MonthlyPerformanceRanks(Id),
 CONSTRAINT FK_MPRankQuestion_Question FOREIGN KEY(QuestionId) REFERENCES dbo.MonthlyPerformanceQuestions(Id));",
            // Step 17
            @"IF COL_LENGTH(N'dbo.MonthlyPerformanceRankQuestions',N'SortOrder') IS NULL
 ALTER TABLE dbo.MonthlyPerformanceRankQuestions ADD SortOrder int NOT NULL CONSTRAINT DF_MPRankQuestions_SortOrder DEFAULT(1) WITH VALUES;",
            // Step 18
            @"IF OBJECT_ID(N'dbo.MonthlyPerformanceEvaluations',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceEvaluations(
 Id int IDENTITY(1,1) PRIMARY KEY, PeriodId int NOT NULL, EmployeeId int NOT NULL,
 OriginalEvaluatorId int NOT NULL, LastEvaluatorId int NOT NULL,
 RankTitleSnapshot nvarchar(100) NOT NULL, MaxScore decimal(12,2) NOT NULL,
 TotalScore decimal(12,2) NOT NULL, RowVersion rowversion NOT NULL, CreatedAt datetime2 NOT NULL, UpdatedAt datetime2 NOT NULL,
 CONSTRAINT UQ_MPEvaluations_PeriodEmployee UNIQUE(PeriodId,EmployeeId),
 CONSTRAINT CK_MPEvaluations_Range CHECK(TotalScore >= 0 AND TotalScore <= MaxScore),
 CONSTRAINT FK_MPEvaluations_Period FOREIGN KEY(PeriodId) REFERENCES dbo.MonthlyPerformancePeriods(Id),
 CONSTRAINT FK_MPEvaluations_Employee FOREIGN KEY(EmployeeId) REFERENCES dbo.Employees(Id));",
            // Step 19
            @"IF COL_LENGTH(N'dbo.MonthlyPerformanceEvaluations', N'RowVersion') IS NULL
 ALTER TABLE dbo.MonthlyPerformanceEvaluations ADD RowVersion rowversion;",
            // Step 20
            @"IF OBJECT_ID(N'dbo.MonthlyPerformanceScores',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceScores(
 Id int IDENTITY(1,1) PRIMARY KEY, EvaluationId int NOT NULL, QuestionId int NOT NULL,
 QuestionTitleSnapshot nvarchar(500) NOT NULL, DomainTitleSnapshot nvarchar(200) NOT NULL,
 MaxScore decimal(12,2) NOT NULL, Score decimal(12,2) NOT NULL, Note nvarchar(1000) NULL,
 CONSTRAINT UQ_MPScores_EvalQuestion UNIQUE(EvaluationId,QuestionId),
 CONSTRAINT CK_MPScores_Range CHECK(Score >= 0 AND MaxScore > 0 AND Score <= MaxScore),
 CONSTRAINT FK_MPScores_Eval FOREIGN KEY(EvaluationId) REFERENCES dbo.MonthlyPerformanceEvaluations(Id),
 CONSTRAINT FK_MPScores_Question FOREIGN KEY(QuestionId) REFERENCES dbo.MonthlyPerformanceQuestions(Id));",
            // Step 21
            @"IF OBJECT_ID(N'dbo.MonthlyPerformanceRevisions',N'U') IS NULL
 CREATE TABLE dbo.MonthlyPerformanceRevisions(
 Id bigint IDENTITY(1,1) PRIMARY KEY, EvaluationId int NOT NULL, QuestionId int NOT NULL,
 ChangedByEmployeeId int NOT NULL, PreviousEvaluatorId int NOT NULL,
 OldScore decimal(12,2) NULL, NewScore decimal(12,2) NOT NULL,
 OldNote nvarchar(1000) NULL, NewNote nvarchar(1000) NULL,
 Reason nvarchar(500) NOT NULL, ChangedAt datetime2 NOT NULL,
 CONSTRAINT FK_MPRevisions_Eval FOREIGN KEY(EvaluationId) REFERENCES dbo.MonthlyPerformanceEvaluations(Id));",
            // Step 22
            @"IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name = N'IX_MPRevisions_EvaluationDate' AND object_id = OBJECT_ID(N'dbo.MonthlyPerformanceRevisions'))
 CREATE INDEX IX_MPRevisions_EvaluationDate ON dbo.MonthlyPerformanceRevisions(EvaluationId,ChangedAt);"
        };

        foreach (var statement in schemaSteps)
            await db.Database.ExecuteSqlRawAsync(statement);

        // Verify that all EF model properties map to actual columns *before*
        // using the mapped entities to query and seed ranks. Catalog validation
        // is itself a fresh batch after every preceding ALTER TABLE.
        await db.Database.ExecuteSqlRawAsync(@"
DECLARE @missing nvarchar(max);
SELECT @missing = STRING_AGG(CONCAT(Required.TableName, N'.', Required.ColumnName), N', ')
FROM (VALUES
    (N'Employees', N'IsPerformanceEvaluator'),
    (N'Employees', N'MustChangeInitialPassword'),
    (N'Employees', N'PerformanceRankId'),
    (N'MonthlyPerformancePeriods', N'IsDeleted'),
    (N'MonthlyPerformancePeriods', N'DeletedAt'),
    (N'MonthlyPerformancePeriods', N'DeletedByAdminId'),
    (N'MonthlyPerformancePeriods', N'DeletionReason'),
    (N'MonthlyPerformanceQuestions', N'Text'),
    (N'MonthlyPerformanceQuestions', N'Description'),
    (N'MonthlyPerformanceRankQuestions', N'SortOrder'),
    (N'MonthlyPerformanceEvaluations', N'RowVersion')
) AS Required(TableName, ColumnName)
WHERE COL_LENGTH(N'dbo.' + Required.TableName, Required.ColumnName) IS NULL;
IF @missing IS NOT NULL
BEGIN
    DECLARE @message nvarchar(2048) = CONCAT(N'HRPortal monthly performance schema has missing columns: ', @missing);
    THROW 51081, @message, 1;
END;
");



        // Only add missing rank titles. Existing codes and mappings are never rewritten.
        var titles = await db.OrganizationRankDefinitions.AsNoTracking().Select(x => x.Title).ToListAsync();
        var grades = await db.OrganizationPositionDefinitions.AsNoTracking()
            .Where(x => x.Grade != null && x.Grade != "")
            .Select(x => x.Grade!).Distinct().ToListAsync();
        titles.AddRange(grades.Where(x => x.Length <= 100));
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

        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys
              WHERE parent_object_id=OBJECT_ID(N'dbo.Employees')
                AND referenced_object_id=OBJECT_ID(N'dbo.MonthlyPerformanceRanks'))
    ALTER TABLE dbo.Employees WITH CHECK
    ADD CONSTRAINT FK_Employees_MonthlyPerformanceRank
        FOREIGN KEY(PerformanceRankId) REFERENCES dbo.MonthlyPerformanceRanks(Id);
");
        await transaction.CommitAsync();
    }
}
