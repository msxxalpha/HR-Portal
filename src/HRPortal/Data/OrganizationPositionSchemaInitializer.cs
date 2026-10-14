using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class OrganizationPositionSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF COL_LENGTH(N'dbo.OrganizationNodes', N'AssignedEmployeeIdsCsv') IS NULL
    ALTER TABLE dbo.OrganizationNodes ADD AssignedEmployeeIdsCsv nvarchar(max) NULL;
IF OBJECT_ID(N'dbo.OrganizationPositionDefinitions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OrganizationPositionDefinitions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OrganizationPositionDefinitions] PRIMARY KEY,
        [Code] nvarchar(50) NOT NULL,
        [Title] nvarchar(150) NOT NULL,
        [JobFamily] nvarchar(100) NULL,
        [Grade] nvarchar(50) NULL,
        [Description] nvarchar(1000) NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_OrganizationPositionDefinitions_SortOrder] DEFAULT 10,
        [IsActive] bit NOT NULL CONSTRAINT [DF_OrganizationPositionDefinitions_IsActive] DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_OrganizationPositionDefinitions_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_OrganizationPositionDefinitions_UpdatedAt] DEFAULT SYSUTCDATETIME()
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_OrganizationPositionDefinitions_Code' AND object_id=OBJECT_ID(N'dbo.OrganizationPositionDefinitions'))
    CREATE UNIQUE INDEX [UX_OrganizationPositionDefinitions_Code] ON [dbo].[OrganizationPositionDefinitions]([Code]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_OrganizationPositionDefinitions_Title' AND object_id=OBJECT_ID(N'dbo.OrganizationPositionDefinitions'))
    CREATE UNIQUE INDEX [UX_OrganizationPositionDefinitions_Title] ON [dbo].[OrganizationPositionDefinitions]([Title]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_OrganizationPositionDefinitions_Active_SortOrder_Title' AND object_id=OBJECT_ID(N'dbo.OrganizationPositionDefinitions'))
    CREATE INDEX [IX_OrganizationPositionDefinitions_Active_SortOrder_Title] ON [dbo].[OrganizationPositionDefinitions]([IsActive],[SortOrder],[Title]);
";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
