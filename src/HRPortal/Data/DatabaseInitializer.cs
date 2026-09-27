using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await EnsureSchemaAsync(db);

        await EnsureAdminTableAsync(db);

        if (!await db.SystemSettings.AnyAsync())
            db.SystemSettings.Add(new SystemSettings { OrganizationName = "شرکت کمک فنرسازی ایندامین سایپا", ApplicationName = "پورتال جامع منابع انسانی" });

        if (!await db.OtpSettings.AnyAsync())
            db.OtpSettings.Add(new OtpSettings { Length = 5, ValiditySeconds = 120, MaxAttempts = 5, Enabled = true });

        if (!await db.SmsSettings.AnyAsync())
            db.SmsSettings.Add(new SmsSettings());

        if (!await db.PayrollReportSettings.AnyAsync())
            db.PayrollReportSettings.Add(new PayrollReportSettings());

        if (!await db.OrganizationStructureRevisions.AnyAsync())
            db.OrganizationStructureRevisions.Add(new OrganizationStructureRevision
            {
                RevisionCode = "ORG-001",
                Title = "نسخه اولیه ساختار سازمانی",
                EffectiveDate = DateTime.Today,
                IsFinalized = true,
                FinalizedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
        var adminService = new global::HRPortal.Services.AdminService(db);
        await adminService.SeedAsync();
    }

    private static async Task EnsureAdminTableAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'[AdminUsers]', N'U') IS NULL
BEGIN
    CREATE TABLE [AdminUsers](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AdminUsers] PRIMARY KEY,
        [Username] nvarchar(100) NOT NULL,
        [PasswordHash] nvarchar(1000) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL CONSTRAINT [DF_AdminUsers_DisplayName] DEFAULT N'مدیر سامانه',
        [IsActive] bit NOT NULL CONSTRAINT [DF_AdminUsers_IsActive] DEFAULT 1,
        [MustChangePassword] bit NOT NULL CONSTRAINT [DF_AdminUsers_MustChangePassword] DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [LastLoginAt] datetime2 NULL
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_AdminUsers_Username')
    CREATE UNIQUE INDEX [IX_AdminUsers_Username] ON [AdminUsers]([Username]);
";
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task EnsureSchemaAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'[Employees]', N'U') IS NULL
BEGIN
    CREATE TABLE [Employees](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Employees] PRIMARY KEY,
        [PersonnelNumber] nvarchar(50) NOT NULL,
        [NationalId] nvarchar(20) NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(150) NOT NULL,
        [Mobile] nvarchar(30) NOT NULL,
        [Gender] nvarchar(20) NOT NULL,
        [OrganizationUnitId] int NULL,
        [OrganizationDepartmentId] int NULL,
        [OrganizationSectionId] int NULL,
        [PositionTitle] nvarchar(300) NULL,
        [EmploymentType] nvarchar(100) NULL,
        [Email] nvarchar(200) NULL,
        [FatherName] nvarchar(100) NULL,
        [Status] nvarchar(50) NOT NULL CONSTRAINT [DF_Employees_Status] DEFAULT N'فعال',
        [IsSystemUser] bit NOT NULL CONSTRAINT [DF_Employees_IsSystemUser] DEFAULT 1,
        [IsSystemAdministrator] bit NOT NULL CONSTRAINT [DF_Employees_IsSystemAdministrator] DEFAULT 0,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL
    );
END;

IF OBJECT_ID(N'[OrganizationStructureRevisions]', N'U') IS NULL
BEGIN
    CREATE TABLE [OrganizationStructureRevisions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OrganizationStructureRevisions] PRIMARY KEY,
        [RevisionCode] nvarchar(100) NOT NULL,
        [EffectiveDate] date NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [IsFinalized] bit NOT NULL,
        [FinalizedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL
    );
END;

