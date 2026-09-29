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
        await EnsureAdminColumnsAsync(db);
        await EnsureRoleTablesAsync(db);

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

        var roleService = new global::HRPortal.Services.RoleService(db);
        await roleService.SeedAsync();
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

    private static async Task EnsureAdminColumnsAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF COL_LENGTH(N'dbo.AdminUsers', N'Username') IS NULL
    ALTER TABLE [dbo].[AdminUsers] ADD [Username] nvarchar(100) NOT NULL CONSTRAINT [DF_AdminUsers_Username] DEFAULT N'';
IF COL_LENGTH(N'dbo.AdminUsers', N'PasswordHash') IS NULL
    ALTER TABLE [dbo].[AdminUsers] ADD [PasswordHash] nvarchar(1000) NOT NULL CONSTRAINT [DF_AdminUsers_PasswordHash] DEFAULT N'';
IF COL_LENGTH(N'dbo.AdminUsers', N'DisplayName') IS NULL
    ALTER TABLE [dbo].[AdminUsers] ADD [DisplayName] nvarchar(200) NOT NULL CONSTRAINT [DF_AdminUsers_DisplayName] DEFAULT N'مدیر سامانه';
IF COL_LENGTH(N'dbo.AdminUsers', N'IsActive') IS NULL
    ALTER TABLE [dbo].[AdminUsers] ADD [IsActive] bit NOT NULL CONSTRAINT [DF_AdminUsers_IsActive] DEFAULT 1;
IF COL_LENGTH(N'dbo.AdminUsers', N'MustChangePassword') IS NULL
    ALTER TABLE [dbo].[AdminUsers] ADD [MustChangePassword] bit NOT NULL CONSTRAINT [DF_AdminUsers_MustChangePassword] DEFAULT 1;
IF COL_LENGTH(N'dbo.AdminUsers', N'CreatedAt') IS NULL
    ALTER TABLE [dbo].[AdminUsers] ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_AdminUsers_CreatedAt] DEFAULT SYSUTCDATETIME();
IF COL_LENGTH(N'dbo.AdminUsers', N'UpdatedAt') IS NULL
    ALTER TABLE [dbo].[AdminUsers] ADD [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_AdminUsers_UpdatedAt] DEFAULT SYSUTCDATETIME();
IF COL_LENGTH(N'dbo.AdminUsers', N'LastLoginAt') IS NULL
    ALTER TABLE [dbo].[AdminUsers] ADD [LastLoginAt] datetime2 NULL;";
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task EnsureRoleTablesAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Roles](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Roles] PRIMARY KEY,
        [Code] nvarchar(100) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [IsActive] bit NOT NULL CONSTRAINT [DF_Roles_IsActive] DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Roles_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Permissions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Permissions] PRIMARY KEY,
        [Code] nvarchar(100) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Module] nvarchar(100) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RolePermissions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_RolePermissions] PRIMARY KEY,
        [RoleId] int NOT NULL,
        [PermissionId] int NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.EmployeeRoles', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmployeeRoles](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EmployeeRoles] PRIMARY KEY,
        [EmployeeId] int NOT NULL,
        [RoleId] int NOT NULL
    );
