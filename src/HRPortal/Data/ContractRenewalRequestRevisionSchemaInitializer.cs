using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class ContractRenewalRequestRevisionSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.ContractRenewalRequestRevisions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ContractRenewalRequestRevisions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ContractRenewalRequestRevisions] PRIMARY KEY,
        [ContractRenewalRequestId] int NOT NULL,
        [RevisionNumber] int NOT NULL,
        [Action] nvarchar(30) NOT NULL,
        [SnapshotJson] nvarchar(max) NOT NULL,
        [ChangedByEmployeeId] int NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_ContractRenewalRequestRevisions_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.ContractRenewalRequestRevisions', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_ContractRenewalRequestRevisions_Request_Revision'
    AND object_id=OBJECT_ID(N'dbo.ContractRenewalRequestRevisions'))
    CREATE UNIQUE INDEX [UX_ContractRenewalRequestRevisions_Request_Revision]
        ON [dbo].[ContractRenewalRequestRevisions]([ContractRenewalRequestId],[RevisionNumber]);

IF OBJECT_ID(N'dbo.ContractRenewalRequestRevisions', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_ContractRenewalRequestRevisions_Request_CreatedAt'
    AND object_id=OBJECT_ID(N'dbo.ContractRenewalRequestRevisions'))
    CREATE INDEX [IX_ContractRenewalRequestRevisions_Request_CreatedAt]
        ON [dbo].[ContractRenewalRequestRevisions]([ContractRenewalRequestId],[CreatedAt]);

IF OBJECT_ID(N'dbo.ContractRenewalRequestRevisions', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ContractRenewalRequestRevisions_Request')
    ALTER TABLE [dbo].[ContractRenewalRequestRevisions]
    ADD CONSTRAINT [FK_ContractRenewalRequestRevisions_Request]
    FOREIGN KEY([ContractRenewalRequestId]) REFERENCES [dbo].[ContractRenewalRequests]([Id]) ON DELETE NO ACTION;";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
