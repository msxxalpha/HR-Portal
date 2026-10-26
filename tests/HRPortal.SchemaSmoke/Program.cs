using HRPortal.Data;
using HRPortal.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// SQL Server integration smoke test for a real legacy/partially upgraded HRPortal
// database. Uses a local ephemeral CI SQL Server; no production connection strings.
var password = Environment.GetEnvironmentVariable("HRPORTAL_SCHEMA_TEST_PASSWORD")
    ?? throw new InvalidOperationException("Set HRPORTAL_SCHEMA_TEST_PASSWORD for the isolated SQL Server test container.");
var masterString = new SqlConnectionStringBuilder
{
    DataSource = "127.0.0.1,1433",
    InitialCatalog = "master",
    UserID = "sa",
    Password = password,
    Encrypt = true,
    TrustServerCertificate = true,
    ConnectTimeout = 3
}.ConnectionString;

await using var master = new SqlConnection(masterString);
Exception? lastConnectionError = null;
for (var attempt = 0; attempt < 45; attempt++)
{
    try { await master.OpenAsync(); break; }
    catch (SqlException ex) { lastConnectionError = ex; await Task.Delay(2000); }
}
if (master.State != System.Data.ConnectionState.Open)
    throw new InvalidOperationException("The SQL Server integration test container did not become ready.", lastConnectionError);

foreach (var fixture in new[] { "HRPortal_SchemaLegacy_Test", "HRPortal_SchemaPartial_Test" })
{
    try
    {
        await Execute(master, $"IF DB_ID(N'{fixture}') IS NOT NULL BEGIN ALTER DATABASE [{fixture}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{fixture}]; END");
        await Execute(master, $"CREATE DATABASE [{fixture}]");
        var builder = new SqlConnectionStringBuilder(masterString) { InitialCatalog = fixture };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await CreateLegacyFixture(connection);

        if (fixture.Contains("Partial"))
            await CreatePartiallyUpgradedFixture(connection);

        var options = new DbContextOptionsBuilder<HRPortalDbContext>()
            .UseSqlServer(builder.ConnectionString).Options;
        await using (var db = new HRPortalDbContext(options))
        {
            // Run twice to check both a legacy upgrade and repeatable startup.
            await MonthlyPerformanceSchemaInitializer.InitializeAsync(db);
            await MonthlyPerformanceSchemaInitializer.InitializeAsync(db);
            var periods = await db.MonthlyPerformancePeriods.AsNoTracking().ToListAsync();
            Check(periods.Count == 1 && periods[0].Title == "شهریور ۱۴۰۵",
                "Legacy evaluation period was not preserved.");
            Check(!periods[0].IsDeleted, "New IsDeleted flag must default to false.");
            Check(await db.MonthlyPerformanceRanks.AsNoTracking().AnyAsync(x => x.Title == "کارشناس"),
                "Existing organization ranks were not seeded.");
            if (fixture.Contains("Partial"))
            {
                var eval = await db.MonthlyPerformanceEvaluations.AsNoTracking().SingleAsync();
                Check(eval.RowVersion.Length == 8, "Migrated evaluation must receive an 8-byte rowversion.");
                Check(await db.MonthlyPerformanceRankQuestions.AsNoTracking()
                    .AnyAsync(x => x.SortOrder == 1 && x.MaxScore == 100),
                    "Legacy rank/question mapping was not preserved or backfilled.");
                var question = await db.MonthlyPerformanceQuestions.AsNoTracking().SingleAsync();
                Check(question.Text == null && question.Description == null,
                    "Added question fields must preserve prior question text/data.");
            }
        }

        await Execute(connection, @"
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.MonthlyPerformancePeriods')
      AND name=N'UX_MPPeriods_ActiveYearMonth' AND has_filter=1)
    THROW 51098, N'Active Jalali period filtered index is missing.', 1;
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.MonthlyPerformancePeriods')
    AND name=N'UQ_MonthlyPerformancePeriods_YearMonth')
    THROW 51099, N'Legacy unfiltered month constraint was not migrated.', 1;
UPDATE dbo.MonthlyPerformancePeriods SET IsDeleted=1, IsEnabled=0 WHERE Code=N'PER-LEGACY';
INSERT INTO dbo.MonthlyPerformancePeriods
    (Code,Title,[Year],[Month],StartAt,EndAt,IsEnabled,IsDeleted,CreatedAt)
VALUES
    (N'PER-NEW',N'دوره جایگزین',1405,6,'2026-09-12','2026-09-17',1,0,SYSUTCDATETIME());
");
        await using (var db = new HRPortalDbContext(options))
        {
            await MonthlyPerformanceSchemaInitializer.InitializeAsync(db);
            var periods = await db.MonthlyPerformancePeriods.AsNoTracking().ToListAsync();
            Check(periods.Count == 2 && periods.Count(x => x.IsDeleted) == 1,
                "Filtered index did not permit replacing an archived period.");
        }
        // Actual EF Core query integration (not just pure resolver tests):
        // one evaluator must see both child-node staff, a third employee whose
        // department FK belongs to the prior finalized revision, and no sibling
        // manager or sibling manager's subordinates.
        await CreateHierarchyFixture(connection);
        await using (var db = new HRPortalDbContext(options))
        {
            var resolver = new MonthlyPerformanceService(db);
            var actual = (await resolver.ScopeAsync(100))
                .Select(x => x.EmployeeId).OrderBy(id => id).ToArray();
            Check(actual.SequenceEqual(new[] { 101, 102, 103 }),
                $"Real SQL Server evaluator roster mismatch: [{string.Join(",", actual)}]");
            Check(await resolver.CanEvaluateAsync(100, 103),
                "Previous-revision department member cannot be evaluated.");
            Check(!await resolver.CanEvaluateAsync(100, 200) &&
                  !await resolver.CanEvaluateAsync(100, 201),
                "Cross-branch evaluator access must be forbidden.");
        }
        Console.WriteLine($"PASS: {fixture}, including repeated upgrade, data preservation, rowversion, filtered index.");
    }
    finally
    {
        await Execute(master, $"IF DB_ID(N'{fixture}') IS NOT NULL BEGIN ALTER DATABASE [{fixture}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{fixture}]; END");
    }
}
Console.WriteLine("Monthly performance schema integration tests passed.");