END;";
        await db.Database.ExecuteSqlRawAsync(sql);

        const string indexes = @"
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_Roles_Code' AND object_id=OBJECT_ID(N'dbo.Roles'))
    CREATE UNIQUE INDEX [IX_Roles_Code] ON [dbo].[Roles]([Code]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_Permissions_Code' AND object_id=OBJECT_ID(N'dbo.Permissions'))
    CREATE UNIQUE INDEX [IX_Permissions_Code] ON [dbo].[Permissions]([Code]);

IF OBJECT_ID(N'dbo.RolePermissions', N'U') IS NOT NULL
BEGIN
    ;WITH duplicates AS
    (
        SELECT [Id], ROW_NUMBER() OVER(PARTITION BY [RoleId],[PermissionId] ORDER BY [Id]) AS rn
        FROM [dbo].[RolePermissions]
    )
    DELETE FROM duplicates WHERE rn > 1;
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_RolePermissions_RoleId_PermissionId' AND object_id=OBJECT_ID(N'dbo.RolePermissions'))
    CREATE UNIQUE INDEX [IX_RolePermissions_RoleId_PermissionId] ON [dbo].[RolePermissions]([RoleId],[PermissionId]);

IF OBJECT_ID(N'dbo.EmployeeRoles', N'U') IS NOT NULL
BEGIN
    ;WITH duplicates AS
    (
        SELECT [Id], ROW_NUMBER() OVER(PARTITION BY [EmployeeId],[RoleId] ORDER BY [Id]) AS rn
        FROM [dbo].[EmployeeRoles]
    )
    DELETE FROM duplicates WHERE rn > 1;
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_EmployeeRoles_EmployeeId_RoleId' AND object_id=OBJECT_ID(N'dbo.EmployeeRoles'))
    CREATE UNIQUE INDEX [IX_EmployeeRoles_EmployeeId_RoleId] ON [dbo].[EmployeeRoles]([EmployeeId],[RoleId]);";
        await db.Database.ExecuteSqlRawAsync(indexes);

        const string relationships = @"
IF OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Permissions', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.RolePermissions', N'U') IS NOT NULL
BEGIN
    DELETE rp
    FROM [dbo].[RolePermissions] rp
    LEFT JOIN [dbo].[Roles] r ON r.[Id] = rp.[RoleId]
    LEFT JOIN [dbo].[Permissions] p ON p.[Id] = rp.[PermissionId]
    WHERE r.[Id] IS NULL OR p.[Id] IS NULL;

    IF NOT EXISTS(
        SELECT 1 FROM sys.foreign_keys
        WHERE name=N'FK_RolePermissions_Roles_RoleId'
          AND parent_object_id=OBJECT_ID(N'dbo.RolePermissions'))
        ALTER TABLE [dbo].[RolePermissions]
        ADD CONSTRAINT [FK_RolePermissions_Roles_RoleId]
        FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]) ON DELETE CASCADE;

    IF NOT EXISTS(
        SELECT 1 FROM sys.foreign_keys
        WHERE name=N'FK_RolePermissions_Permissions_PermissionId'
          AND parent_object_id=OBJECT_ID(N'dbo.RolePermissions'))
        ALTER TABLE [dbo].[RolePermissions]
        ADD CONSTRAINT [FK_RolePermissions_Permissions_PermissionId]
        FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id]) ON DELETE CASCADE;
END;

IF OBJECT_ID(N'dbo.EmployeeRoles', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL
BEGIN
    DELETE er
    FROM [dbo].[EmployeeRoles] er
    LEFT JOIN [dbo].[Employees] e ON e.[Id] = er.[EmployeeId]
    LEFT JOIN [dbo].[Roles] r ON r.[Id] = er.[RoleId]
    WHERE e.[Id] IS NULL OR r.[Id] IS NULL;

    IF NOT EXISTS(
        SELECT 1 FROM sys.foreign_keys
        WHERE name=N'FK_EmployeeRoles_Employees_EmployeeId'
          AND parent_object_id=OBJECT_ID(N'dbo.EmployeeRoles'))
        ALTER TABLE [dbo].[EmployeeRoles]
        ADD CONSTRAINT [FK_EmployeeRoles_Employees_EmployeeId]
        FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees]([Id]) ON DELETE CASCADE;

    IF NOT EXISTS(
        SELECT 1 FROM sys.foreign_keys
        WHERE name=N'FK_EmployeeRoles_Roles_RoleId'
          AND parent_object_id=OBJECT_ID(N'dbo.EmployeeRoles'))
        ALTER TABLE [dbo].[EmployeeRoles]
        ADD CONSTRAINT [FK_EmployeeRoles_Roles_RoleId]
        FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]) ON DELETE CASCADE;
