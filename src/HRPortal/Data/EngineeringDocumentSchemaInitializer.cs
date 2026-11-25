using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class EngineeringDocumentSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'dbo.EngineeringDocumentNodes', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EngineeringDocumentNodes](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EngineeringDocumentNodes] PRIMARY KEY,
        [ParentId] int NULL,
        [Title] nvarchar(200) NOT NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_EngineeringDocumentNodes_SortOrder] DEFAULT (0),
        [IsActive] bit NOT NULL CONSTRAINT [DF_EngineeringDocumentNodes_IsActive] DEFAULT (1),
        [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_EngineeringDocumentNodes_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
        [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_EngineeringDocumentNodes_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [FK_EngineeringDocumentNodes_Parent] FOREIGN KEY([ParentId])
            REFERENCES [dbo].[EngineeringDocumentNodes]([Id]) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID(N'dbo.EngineeringDocumentTypes', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EngineeringDocumentTypes](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EngineeringDocumentTypes] PRIMARY KEY,
        [Title] nvarchar(150) NOT NULL,
        [Description] nvarchar(500) NOT NULL CONSTRAINT [DF_EngineeringDocumentTypes_Description] DEFAULT N'',
        [SortOrder] int NOT NULL CONSTRAINT [DF_EngineeringDocumentTypes_SortOrder] DEFAULT (0),
        [IsActive] bit NOT NULL CONSTRAINT [DF_EngineeringDocumentTypes_IsActive] DEFAULT (1),
        [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_EngineeringDocumentTypes_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
        [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_EngineeringDocumentTypes_UpdatedAtUtc] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.EngineeringDocuments', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EngineeringDocuments](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EngineeringDocuments] PRIMARY KEY,
        [NodeId] int NOT NULL,
        [TypeId] int NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [DocumentNumber] nvarchar(100) NOT NULL CONSTRAINT [DF_EngineeringDocuments_DocumentNumber] DEFAULT N'',
        [RevisionLabel] nvarchar(50) NOT NULL CONSTRAINT [DF_EngineeringDocuments_RevisionLabel] DEFAULT N'',
        [PublishDate] date NULL,
        [EffectiveDate] date NULL,
        [Keywords] nvarchar(500) NOT NULL CONSTRAINT [DF_EngineeringDocuments_Keywords] DEFAULT N'',
        [Description] nvarchar(2000) NOT NULL CONSTRAINT [DF_EngineeringDocuments_Description] DEFAULT N'',
        [IsActive] bit NOT NULL CONSTRAINT [DF_EngineeringDocuments_IsActive] DEFAULT (1),
        [IsPublished] bit NOT NULL CONSTRAINT [DF_EngineeringDocuments_IsPublished] DEFAULT (0),
        [HasBeenPublished] bit NOT NULL CONSTRAINT [DF_EngineeringDocuments_HasBeenPublished] DEFAULT (0),
        [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_EngineeringDocuments_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
        [UpdatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_EngineeringDocuments_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
        [CreatedByName] nvarchar(200) NOT NULL CONSTRAINT [DF_EngineeringDocuments_CreatedByName] DEFAULT N'',
        [CreatedByPersonnelNumber] nvarchar(50) NOT NULL CONSTRAINT [DF_EngineeringDocuments_CreatedByPersonnelNumber] DEFAULT N'',
        CONSTRAINT [FK_EngineeringDocuments_Node] FOREIGN KEY([NodeId])
            REFERENCES [dbo].[EngineeringDocumentNodes]([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_EngineeringDocuments_Type] FOREIGN KEY([TypeId])
            REFERENCES [dbo].[EngineeringDocumentTypes]([Id]) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID(N'dbo.EngineeringDocumentVersions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EngineeringDocumentVersions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EngineeringDocumentVersions] PRIMARY KEY,
        [EngineeringDocumentId] int NOT NULL,
        [VersionNumber] int NOT NULL,
        [PdfContent] varbinary(max) NOT NULL,
        [Sha256] nvarchar(64) NOT NULL CONSTRAINT [DF_EngineeringDocumentVersions_Sha256] DEFAULT N'',
        [OriginalFileName] nvarchar(260) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL CONSTRAINT [DF_EngineeringDocumentVersions_ContentType] DEFAULT N'application/pdf',
        [FileSize] bigint NOT NULL,
        [IsCurrent] bit NOT NULL CONSTRAINT [DF_EngineeringDocumentVersions_IsCurrent] DEFAULT (0),
        [UploadedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_EngineeringDocumentVersions_UploadedAtUtc] DEFAULT SYSUTCDATETIME(),
        [UploadedByName] nvarchar(200) NOT NULL CONSTRAINT [DF_EngineeringDocumentVersions_UploadedByName] DEFAULT N'',
        [UploadedByPersonnelNumber] nvarchar(50) NOT NULL CONSTRAINT [DF_EngineeringDocumentVersions_UploadedByPersonnelNumber] DEFAULT N'',
        [ChangeNote] nvarchar(500) NOT NULL CONSTRAINT [DF_EngineeringDocumentVersions_ChangeNote] DEFAULT N'',
        CONSTRAINT [FK_EngineeringDocumentVersions_Document] FOREIGN KEY([EngineeringDocumentId])
            REFERENCES [dbo].[EngineeringDocuments]([Id]) ON DELETE NO ACTION,
        CONSTRAINT [CK_EngineeringDocumentVersions_VersionNumber] CHECK ([VersionNumber] > 0)
    );
END;

IF COL_LENGTH(N'dbo.EngineeringDocuments', N'IsPublished') IS NULL
    ALTER TABLE [dbo].[EngineeringDocuments] ADD [IsPublished] bit NOT NULL
        CONSTRAINT [DF_EngineeringDocuments_IsPublished] DEFAULT (0);
IF COL_LENGTH(N'dbo.EngineeringDocuments', N'HasBeenPublished') IS NULL
    ALTER TABLE [dbo].[EngineeringDocuments] ADD [HasBeenPublished] bit NOT NULL
        CONSTRAINT [DF_EngineeringDocuments_HasBeenPublished] DEFAULT (0);
IF COL_LENGTH(N'dbo.EngineeringDocuments', N'PublishDate') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns
       WHERE object_id=OBJECT_ID(N'dbo.EngineeringDocuments') AND name=N'PublishDate' AND is_nullable=0)
    EXEC sys.sp_executesql N'ALTER TABLE [dbo].[EngineeringDocuments] ALTER COLUMN [PublishDate] date NULL;';

-- Existing documents that had a publication date were visible before the
-- publication workflow was introduced. Preserve their public status/history.
EXEC sys.sp_executesql N'UPDATE [dbo].[EngineeringDocuments]
    SET [IsPublished]=1, [HasBeenPublished]=1
    WHERE [PublishDate] IS NOT NULL AND ([IsPublished]=0 OR [HasBeenPublished]=0);';

IF COL_LENGTH(N'dbo.EngineeringDocumentVersions', N'PdfContent') IS NULL
    ALTER TABLE [dbo].[EngineeringDocumentVersions] ADD [PdfContent] varbinary(max) NULL;
IF COL_LENGTH(N'dbo.EngineeringDocumentVersions', N'Sha256') IS NULL
    ALTER TABLE [dbo].[EngineeringDocumentVersions] ADD [Sha256] nvarchar(64) NOT NULL
        CONSTRAINT [DF_EngineeringDocumentVersions_Sha256] DEFAULT N'';
EXEC sys.sp_executesql N'
IF EXISTS (SELECT 1 FROM [dbo].[EngineeringDocumentVersions] WHERE [PdfContent] IS NULL)
    THROW 51000, ''Engineering document PDF BLOB migration found legacy versions without database content. No file data was moved automatically.'', 1;';

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.EngineeringDocumentVersions') AND name=N'PdfContent' AND is_nullable=1)
    EXEC sys.sp_executesql N'ALTER TABLE [dbo].[EngineeringDocumentVersions] ALTER COLUMN [PdfContent] varbinary(max) NOT NULL;';
IF COL_LENGTH(N'dbo.EngineeringDocumentVersions', N'StoredFileName') IS NOT NULL
    EXEC sys.sp_executesql N'ALTER TABLE [dbo].[EngineeringDocumentVersions] DROP COLUMN [StoredFileName];'

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_EngineeringDocumentNodes_Parent_Sort' AND object_id=OBJECT_ID(N'dbo.EngineeringDocumentNodes'))
    CREATE INDEX [IX_EngineeringDocumentNodes_Parent_Sort] ON [dbo].[EngineeringDocumentNodes]([ParentId],[SortOrder],[Title]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_EngineeringDocumentTypes_Title' AND object_id=OBJECT_ID(N'dbo.EngineeringDocumentTypes'))
    CREATE UNIQUE INDEX [UX_EngineeringDocumentTypes_Title] ON [dbo].[EngineeringDocumentTypes]([Title]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_EngineeringDocuments_Node_Active' AND object_id=OBJECT_ID(N'dbo.EngineeringDocuments'))
    CREATE INDEX [IX_EngineeringDocuments_Node_Active] ON [dbo].[EngineeringDocuments]([NodeId],[IsActive],[PublishDate]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_EngineeringDocuments_Type_PublishDate' AND object_id=OBJECT_ID(N'dbo.EngineeringDocuments'))
    CREATE INDEX [IX_EngineeringDocuments_Type_PublishDate] ON [dbo].[EngineeringDocuments]([TypeId],[PublishDate]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_EngineeringDocuments_PublishState' AND object_id=OBJECT_ID(N'dbo.EngineeringDocuments'))
    EXEC sys.sp_executesql N'CREATE INDEX [IX_EngineeringDocuments_PublishState] ON [dbo].[EngineeringDocuments]([IsPublished],[IsActive],[UpdatedAtUtc]);';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_EngineeringDocumentVersions_Document_Version' AND object_id=OBJECT_ID(N'dbo.EngineeringDocumentVersions'))
    CREATE UNIQUE INDEX [UX_EngineeringDocumentVersions_Document_Version] ON [dbo].[EngineeringDocumentVersions]([EngineeringDocumentId],[VersionNumber]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_EngineeringDocumentVersions_Current' AND object_id=OBJECT_ID(N'dbo.EngineeringDocumentVersions'))
    CREATE UNIQUE INDEX [UX_EngineeringDocumentVersions_Current] ON [dbo].[EngineeringDocumentVersions]([EngineeringDocumentId]) WHERE [IsCurrent] = 1;
""");

        if (!await db.EngineeringDocumentTypes.AnyAsync())
        {
            var titles = new[]
            {
                "نقشه مهندسی",
                "دستورالعمل کاری",
                "مشخصات فنی",
                "استاندارد و الزامات",
                "مدرک کنترل کیفیت",
                "سایر مستندات"
            };
            for (var i = 0; i < titles.Length; i++)
                db.EngineeringDocumentTypes.Add(new EngineeringDocumentType
                {
                    Title = titles[i],
                    SortOrder = (i + 1) * 10,
                    IsActive = true
                });
            await db.SaveChangesAsync();
        }
    }
}