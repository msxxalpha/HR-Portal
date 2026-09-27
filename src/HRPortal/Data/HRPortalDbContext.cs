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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PersonnelNumber).IsUnique();
            entity.HasIndex(x => x.NationalId).IsUnique();

            entity.HasOne(x => x.OrganizationUnit)
                .WithMany()
                .HasForeignKey(x => x.OrganizationUnitId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.OrganizationDepartment)
                .WithMany()
                .HasForeignKey(x => x.OrganizationDepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.OrganizationSection)
                .WithMany()
                .HasForeignKey(x => x.OrganizationSectionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrganizationStructureRevision>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RevisionCode).IsUnique();
        });

        modelBuilder.Entity<OrganizationNode>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.OrganizationStructureRevisionId, x.Code }).IsUnique();

            entity.HasOne(x => x.Revision)
                .WithMany(x => x.Nodes)
                .HasForeignKey(x => x.OrganizationStructureRevisionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrganizationChange>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne(x => x.Revision)
                .WithMany(x => x.Changes)
                .HasForeignKey(x => x.OrganizationStructureRevisionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OtpChallenge>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.PersonnelNumber, x.CreatedAt });
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<SystemSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<OtpSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<SmsSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<PayrollReportSettings>().HasKey(x => x.Id);
    }
}