END;";
        await db.Database.ExecuteSqlRawAsync(relationships);
    }

    private static async Task EnsureSchemaAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'[SystemSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [SystemSettings](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SystemSettings] PRIMARY KEY,
        [ApplicationName] nvarchar(200) NOT NULL CONSTRAINT [DF_SystemSettings_ApplicationName] DEFAULT N'پورتال جامع منابع انسانی',
        [OrganizationName] nvarchar(300) NOT NULL CONSTRAINT [DF_SystemSettings_OrganizationName] DEFAULT N'',
        [ShortName] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_ShortName] DEFAULT N'HR',
        [Slogan] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_Slogan] DEFAULT N'',
        [FooterText] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_FooterText] DEFAULT N'',
        [Website] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_Website] DEFAULT N'',
        [Phone] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_Phone] DEFAULT N'',
        [Email] nvarchar(200) NOT NULL CONSTRAINT [DF_SystemSettings_Email] DEFAULT N'',
        [EconomicCode] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_EconomicCode] DEFAULT N'',
        [NationalId] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_NationalId] DEFAULT N'',
        [DefaultLanguage] nvarchar(50) NOT NULL CONSTRAINT [DF_SystemSettings_DefaultLanguage] DEFAULT N'fa-IR',
        [Calendar] nvarchar(50) NOT NULL CONSTRAINT [DF_SystemSettings_Calendar] DEFAULT N'Persian',
        [TimeZone] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_TimeZone] DEFAULT N'Asia/Tehran',
        [Theme] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_Theme] DEFAULT N'Indamin',
        [PrimaryColor] nvarchar(20) NOT NULL CONSTRAINT [DF_SystemSettings_PrimaryColor] DEFAULT N'#17324D',
        [SecondaryColor] nvarchar(20) NOT NULL CONSTRAINT [DF_SystemSettings_SecondaryColor] DEFAULT N'#6C757D',
        [LogoUrl] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_LogoUrl] DEFAULT N'/images/logo.png',
        [FaviconUrl] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_FaviconUrl] DEFAULT N'',
        [ItemsPerPage] int NOT NULL CONSTRAINT [DF_SystemSettings_ItemsPerPage] DEFAULT 20,
        [EnableAuditLog] bit NOT NULL CONSTRAINT [DF_SystemSettings_EnableAuditLog] DEFAULT 1,
        [MaintenanceMode] bit NOT NULL CONSTRAINT [DF_SystemSettings_MaintenanceMode] DEFAULT 0,
        [MaintenanceMessage] nvarchar(1000) NOT NULL CONSTRAINT [DF_SystemSettings_MaintenanceMessage] DEFAULT N'سامانه در حال بروزرسانی است.',
        [AllowUserSelfService] bit NOT NULL CONSTRAINT [DF_SystemSettings_AllowUserSelfService] DEFAULT 1,
        [UpdatedAt] datetime2 NOT NULL
    );
END;

IF OBJECT_ID(N'[OtpSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [OtpSettings](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OtpSettings] PRIMARY KEY,
        [Length] int NOT NULL CONSTRAINT [DF_OtpSettings_Length] DEFAULT 5,
        [ValiditySeconds] int NOT NULL CONSTRAINT [DF_OtpSettings_ValiditySeconds] DEFAULT 120,
        [MaxAttempts] int NOT NULL CONSTRAINT [DF_OtpSettings_MaxAttempts] DEFAULT 5,
        [Enabled] bit NOT NULL CONSTRAINT [DF_OtpSettings_Enabled] DEFAULT 1
    );
END;

IF OBJECT_ID(N'[SmsSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [SmsSettings](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SmsSettings] PRIMARY KEY,
        [Enabled] bit NOT NULL CONSTRAINT [DF_SmsSettings_Enabled] DEFAULT 0,
        [Endpoint] nvarchar(2000) NOT NULL CONSTRAINT [DF_SmsSettings_Endpoint] DEFAULT N'',
        [Method] nvarchar(20) NOT NULL CONSTRAINT [DF_SmsSettings_Method] DEFAULT N'POST',
        [Format] nvarchar(30) NOT NULL CONSTRAINT [DF_SmsSettings_Format] DEFAULT N'json',
        [AuthMode] nvarchar(30) NOT NULL CONSTRAINT [DF_SmsSettings_AuthMode] DEFAULT N'header',
        [ApiKeyName] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_ApiKeyName] DEFAULT N'Api-Key',
        [ApiKey] nvarchar(1000) NOT NULL CONSTRAINT [DF_SmsSettings_ApiKey] DEFAULT N'',
        [SenderField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_SenderField] DEFAULT N'sender',
        [Sender] nvarchar(200) NOT NULL CONSTRAINT [DF_SmsSettings_Sender] DEFAULT N'',
        [RecipientField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_RecipientField] DEFAULT N'recipient',
        [RecipientMode] nvarchar(30) NOT NULL CONSTRAINT [DF_SmsSettings_RecipientMode] DEFAULT N'scalar',
        [MessageField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_MessageField] DEFAULT N'message',
        [CodeField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_CodeField] DEFAULT N'code',
        [OtpParameterField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_OtpParameterField] DEFAULT N'code',
        [PatternCode] nvarchar(500) NOT NULL CONSTRAINT [DF_SmsSettings_PatternCode] DEFAULT N'',
        [NumberFormatField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_NumberFormatField] DEFAULT N'',
        [NumberFormat] nvarchar(200) NOT NULL CONSTRAINT [DF_SmsSettings_NumberFormat] DEFAULT N'',
        [StaticParams] nvarchar(max) NOT NULL CONSTRAINT [DF_SmsSettings_StaticParams] DEFAULT N'',
        [SuccessCodes] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_SuccessCodes] DEFAULT N'200-299',
        [Template] nvarchar(2000) NOT NULL CONSTRAINT [DF_SmsSettings_Template] DEFAULT (N'کاربر محترم، کد ورود شما: ' + NCHAR(123) + N'code' + NCHAR(125)),
        [TestRecipient] nvarchar(30) NOT NULL CONSTRAINT [DF_SmsSettings_TestRecipient] DEFAULT N''
    );
