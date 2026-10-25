using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Modules.Performance;

public static class PerformanceModelConfiguration
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<PerformanceDomain>(e =>
        {
            e.ToTable("PerformanceDomains");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });
        b.Entity<PerformanceQuestion>(e =>
        {
            e.ToTable("PerformanceQuestions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.Domain).WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PerformancePosition>(e =>
        {
            e.ToTable("PerformancePositions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });
        b.Entity<PerformancePositionQuestion>(e =>
        {
            e.ToTable("PerformancePositionQuestions");
            e.HasKey(x => x.Id);
            e.Property(x => x.MaxScore).HasPrecision(12, 2);
            e.HasIndex(x => new { x.PositionId, x.QuestionId }).IsUnique();
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PerformanceEmployeePosition>(e =>
        {
            e.ToTable("PerformanceEmployeePositions");
            e.HasKey(x => x.EmployeeId);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PerformancePeriod>(e =>
        {
            e.ToTable("PerformancePeriods");
            e.HasKey(x => x.Id);
            e.HasOne<OrganizationStructureRevision>().WithMany()
                .HasForeignKey(x => x.OrganizationStructureRevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PerformanceAssignment>(e =>
        {
            e.ToTable("PerformanceAssignments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PeriodId, x.EmployeeId }).IsUnique();
            e.HasOne(x => x.Period).WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EvaluatorEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PerformanceEvaluatorChain>(e =>
        {
            e.ToTable("PerformanceEvaluatorChains");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PeriodId, x.SubordinateEvaluatorEmployeeId }).IsUnique();
            e.HasOne<PerformancePeriod>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.SubordinateEvaluatorEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.SuperiorEvaluatorEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PerformanceEvaluation>(e =>
        {
            e.ToTable("PerformanceEvaluations");
            e.HasKey(x => x.Id);
            e.Property(x => x.FinalScore).HasPrecision(12, 2);
            e.Property(x => x.FinalMaxScore).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(30);
            e.HasIndex(x => new { x.PeriodId, x.EmployeeId }).IsUnique();
            e.HasOne(x => x.Period).WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EvaluatorEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.LastReviewedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PerformanceScore>(e =>
        {
            e.ToTable("PerformanceScores");
            e.HasKey(x => x.Id);
            e.Property(x => x.Score).HasPrecision(12, 2);
            e.Property(x => x.MaxScore).HasPrecision(12, 2);
            e.HasIndex(x => new { x.EvaluationId, x.QuestionId }).IsUnique();
            e.HasOne(x => x.Evaluation).WithMany(x => x.Scores)
                .HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PerformanceQuestion>().WithMany()
                .HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PerformanceScoreChange>(e =>
        {
            e.ToTable("PerformanceScoreChanges");
            e.HasKey(x => x.Id);
            e.Property(x => x.OldScore).HasPrecision(12, 2);
            e.Property(x => x.NewScore).HasPrecision(12, 2);
            e.HasIndex(x => x.EvaluationId);
            e.HasOne<PerformanceEvaluation>().WithMany().HasForeignKey(x => x.EvaluationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ChangedByEmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
