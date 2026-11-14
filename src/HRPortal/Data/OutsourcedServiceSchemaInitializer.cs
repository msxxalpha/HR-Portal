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
END;";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}