END;

IF OBJECT_ID(N'[PayrollReportSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [PayrollReportSettings](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PayrollReportSettings] PRIMARY KEY,
        [Enabled] bit NOT NULL CONSTRAINT [DF_PayrollReportSettings_Enabled] DEFAULT 1,
        [ReportUrl] nvarchar(2000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportUrl] DEFAULT N'',
        [ReportServerUrl] nvarchar(1000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportServerUrl] DEFAULT N'',
        [ReportPath] nvarchar(1000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportPath] DEFAULT N'',
        [YearParameter] nvarchar(100) NOT NULL CONSTRAINT [DF_PayrollReportSettings_YearParameter] DEFAULT N'YearMonth',
        [PersonnelParameter] nvarchar(100) NOT NULL CONSTRAINT [DF_PayrollReportSettings_PersonnelParameter] DEFAULT N'PersonnelNo',
        [ReportFormat] nvarchar(50) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportFormat] DEFAULT N'PDF',
        [ReportAuthentication] nvarchar(30) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportAuthentication] DEFAULT N'forms',
        [ReportUsername] nvarchar(300) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportUsername] DEFAULT N'',
        [ReportDomain] nvarchar(200) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportDomain] DEFAULT N'',
        [ReportLoginUrl] nvarchar(2000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportLoginUrl] DEFAULT N'',
        [ReportUsernameField] nvarchar(300) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportUsernameField] DEFAULT N'',
        [ReportPasswordField] nvarchar(300) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportPasswordField] DEFAULT N'',
        [ReportPasswordProtected] nvarchar(4000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportPasswordProtected] DEFAULT N'',
        [UseIntegratedSecurity] bit NOT NULL CONSTRAINT [DF_PayrollReportSettings_UseIntegratedSecurity] DEFAULT 0
    );
END;

