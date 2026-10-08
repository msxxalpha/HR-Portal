using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public class HRPortalDbContext(DbContextOptions<HRPortalDbContext> options) : DbContext(options)
{
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<OtpSettings> OtpSettings => Set<OtpSettings>();
    public DbSet<SmsSettings> SmsSettings => Set<SmsSettings>();
    public DbSet<PayrollReportSettings> PayrollReportSettings => Set<PayrollReportSettings>();
    public DbSet<PersonnelOrderReportSettings> PersonnelOrderReportSettings => Set<PersonnelOrderReportSettings>();
    public DbSet<InputQuery> InputQueries => Set<InputQuery>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<OrganizationStructureRevision> OrganizationStructureRevisions => Set<OrganizationStructureRevision>();
    public DbSet<OrganizationNode> OrganizationNodes => Set<OrganizationNode>();
    public DbSet<OrganizationChange> OrganizationChanges => Set<OrganizationChange>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<EmployeeRole> EmployeeRoles => Set<EmployeeRole>();
    public DbSet<AnnouncementCategory> AnnouncementCategories => Set<AnnouncementCategory>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowTransition> WorkflowTransitions => Set<WorkflowTransition>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowTask> WorkflowTasks => Set<WorkflowTask>();
    public DbSet<WorkflowHistory> WorkflowHistory => Set<WorkflowHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Employee>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PersonnelNumber).IsUnique();
            e.Property(x => x.PersonalPasswordHash).HasMaxLength(1000);
            e.HasIndex(x => x.NationalId).IsUnique();
            e.HasIndex(x => x.Identifier)
                .IsUnique()
                .HasFilter("[Identifier] IS NOT NULL AND [Identifier] <> N''");
            e.HasOne(x => x.OrganizationUnit).WithMany().HasForeignKey(x => x.OrganizationUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OrganizationDepartment).WithMany().HasForeignKey(x => x.OrganizationDepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OrganizationSection).WithMany().HasForeignKey(x => x.OrganizationSectionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<OrganizationStructureRevision>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RevisionCode).IsUnique();
        });
        modelBuilder.Entity<OrganizationNode>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.OrganizationStructureRevisionId, x.Code }).IsUnique();
            e.HasOne(x => x.Revision).WithMany(x => x.Nodes).HasForeignKey(x => x.OrganizationStructureRevisionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<OrganizationChange>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Revision).WithMany(x => x.Changes).HasForeignKey(x => x.OrganizationStructureRevisionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<OtpChallenge>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PersonnelNumber, x.CreatedAt });
        });
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CreatedAt);
        });
        modelBuilder.Entity<SystemSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<OtpSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<SmsSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<PayrollReportSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<PersonnelOrderReportSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<InputQuery>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Title).IsUnique();
        });
        modelBuilder.Entity<AdminUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
        });
        modelBuilder.Entity<Role>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => x.Code).IsUnique();
            // RolePermission -> Role is configured from RolePermission below.

        });
        modelBuilder.Entity<Permission>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Module).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<RolePermission>(e =>
        {
            e.HasKey(x => x.Id);
            e.Ignore("RoleId1");
            e.Ignore("PermissionId1");
            e.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();
            e.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission)
                .WithMany()
                .HasForeignKey(x => x.PermissionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AnnouncementCategory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Title).IsUnique();
        });
        modelBuilder.Entity<Announcement>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(700);
            e.Property(x => x.Priority).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.IsActive, x.StartAtUtc, x.EndAtUtc });
            e.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<EmployeeRole>(e =>
        {
            e.HasKey(x => x.Id);
            e.Ignore("RoleId1");
            e.Ignore("EmployeeId1");
            e.HasIndex(x => new { x.EmployeeId, x.RoleId }).IsUnique();
            e.HasOne(x => x.Employee)
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}