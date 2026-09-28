using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public class HRPortalDbContext(DbContextOptions<HRPortalDbContext> options) : DbContext(options)
{
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<OtpSettings> OtpSettings => Set<OtpSettings>();
    public DbSet<SmsSettings> SmsSettings => Set<SmsSettings>();
    public DbSet<PayrollReportSettings> PayrollReportSettings => Set<PayrollReportSettings>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Employee>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PersonnelNumber).IsUnique();
            e.HasIndex(x => x.NationalId).IsUnique();
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
        modelBuilder.Entity<EmployeeRole>(e =>
        {
            e.HasKey(x => x.Id);
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