IF COL_LENGTH(N'SystemSettings', N'ApplicationName') IS NULL ALTER TABLE [SystemSettings] ADD [ApplicationName] nvarchar(200) NOT NULL CONSTRAINT [DF_SystemSettings_ApplicationName] DEFAULT N'پورتال جامع منابع انسانی';
IF COL_LENGTH(N'SystemSettings', N'OrganizationName') IS NULL ALTER TABLE [SystemSettings] ADD [OrganizationName] nvarchar(300) NOT NULL CONSTRAINT [DF_SystemSettings_OrganizationName] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'ShortName') IS NULL ALTER TABLE [SystemSettings] ADD [ShortName] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_ShortName] DEFAULT N'HR';
IF COL_LENGTH(N'SystemSettings', N'Slogan') IS NULL ALTER TABLE [SystemSettings] ADD [Slogan] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_Slogan] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'FooterText') IS NULL ALTER TABLE [SystemSettings] ADD [FooterText] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_FooterText] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'Website') IS NULL ALTER TABLE [SystemSettings] ADD [Website] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_Website] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'Phone') IS NULL ALTER TABLE [SystemSettings] ADD [Phone] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_Phone] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'Email') IS NULL ALTER TABLE [SystemSettings] ADD [Email] nvarchar(200) NOT NULL CONSTRAINT [DF_SystemSettings_Email] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'EconomicCode') IS NULL ALTER TABLE [SystemSettings] ADD [EconomicCode] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_EconomicCode] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'NationalId') IS NULL ALTER TABLE [SystemSettings] ADD [NationalId] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_NationalId] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'DefaultLanguage') IS NULL ALTER TABLE [SystemSettings] ADD [DefaultLanguage] nvarchar(50) NOT NULL CONSTRAINT [DF_SystemSettings_DefaultLanguage] DEFAULT N'fa-IR';
IF COL_LENGTH(N'SystemSettings', N'Calendar') IS NULL ALTER TABLE [SystemSettings] ADD [Calendar] nvarchar(50) NOT NULL CONSTRAINT [DF_SystemSettings_Calendar] DEFAULT N'Persian';
IF COL_LENGTH(N'SystemSettings', N'TimeZone') IS NULL ALTER TABLE [SystemSettings] ADD [TimeZone] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_TimeZone] DEFAULT N'Asia/Tehran';
IF COL_LENGTH(N'SystemSettings', N'Theme') IS NULL ALTER TABLE [SystemSettings] ADD [Theme] nvarchar(100) NOT NULL CONSTRAINT [DF_SystemSettings_Theme] DEFAULT N'Indamin';
IF COL_LENGTH(N'SystemSettings', N'PrimaryColor') IS NULL ALTER TABLE [SystemSettings] ADD [PrimaryColor] nvarchar(20) NOT NULL CONSTRAINT [DF_SystemSettings_PrimaryColor] DEFAULT N'#17324D';
IF COL_LENGTH(N'SystemSettings', N'SecondaryColor') IS NULL ALTER TABLE [SystemSettings] ADD [SecondaryColor] nvarchar(20) NOT NULL CONSTRAINT [DF_SystemSettings_SecondaryColor] DEFAULT N'#6C757D';
IF COL_LENGTH(N'SystemSettings', N'LogoUrl') IS NULL ALTER TABLE [SystemSettings] ADD [LogoUrl] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_LogoUrl] DEFAULT N'/images/logo.png';
IF COL_LENGTH(N'SystemSettings', N'FaviconUrl') IS NULL ALTER TABLE [SystemSettings] ADD [FaviconUrl] nvarchar(500) NOT NULL CONSTRAINT [DF_SystemSettings_FaviconUrl] DEFAULT N'';
IF COL_LENGTH(N'SystemSettings', N'ItemsPerPage') IS NULL ALTER TABLE [SystemSettings] ADD [ItemsPerPage] int NOT NULL CONSTRAINT [DF_SystemSettings_ItemsPerPage] DEFAULT 20;
IF COL_LENGTH(N'SystemSettings', N'EnableAuditLog') IS NULL ALTER TABLE [SystemSettings] ADD [EnableAuditLog] bit NOT NULL CONSTRAINT [DF_SystemSettings_EnableAuditLog] DEFAULT 1;
IF COL_LENGTH(N'SystemSettings', N'MaintenanceMode') IS NULL ALTER TABLE [SystemSettings] ADD [MaintenanceMode] bit NOT NULL CONSTRAINT [DF_SystemSettings_MaintenanceMode] DEFAULT 0;
IF COL_LENGTH(N'SystemSettings', N'MaintenanceMessage') IS NULL ALTER TABLE [SystemSettings] ADD [MaintenanceMessage] nvarchar(1000) NOT NULL CONSTRAINT [DF_SystemSettings_MaintenanceMessage] DEFAULT N'سامانه در حال بروزرسانی است.';
IF COL_LENGTH(N'SystemSettings', N'AllowUserSelfService') IS NULL ALTER TABLE [SystemSettings] ADD [AllowUserSelfService] bit NOT NULL CONSTRAINT [DF_SystemSettings_AllowUserSelfService] DEFAULT 1;
IF COL_LENGTH(N'SystemSettings', N'UpdatedAt') IS NULL ALTER TABLE [SystemSettings] ADD [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_SystemSettings_UpdatedAt] DEFAULT SYSUTCDATETIME();

IF COL_LENGTH(N'OtpSettings', N'Length') IS NULL ALTER TABLE [OtpSettings] ADD [Length] int NOT NULL CONSTRAINT [DF_OtpSettings_Length] DEFAULT 5;
IF COL_LENGTH(N'OtpSettings', N'ValiditySeconds') IS NULL ALTER TABLE [OtpSettings] ADD [ValiditySeconds] int NOT NULL CONSTRAINT [DF_OtpSettings_ValiditySeconds] DEFAULT 120;
IF COL_LENGTH(N'OtpSettings', N'MaxAttempts') IS NULL ALTER TABLE [OtpSettings] ADD [MaxAttempts] int NOT NULL CONSTRAINT [DF_OtpSettings_MaxAttempts] DEFAULT 5;
IF COL_LENGTH(N'OtpSettings', N'Enabled') IS NULL ALTER TABLE [OtpSettings] ADD [Enabled] bit NOT NULL CONSTRAINT [DF_OtpSettings_Enabled] DEFAULT 1;