static async Task CreateLegacyFixture(SqlConnection connection)
{
    await Execute(connection, @"
CREATE TABLE dbo.Employees(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Status nvarchar(30) NOT NULL DEFAULT N'فعال',
    OrganizationPositionId int NULL,
    OrganizationSectionId int NULL,
    OrganizationDepartmentId int NULL,
    OrganizationUnitId int NULL,
    PositionTitle nvarchar(300) NULL);
CREATE TABLE dbo.OrganizationRankDefinitions(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Title nvarchar(100) NOT NULL);
CREATE TABLE dbo.OrganizationPositionDefinitions(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Title nvarchar(150) NOT NULL, Grade nvarchar(50) NULL);
CREATE TABLE dbo.MonthlyPerformancePeriods(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Code nvarchar(100) NOT NULL,
    Title nvarchar(150) NOT NULL,
    [Year] int NOT NULL, [Month] int NOT NULL,
    StartAt datetime2 NOT NULL, EndAt datetime2 NOT NULL,
    IsEnabled bit NOT NULL, Description nvarchar(1000) NULL,
    CreatedAt datetime2 NOT NULL,
    CONSTRAINT UQ_MonthlyPerformancePeriods_YearMonth UNIQUE([Year],[Month]),
    CONSTRAINT UQ_MonthlyPerformancePeriods_Code UNIQUE(Code));
" );
    await Execute(connection, @"INSERT INTO dbo.Employees DEFAULT VALUES;
INSERT INTO dbo.OrganizationRankDefinitions(Title) VALUES(N'کارشناس');
INSERT INTO dbo.OrganizationPositionDefinitions(Title,Grade) VALUES(N'کارشناس منابع انسانی',N'کارشناس');
INSERT INTO dbo.MonthlyPerformancePeriods(
    Code,Title,[Year],[Month],StartAt,EndAt,IsEnabled,CreatedAt)
VALUES(N'PER-LEGACY',N'شهریور ۱۴۰۵',1405,6,'2026-09-12','2026-09-17',1,SYSUTCDATETIME());");
}

static async Task CreatePartiallyUpgradedFixture(SqlConnection connection)
{
    await Execute(connection, @"
ALTER TABLE dbo.MonthlyPerformancePeriods ADD IsDeleted bit NOT NULL DEFAULT(0) WITH VALUES;
ALTER TABLE dbo.MonthlyPerformancePeriods ADD DeletedAt datetime2 NULL;
CREATE TABLE dbo.MonthlyPerformanceDomains(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Code nvarchar(50) NOT NULL, Title nvarchar(200) NOT NULL,
    IsActive bit NOT NULL, SortOrder int NOT NULL);
CREATE TABLE dbo.MonthlyPerformanceRanks(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Code nvarchar(50) NOT NULL, Title nvarchar(100) NOT NULL,
    IsActive bit NOT NULL);
CREATE TABLE dbo.MonthlyPerformanceQuestions(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Code nvarchar(50) NOT NULL, Title nvarchar(500) NOT NULL,
    DomainId int NOT NULL, IsActive bit NOT NULL, SortOrder int NOT NULL);
CREATE TABLE dbo.MonthlyPerformanceRankQuestions(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RankId int NOT NULL, QuestionId int NOT NULL,
    MaxScore decimal(12,2) NOT NULL, IsActive bit NOT NULL);
CREATE TABLE dbo.MonthlyPerformanceEvaluations(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PeriodId int NOT NULL, EmployeeId int NOT NULL,
    OriginalEvaluatorId int NOT NULL, LastEvaluatorId int NOT NULL,
    RankTitleSnapshot nvarchar(100) NOT NULL,
    MaxScore decimal(12,2) NOT NULL, TotalScore decimal(12,2) NOT NULL,
    CreatedAt datetime2 NOT NULL, UpdatedAt datetime2 NOT NULL);
" );
    await Execute(connection, @"INSERT INTO dbo.MonthlyPerformanceDomains(Code,Title,IsActive,SortOrder)
    VALUES(N'D-1',N'کیفیت',1,1);
INSERT INTO dbo.MonthlyPerformanceRanks(Code,Title,IsActive)
    VALUES(N'R-010',N'کارشناس',1);
INSERT INTO dbo.MonthlyPerformanceQuestions(Code,Title,DomainId,IsActive,SortOrder)
    VALUES(N'Q-1',N'دقت و کیفیت',1,1,1);
INSERT INTO dbo.MonthlyPerformanceRankQuestions(RankId,QuestionId,MaxScore,IsActive)
    VALUES(1,1,100,1);
INSERT INTO dbo.MonthlyPerformanceEvaluations(
    PeriodId,EmployeeId,OriginalEvaluatorId,LastEvaluatorId,RankTitleSnapshot,
    MaxScore,TotalScore,CreatedAt,UpdatedAt)
VALUES(1,1,1,1,N'کارشناس',100,100,SYSUTCDATETIME(),SYSUTCDATETIME());");
}

static async Task CreateHierarchyFixture(SqlConnection connection)
{
    await Execute(connection, @"
CREATE TABLE dbo.OrganizationStructureRevisions(
    Id int NOT NULL PRIMARY KEY,
    IsFinalized bit NOT NULL,
    EffectiveDate date NOT NULL,
    FinalizedAt datetime2 NULL,
    CreatedAt datetime2 NOT NULL);
CREATE TABLE dbo.OrganizationNodes(
    Id int NOT NULL PRIMARY KEY,
    OrganizationStructureRevisionId int NOT NULL,
    ParentId int NULL, Code nvarchar(100) NOT NULL,
    RankType nvarchar(50) NOT NULL, IsActive bit NOT NULL,
    PositionHolderEmployeeId int NULL,
    AssignedEmployeeIdsCsv nvarchar(max) NULL);
");
    await Execute(connection, @"
INSERT INTO dbo.OrganizationStructureRevisions(Id,IsFinalized,EffectiveDate,FinalizedAt,CreatedAt)
VALUES
 (1,1,'2024-01-01','2024-01-01','2024-01-01'),
 (2,1,'2025-01-01','2025-01-01','2025-01-01');
INSERT INTO dbo.OrganizationNodes(
    Id,OrganizationStructureRevisionId,ParentId,Code,RankType,IsActive,
    PositionHolderEmployeeId,AssignedEmployeeIdsCsv)
VALUES
 (910,1,NULL,N'M10',N'مدیریت',1,NULL,NULL),
 (1,2,NULL,N'CEO',N'مدیرعامل',1,1,N'1'),
 (10,2,1,N'M10',N'مدیریت',1,100,N'100'),
 (11,2,10,N'S11',N'کارشناس',1,NULL,N'101,102'),
 (20,2,1,N'M20',N'مدیریت',1,200,N'200'),
 (21,2,20,N'S21',N'کارشناس',1,NULL,N'201');
SET IDENTITY_INSERT dbo.Employees ON;
INSERT INTO dbo.Employees(Id,Status,OrganizationDepartmentId,PositionTitle,IsPerformanceEvaluator)
VALUES
 (100,N'فعال',910,N'مدیر',1),
 (101,N'فعال',910,N'کارشناس',0),
 (102,N'فعال',NULL,N'کارشناس',0),
 (103,N'فعال',910,N'کارشناس',0),
 (200,N'فعال',NULL,N'مدیر',1),
 (201,N'فعال',NULL,N'کارشناس',0);
SET IDENTITY_INSERT dbo.Employees OFF;
");
}

static async Task Execute(SqlConnection connection, string sql)
{
    await using var command = new SqlCommand(sql, connection) { CommandTimeout = 90 };
    await command.ExecuteNonQueryAsync();
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
