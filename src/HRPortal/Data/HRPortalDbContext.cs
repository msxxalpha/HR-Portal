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
    }
}