IF COL_LENGTH(N'dbo.SmsSettings', N'Enabled') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [Enabled] bit NOT NULL CONSTRAINT [DF_SmsSettings_Enabled] DEFAULT 0;
IF COL_LENGTH(N'dbo.SmsSettings', N'Endpoint') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [Endpoint] nvarchar(2000) NOT NULL CONSTRAINT [DF_SmsSettings_Endpoint] DEFAULT N'';
IF COL_LENGTH(N'dbo.SmsSettings', N'Method') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [Method] nvarchar(20) NOT NULL CONSTRAINT [DF_SmsSettings_Method] DEFAULT N'POST';
IF COL_LENGTH(N'dbo.SmsSettings', N'Format') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [Format] nvarchar(30) NOT NULL CONSTRAINT [DF_SmsSettings_Format] DEFAULT N'json';
IF COL_LENGTH(N'dbo.SmsSettings', N'AuthMode') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [AuthMode] nvarchar(30) NOT NULL CONSTRAINT [DF_SmsSettings_AuthMode] DEFAULT N'header';
IF COL_LENGTH(N'dbo.SmsSettings', N'ApiKeyName') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [ApiKeyName] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_ApiKeyName] DEFAULT N'Api-Key';
IF COL_LENGTH(N'dbo.SmsSettings', N'ApiKey') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [ApiKey] nvarchar(1000) NOT NULL CONSTRAINT [DF_SmsSettings_ApiKey] DEFAULT N'';
IF COL_LENGTH(N'dbo.SmsSettings', N'SenderField') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [SenderField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_SenderField] DEFAULT N'sender';
IF COL_LENGTH(N'dbo.SmsSettings', N'Sender') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [Sender] nvarchar(200) NOT NULL CONSTRAINT [DF_SmsSettings_Sender] DEFAULT N'';
IF COL_LENGTH(N'dbo.SmsSettings', N'RecipientField') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [RecipientField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_RecipientField] DEFAULT N'recipient';
IF COL_LENGTH(N'dbo.SmsSettings', N'RecipientMode') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [RecipientMode] nvarchar(30) NOT NULL CONSTRAINT [DF_SmsSettings_RecipientMode] DEFAULT N'scalar';
IF COL_LENGTH(N'dbo.SmsSettings', N'MessageField') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [MessageField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_MessageField] DEFAULT N'message';
IF COL_LENGTH(N'dbo.SmsSettings', N'CodeField') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [CodeField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_CodeField] DEFAULT N'code';
IF COL_LENGTH(N'dbo.SmsSettings', N'OtpParameterField') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [OtpParameterField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_OtpParameterField] DEFAULT N'code';
IF COL_LENGTH(N'dbo.SmsSettings', N'PatternCode') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [PatternCode] nvarchar(500) NOT NULL CONSTRAINT [DF_SmsSettings_PatternCode] DEFAULT N'';
IF COL_LENGTH(N'dbo.SmsSettings', N'NumberFormatField') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [NumberFormatField] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_NumberFormatField] DEFAULT N'';
IF COL_LENGTH(N'dbo.SmsSettings', N'NumberFormat') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [NumberFormat] nvarchar(200) NOT NULL CONSTRAINT [DF_SmsSettings_NumberFormat] DEFAULT N'';
IF COL_LENGTH(N'dbo.SmsSettings', N'StaticParams') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [StaticParams] nvarchar(max) NOT NULL CONSTRAINT [DF_SmsSettings_StaticParams] DEFAULT N'';
IF COL_LENGTH(N'dbo.SmsSettings', N'SuccessCodes') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [SuccessCodes] nvarchar(100) NOT NULL CONSTRAINT [DF_SmsSettings_SuccessCodes] DEFAULT N'200-299';
IF COL_LENGTH(N'dbo.SmsSettings', N'Template') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [Template] nvarchar(2000) NOT NULL CONSTRAINT [DF_SmsSettings_Template] DEFAULT (N'کاربر محترم، کد ورود شما: ' + NCHAR(123) + N'code' + NCHAR(125));
IF COL_LENGTH(N'dbo.SmsSettings', N'TestRecipient') IS NULL ALTER TABLE [dbo].[SmsSettings] ADD [TestRecipient] nvarchar(30) NOT NULL CONSTRAINT [DF_SmsSettings_TestRecipient] DEFAULT N'';

IF COL_LENGTH(N'dbo.Employees', N'Identifier') IS NULL ALTER TABLE [dbo].[Employees] ADD [Identifier] nvarchar(100) NOT NULL CONSTRAINT [DF_Employees_Identifier] DEFAULT N'';

