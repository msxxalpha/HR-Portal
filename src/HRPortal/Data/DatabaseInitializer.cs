using HRPortal.Models;
using Microsoft.EntityFrameworkCore;
namespace HRPortal.Data;
public static class DatabaseInitializer
{
 public static async Task InitializeAsync(HRPortalDbContext db)
 {
  await db.Database.EnsureCreatedAsync();
  await UpgradeExistingDatabaseAsync(db);
  if(!await db.SystemSettings.AnyAsync())db.SystemSettings.Add(new SystemSettings{OrganizationName="شرکت کمک فنرسازی ایندامین سایپا"});
  if(!await db.OtpSettings.AnyAsync())db.OtpSettings.Add(new OtpSettings());
  if(!await db.SmsSettings.AnyAsync())db.SmsSettings.Add(new SmsSettings());
  if(!await db.PayrollReportSettings.AnyAsync())db.PayrollReportSettings.Add(new PayrollReportSettings());
  if(!await db.OrganizationStructureRevisions.AnyAsync())db.OrganizationStructureRevisions.Add(new OrganizationStructureRevision{RevisionCode="ORG-001",Title="نسخه اولیه ساختار سازمانی",EffectiveDate=DateTime.Today,IsFinalized=true,FinalizedAt=DateTime.UtcNow});
  await db.SaveChangesAsync();
 }
 private static async Task UpgradeExistingDatabaseAsync(HRPortalDbContext db)
 {
  var sql=@"
IF COL_LENGTH('SystemSettings','ShortName') IS NULL ALTER TABLE SystemSettings ADD ShortName nvarchar(100) NOT NULL CONSTRAINT DF_SystemSettings_ShortName DEFAULT N'HR';
IF COL_LENGTH('SystemSettings','Slogan') IS NULL ALTER TABLE SystemSettings ADD Slogan nvarchar(500) NOT NULL CONSTRAINT DF_SystemSettings_Slogan DEFAULT N'';
IF COL_LENGTH('SystemSettings','FooterText') IS NULL ALTER TABLE SystemSettings ADD FooterText nvarchar(500) NOT NULL CONSTRAINT DF_SystemSettings_FooterText DEFAULT N'';
IF COL_LENGTH('SystemSettings','Website') IS NULL ALTER TABLE SystemSettings ADD Website nvarchar(500) NOT NULL CONSTRAINT DF_SystemSettings_Website DEFAULT N'';
IF COL_LENGTH('SystemSettings','Phone') IS NULL ALTER TABLE SystemSettings ADD Phone nvarchar(100) NOT NULL CONSTRAINT DF_SystemSettings_Phone DEFAULT N'';
IF COL_LENGTH('SystemSettings','Email') IS NULL ALTER TABLE SystemSettings ADD Email nvarchar(200) NOT NULL CONSTRAINT DF_SystemSettings_Email DEFAULT N'';
IF COL_LENGTH('SystemSettings','EconomicCode') IS NULL ALTER TABLE SystemSettings ADD EconomicCode nvarchar(100) NOT NULL CONSTRAINT DF_SystemSettings_EconomicCode DEFAULT N'';
IF COL_LENGTH('SystemSettings','NationalId') IS NULL ALTER TABLE SystemSettings ADD NationalId nvarchar(100) NOT NULL CONSTRAINT DF_SystemSettings_NationalId DEFAULT N'';
IF COL_LENGTH('SystemSettings','FaviconUrl') IS NULL ALTER TABLE SystemSettings ADD FaviconUrl nvarchar(500) NOT NULL CONSTRAINT DF_SystemSettings_FaviconUrl DEFAULT N'';

IF OBJECT_ID('OrganizationStructureRevisions','U') IS NULL CREATE TABLE OrganizationStructureRevisions(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrganizationStructureRevisions PRIMARY KEY,RevisionCode nvarchar(100) NOT NULL,EffectiveDate date NOT NULL,Title nvarchar(300) NOT NULL,Notes nvarchar(max) NULL,IsFinalized bit NOT NULL,FinalizedAt datetime2 NULL,CreatedAt datetime2 NOT NULL);
IF OBJECT_ID('OrganizationNodes','U') IS NULL CREATE TABLE OrganizationNodes(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrganizationNodes PRIMARY KEY,OrganizationStructureRevisionId int NOT NULL,ParentId int NULL,Code nvarchar(100) NOT NULL,Title nvarchar(300) NOT NULL,RankType nvarchar(50) NOT NULL,SortOrder int NOT NULL,IsActive bit NOT NULL,Notes nvarchar(max) NULL);
IF OBJECT_ID('OrganizationChanges','U') IS NULL CREATE TABLE OrganizationChanges(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrganizationChanges PRIMARY KEY,OrganizationStructureRevisionId int NOT NULL,ChangeType nvarchar(100) NOT NULL,EntityCode nvarchar(100) NOT NULL,Description nvarchar(1000) NOT NULL,ChangedAt datetime2 NOT NULL);
IF OBJECT_ID('Employees','U') IS NULL CREATE TABLE Employees(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,PersonnelNumber nvarchar(50) NOT NULL,NationalId nvarchar(20) NOT NULL,FirstName nvarchar(100) NOT NULL,LastName nvarchar(150) NOT NULL,Mobile nvarchar(30) NOT NULL,Gender nvarchar(20) NOT NULL,OrganizationUnitId int NULL,OrganizationDepartmentId int NULL,OrganizationSectionId int NULL,PositionTitle nvarchar(300) NULL,EmploymentType nvarchar(100) NULL,Email nvarchar(200) NULL,FatherName nvarchar(100) NULL,Status nvarchar(50) NOT NULL,IsSystemUser bit NOT NULL,IsSystemAdministrator bit NOT NULL,CreatedAt datetime2 NOT NULL,UpdatedAt datetime2 NOT NULL);
IF OBJECT_ID('OtpChallenges','U') IS NULL CREATE TABLE OtpChallenges(Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OtpChallenges PRIMARY KEY,EmployeeId int NULL,PersonnelNumber nvarchar(50) NOT NULL,Mobile nvarchar(30) NOT NULL,CodeHash nvarchar(1000) NOT NULL,CreatedAt datetime2 NOT NULL,ExpiresAt datetime2 NOT NULL,AttemptCount int NOT NULL,IsConsumed bit NOT NULL,SmsSent bit NOT NULL,SmsResponse nvarchar(2000) NULL);
IF OBJECT_ID('AuditLogs','U') IS NULL CREATE TABLE AuditLogs(Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,EmployeeId int NULL,Action nvarchar(200) NOT NULL,EntityName nvarchar(200) NOT NULL,EntityId nvarchar(100) NULL,Description nvarchar(max) NULL,IpAddress nvarchar(100) NULL,CreatedAt datetime2 NOT NULL);

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Employees_PersonnelNumber') CREATE UNIQUE INDEX IX_Employees_PersonnelNumber ON Employees(PersonnelNumber);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Employees_NationalId') CREATE UNIQUE INDEX IX_Employees_NationalId ON Employees(NationalId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_OrganizationStructureRevisions_RevisionCode') CREATE UNIQUE INDEX IX_OrganizationStructureRevisions_RevisionCode ON OrganizationStructureRevisions(RevisionCode);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_OrganizationNodes_Revision_Code') CREATE UNIQUE INDEX IX_OrganizationNodes_Revision_Code ON OrganizationNodes(OrganizationStructureRevisionId,Code);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_OrganizationNodes_Revisions') ALTER TABLE OrganizationNodes ADD CONSTRAINT FK_OrganizationNodes_Revisions FOREIGN KEY(OrganizationStructureRevisionId) REFERENCES OrganizationStructureRevisions(Id) ON DELETE CASCADE;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_OrganizationNodes_Parent') ALTER TABLE OrganizationNodes ADD CONSTRAINT FK_OrganizationNodes_Parent FOREIGN KEY(ParentId) REFERENCES OrganizationNodes(Id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_OrganizationChanges_Revisions') ALTER TABLE OrganizationChanges ADD CONSTRAINT FK_OrganizationChanges_Revisions FOREIGN KEY(OrganizationStructureRevisionId) REFERENCES OrganizationStructureRevisions(Id) ON DELETE CASCADE;
";
  await db.Database.ExecuteSqlRawAsync(sql);
 }
}