IF OBJECT_ID(N'[OrganizationNodes]', N'U') IS NULL
BEGIN
    CREATE TABLE [OrganizationNodes](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OrganizationNodes] PRIMARY KEY,
        [OrganizationStructureRevisionId] int NOT NULL,
        [ParentId] int NULL,
        [Code] nvarchar(100) NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [RankType] nvarchar(50) NOT NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_OrganizationNodes_SortOrder] DEFAULT 0,
        [IsActive] bit NOT NULL CONSTRAINT [DF_OrganizationNodes_IsActive] DEFAULT 1,
        [Notes] nvarchar(max) NULL
    );
END;

IF OBJECT_ID(N'[OrganizationChanges]', N'U') IS NULL
BEGIN
    CREATE TABLE [OrganizationChanges](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OrganizationChanges] PRIMARY KEY,
        [OrganizationStructureRevisionId] int NOT NULL,
        [ChangeType] nvarchar(100) NOT NULL,
        [EntityCode] nvarchar(100) NOT NULL,
        [Description] nvarchar(1000) NOT NULL,
        [ChangedAt] datetime2 NOT NULL
    );
END;

IF OBJECT_ID(N'[OtpChallenges]', N'U') IS NULL
BEGIN
    CREATE TABLE [OtpChallenges](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OtpChallenges] PRIMARY KEY,
        [EmployeeId] int NULL,
        [PersonnelNumber] nvarchar(50) NOT NULL,
        [Mobile] nvarchar(30) NOT NULL,
        [CodeHash] nvarchar(1000) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [AttemptCount] int NOT NULL,
        [IsConsumed] bit NOT NULL,
        [SmsSent] bit NOT NULL,
        [SmsResponse] nvarchar(2000) NULL
    );
END;

IF OBJECT_ID(N'[AuditLogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [AuditLogs](
        [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AuditLogs] PRIMARY KEY,
        [EmployeeId] int NULL,
        [Action] nvarchar(200) NOT NULL,
        [EntityName] nvarchar(200) NOT NULL,
        [EntityId] nvarchar(100) NULL,
        [Description] nvarchar(max) NULL,
        [IpAddress] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL
    );
END;

IF COL_LENGTH(N'SystemSettings', N'ShortName') IS NULL ALTER TABLE [SystemSettings] ADD [ShortName] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_ShortName] DEFAULT N'HR';
IF COL_LENGTH(N'SystemSettings', N'Slogan') IS NULL ALTER TABLE [SystemSettings] ADD [Slogan] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_Slogan] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'FooterText') IS NULL ALTER TABLE [SystemSettings] ADD [FooterText] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_FooterText] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'Website') IS NULL ALTER TABLE [SystemSettings] ADD [Website] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_Website] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'Phone') IS NULL ALTER TABLE [SystemSettings] ADD [Phone] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_Phone] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'Email') IS NULL ALTER TABLE [SystemSettings] ADD [Email] nvarchar(200) NOT NULL CONSTRAINT [DF_SystemSettings_Email] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'EconomicCode') IS NULL ALTER TABLE [SystemSettings] ADD [EconomicCode] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_EconomicCode] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'NationalId') IS NULL ALTER TABLE [SystemSettings] ADD [NationalId] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_NationalId] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'FaviconUrl') IS NULL ALTER TABLE [SystemSettings] ADD [FaviconUrl] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_FaviconUrl] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportUrl') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportUrl] nvarchar(2000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportUrl] DEFAULT N'';

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_Employees_PersonnelNumber') CREATE UNIQUE INDEX [IX_Employees_PersonnelNumber] ON [Employees]([PersonnelNumber]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_Employees_NationalId') CREATE UNIQUE INDEX [IX_Employees_NationalId] ON [Employees]([NationalId]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_OrganizationStructureRevisions_RevisionCode') CREATE UNIQUE INDEX [IX_OrganizationStructureRevisions_RevisionCode] ON [OrganizationStructureRevisions]([RevisionCode]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_OrganizationNodes_Revision_Code') CREATE UNIQUE INDEX [IX_OrganizationNodes_Revision_Code] ON [OrganizationNodes]([OrganizationStructureRevisionId],[Code]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_AuditLogs_CreatedAt') CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs]([CreatedAt]);
";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}