IF COL_LENGTH(N'PayrollReportSettings', N'Enabled') IS NULL ALTER TABLE [PayrollReportSettings] ADD [Enabled] bit NOT NULL CONSTRAINT [DF_PayrollReportSettings_Enabled] DEFAULT 1;
IF COL_LENGTH(N'PayrollReportSettings', N'ReportUrl') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportUrl] nvarchar(2000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportUrl] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportServerUrl') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportServerUrl] nvarchar(1000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportServerUrl] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportPath') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportPath] nvarchar(1000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportPath] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'YearParameter') IS NULL ALTER TABLE [PayrollReportSettings] ADD [YearParameter] nvarchar(100) NOT NULL CONSTRAINT [DF_PayrollReportSettings_YearParameter] DEFAULT N'YearMonth';
IF COL_LENGTH(N'PayrollReportSettings', N'PersonnelParameter') IS NULL ALTER TABLE [PayrollReportSettings] ADD [PersonnelParameter] nvarchar(100) NOT NULL CONSTRAINT [DF_PayrollReportSettings_PersonnelParameter] DEFAULT N'PersonnelNo';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportFormat') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportFormat] nvarchar(50) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportFormat] DEFAULT N'PDF';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportAuthentication') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportAuthentication] nvarchar(30) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportAuthentication] DEFAULT N'forms';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportUsername') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportUsername] nvarchar(300) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportUsername] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportDomain') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportDomain] nvarchar(200) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportDomain] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportLoginUrl') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportLoginUrl] nvarchar(2000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportLoginUrl] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportUsernameField') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportUsernameField] nvarchar(300) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportUsernameField] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportPasswordField') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportPasswordField] nvarchar(300) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportPasswordField] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'ReportPasswordProtected') IS NULL ALTER TABLE [PayrollReportSettings] ADD [ReportPasswordProtected] nvarchar(4000) NOT NULL CONSTRAINT [DF_PayrollReportSettings_ReportPasswordProtected] DEFAULT N'';
IF COL_LENGTH(N'PayrollReportSettings', N'UseIntegratedSecurity') IS NULL ALTER TABLE [PayrollReportSettings] ADD [UseIntegratedSecurity] bit NOT NULL CONSTRAINT [DF_PayrollReportSettings_UseIntegratedSecurity] DEFAULT 0;

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
        [Identifier] nvarchar(100) NOT NULL CONSTRAINT [DF_Employees_Identifier] DEFAULT N'',
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


