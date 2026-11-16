using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class EmployeePositionAssignmentHistorySchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.EmployeePositionAssignmentSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeePositionAssignmentSnapshots(
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeePositionAssignmentSnapshots PRIMARY KEY,
        RevisionId int NOT NULL,
        RevisionCodeSnapshot nvarchar(100) NOT NULL,
        RevisionTitleSnapshot nvarchar(250) NOT NULL,
        EffectiveDate date NOT NULL,
        FinalizedAtSnapshot datetime2 NULL,
        EmployeeId int NULL,
        PersonnelNumberSnapshot nvarchar(50) NOT NULL,
        EmployeeNameSnapshot nvarchar(250) NOT NULL,
        PositionCode nvarchar(50) NOT NULL,
        PositionTitle nvarchar(150) NOT NULL,
        RankType nvarchar(100) NOT NULL,
        CapturedAtUtc datetime2 NOT NULL CONSTRAINT DF_EmployeePositionAssignmentSnapshots_CapturedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_EmployeePositionAssignmentSnapshots_Revision_Employee_Position' AND object_id=OBJECT_ID(N'dbo.EmployeePositionAssignmentSnapshots'))
    CREATE UNIQUE INDEX UX_EmployeePositionAssignmentSnapshots_Revision_Employee_Position
        ON dbo.EmployeePositionAssignmentSnapshots(RevisionId, EmployeeId, PositionCode);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_EmployeePositionAssignmentSnapshots_Employee_EffectiveDate' AND object_id=OBJECT_ID(N'dbo.EmployeePositionAssignmentSnapshots'))
    CREATE INDEX IX_EmployeePositionAssignmentSnapshots_Employee_EffectiveDate
        ON dbo.EmployeePositionAssignmentSnapshots(EmployeeId, EffectiveDate);
";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
