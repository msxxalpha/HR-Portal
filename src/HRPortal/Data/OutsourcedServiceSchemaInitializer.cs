using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class OutsourcedServiceSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.OutsourcedServiceRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutsourcedServiceRequests
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OutsourcedServiceRequests PRIMARY KEY,
        RequestNumber nvarchar(30) NOT NULL,
        ServiceDescription nvarchar(3000) NOT NULL,
        UnitOfMeasure nvarchar(40) NOT NULL,
        Quantity decimal(18,3) NOT NULL,
        NeedByDate date NOT NULL,
        Priority nvarchar(20) NOT NULL,
        Status nvarchar(30) NOT NULL CONSTRAINT DF_OutsourcedServiceRequests_Status DEFAULT N'Submitted',
        RequesterEmployeeId int NOT NULL,
        CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_OutsourcedServiceRequests_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2 NOT NULL CONSTRAINT DF_OutsourcedServiceRequests_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_OutsourcedServiceRequests_Employees FOREIGN KEY(RequesterEmployeeId) REFERENCES dbo.Employees(Id)
    );
    CREATE UNIQUE INDEX UX_OutsourcedServiceRequests_Number ON dbo.OutsourcedServiceRequests(RequestNumber);
    CREATE INDEX IX_OutsourcedServiceRequests_Created ON dbo.OutsourcedServiceRequests(CreatedAtUtc);
    CREATE INDEX IX_OutsourcedServiceRequests_StatusPriority ON dbo.OutsourcedServiceRequests(Status, Priority);
    CREATE INDEX IX_OutsourcedServiceRequests_Requester ON dbo.OutsourcedServiceRequests(RequesterEmployeeId, CreatedAtUtc);
END;

IF OBJECT_ID(N'dbo.OutsourcedServiceRequests', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.OutsourcedServiceRequests', N'IsDeleted') IS NULL
        ALTER TABLE dbo.OutsourcedServiceRequests ADD IsDeleted bit NOT NULL
            CONSTRAINT DF_OutsourcedServiceRequests_IsDeleted DEFAULT(0) WITH VALUES;
    IF COL_LENGTH(N'dbo.OutsourcedServiceRequests', N'DeletedAtUtc') IS NULL
        ALTER TABLE dbo.OutsourcedServiceRequests ADD DeletedAtUtc datetime2 NULL;
    IF COL_LENGTH(N'dbo.OutsourcedServiceRequests', N'DeletedByEmployeeId') IS NULL
        ALTER TABLE dbo.OutsourcedServiceRequests ADD DeletedByEmployeeId int NULL;
END;

IF OBJECT_ID(N'dbo.OutsourcedServiceRequestRevisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OutsourcedServiceRequestRevisions
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OutsourcedServiceRequestRevisions PRIMARY KEY,
        RequestId int NOT NULL,
        Action nvarchar(30) NOT NULL,
        BeforeJson nvarchar(max) NULL,
        AfterJson nvarchar(max) NULL,
        ActorEmployeeId int NULL,
        ActorUserName nvarchar(200) NULL,
        CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_OutsourcedServiceRequestRevisions_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_OutsourcedServiceRequestRevisions_Requests FOREIGN KEY(RequestId)
            REFERENCES dbo.OutsourcedServiceRequests(Id)
    );
    CREATE INDEX IX_OutsourcedServiceRequestRevisions_Request_Created
        ON dbo.OutsourcedServiceRequestRevisions(RequestId, CreatedAtUtc);
END;

IF OBJECT_ID(N'dbo.OutsourcedServiceRequests', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OutsourcedServiceRequests')
    AND name=N'IX_OutsourcedServiceRequests_IsDeleted_CreatedAtUtc')
    EXEC sys.sp_executesql N'CREATE INDEX IX_OutsourcedServiceRequests_IsDeleted_CreatedAtUtc
        ON dbo.OutsourcedServiceRequests(IsDeleted, CreatedAtUtc DESC);';
";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}