";
        // DDL is intentionally separated from data migration. SQL Server compiles a full batch
        // before execution; a later UPDATE referencing a newly-added column can otherwise fail
        // with "Invalid column name" and prevent the ALTER TABLE from running.
        await db.Database.ExecuteSqlRawAsync(sql);
        await MigrateLegacySmsAsync(db);
        await MigrateSmsProviderDefaultsAsync(db);
        await EnsureIndexesAsync(db);
    }

    private static async Task MigrateLegacySmsAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF COL_LENGTH(N'dbo.SmsSettings', N'ServiceUrl') IS NOT NULL
   AND COL_LENGTH(N'dbo.SmsSettings', N'Endpoint') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'UPDATE [dbo].[SmsSettings]
        SET [Endpoint]=[ServiceUrl]
        WHERE ISNULL([Endpoint],N'''')=N'''' AND ISNULL([ServiceUrl],N'''')<>N'''';';
END;

IF COL_LENGTH(N'dbo.SmsSettings', N'SenderNumber') IS NOT NULL
   AND COL_LENGTH(N'dbo.SmsSettings', N'Sender') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'UPDATE [dbo].[SmsSettings]
        SET [Sender]=[SenderNumber]
        WHERE ISNULL([Sender],N'''')=N'''' AND ISNULL([SenderNumber],N'''')<>N'''';';
END;

IF COL_LENGTH(N'dbo.SmsSettings', N'OtpTemplate') IS NOT NULL
   AND COL_LENGTH(N'dbo.SmsSettings', N'Template') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'UPDATE [dbo].[SmsSettings]
        SET [Template]=[OtpTemplate]
        WHERE ISNULL([Template],N'''')=N'''' AND ISNULL([OtpTemplate],N'''')<>N'''';';
END;";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
    
    private static async Task MigrateSmsProviderDefaultsAsync(HRPortalDbContext db)
    {
        const string sql = @"
-- IPPanel Edge pattern defaults
IF EXISTS(
    SELECT 1 FROM [dbo].[SmsSettings]
    WHERE LOWER(ISNULL([Endpoint],N'')) LIKE N'%ippanel%'
      AND LOWER(REPLACE(ISNULL([StaticParams],N''),N' ',N'')) LIKE N'%""sending_type"":""pattern""%'
)
BEGIN
    UPDATE [dbo].[SmsSettings]
    SET [RecipientField]=N'recipients'
    WHERE ISNULL([RecipientField],N'') IN (N'',N'recipient');

    UPDATE [dbo].[SmsSettings]
    SET [RecipientMode]=N'array'
    WHERE ISNULL([RecipientMode],N'') IN (N'',N'scalar');

    UPDATE [dbo].[SmsSettings]
    SET [SenderField]=N'from_number'
    WHERE ISNULL([SenderField],N'') IN (N'',N'sender');

    UPDATE [dbo].[SmsSettings]
    SET [ApiKeyName]=N'Authorization'
    WHERE ISNULL([ApiKeyName],N'') IN (N'',N'Api-Key');
END;

-- Faraz SMS pattern defaults
IF EXISTS(
    SELECT 1 FROM [dbo].[SmsSettings]
    WHERE LOWER(ISNULL([Endpoint],N'')) LIKE N'%api.iranpayamak.com%'
      AND LOWER(ISNULL([Endpoint],N'')) LIKE N'%/ws/v1/sms/pattern%'
)
BEGIN
    UPDATE [dbo].[SmsSettings]
    SET [RecipientField]=N'recipient'
    WHERE ISNULL([RecipientField],N'') IN (N'',N'recipients');

    UPDATE [dbo].[SmsSettings]
    SET [RecipientMode]=N'scalar'
    WHERE ISNULL([RecipientMode],N'') IN (N'',N'array');

    UPDATE [dbo].[SmsSettings]
    SET [SenderField]=N'line_number'
    WHERE ISNULL([SenderField],N'') IN (N'',N'sender',N'from_number');

    UPDATE [dbo].[SmsSettings]
    SET [ApiKeyName]=N'Api-Key'
    WHERE ISNULL([ApiKeyName],N'') IN (N'',N'Authorization');

    UPDATE [dbo].[SmsSettings]
    SET [NumberFormatField]=N'number_format'
    WHERE ISNULL([NumberFormatField],N'')=N'';

    UPDATE [dbo].[SmsSettings]
    SET [NumberFormat]=N'english'
    WHERE ISNULL([NumberFormat],N'') IN (N'',N'en');

    UPDATE [dbo].[SmsSettings]
    SET [CodeField]=N'code'
    WHERE ISNULL([CodeField],N'')=N'';

    UPDATE [dbo].[SmsSettings]
    SET [OtpParameterField]=N'code'
    WHERE ISNULL([OtpParameterField],N'')=N'';
END;";
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task EnsureIndexesAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_Employees_PersonnelNumber' AND object_id=OBJECT_ID(N'dbo.Employees'))
    CREATE UNIQUE INDEX [IX_Employees_PersonnelNumber] ON [dbo].[Employees]([PersonnelNumber]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_Employees_NationalId' AND object_id=OBJECT_ID(N'dbo.Employees'))
    CREATE UNIQUE INDEX [IX_Employees_NationalId] ON [dbo].[Employees]([NationalId]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_Employees_Identifier' AND object_id=OBJECT_ID(N'dbo.Employees'))
    CREATE UNIQUE INDEX [IX_Employees_Identifier] ON [dbo].[Employees]([Identifier]) WHERE [Identifier] <> N'';
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_OrganizationStructureRevisions_RevisionCode' AND object_id=OBJECT_ID(N'dbo.OrganizationStructureRevisions'))
    CREATE UNIQUE INDEX [IX_OrganizationStructureRevisions_RevisionCode] ON [dbo].[OrganizationStructureRevisions]([RevisionCode]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_OrganizationNodes_Revision_Code' AND object_id=OBJECT_ID(N'dbo.OrganizationNodes'))
    CREATE UNIQUE INDEX [IX_OrganizationNodes_Revision_Code] ON [dbo].[OrganizationNodes]([OrganizationStructureRevisionId],[Code]);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_AuditLogs_CreatedAt' AND object_id=OBJECT_ID(N'dbo.AuditLogs'))
    CREATE INDEX [IX_AuditLogs_CreatedAt] ON [dbo].[AuditLogs]([CreatedAt]);";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}