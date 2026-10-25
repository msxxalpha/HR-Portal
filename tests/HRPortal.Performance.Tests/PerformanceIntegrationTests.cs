using HRPortal.Data;
using HRPortal.Models;
using HRPortal.Modules.Performance;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRPortal.Performance.Tests;

public sealed class PerformanceIntegrationTests
{
    private static HRPortalDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new HRPortalDbContext(options);
    }

    private static async Task SeedAsync(HRPortalDbContext db)
    {
        db.Employees.AddRange(
            new Employee { Id = 10, PersonnelNumber = "101", NationalId = "1234567890",
                FirstName = "کارمند", LastName = "اول", Mobile = "09123456789", Gender = "مرد", Status = "فعال" },
            new Employee { Id = 20, PersonnelNumber = "202", NationalId = "1234567891",
                FirstName = "مدیر", LastName = "ارزیاب", Mobile = "09123456788", Gender = "مرد", Status = "فعال" }
        );
        db.OrganizationStructureRevisions.Add(new OrganizationStructureRevision
        {
            Id = 1, RevisionCode = "REV-001", Title = "ساختار", IsFinalized = true, EffectiveDate = DateTime.Today
        });
        db.PerformanceDomains.Add(new PerformanceDomain { Id = 1, Code = "DOM-001", Title = "کیفیت" });
        db.PerformanceQuestions.AddRange(
            new PerformanceQuestion { Id = 1, Code = "Q-001", Title = "کیفیت کار",
                Text = "کیفیت کار", DomainId = 1 },
            new PerformanceQuestion { Id = 2, Code = "Q-002", Title = "نظم",
                Text = "نظم", DomainId = 1 }
        );
        db.PerformancePositions.Add(new PerformancePosition { Id = 1, Code = "POS-001", Title = "کارشناس" });
        db.PerformancePositionQuestions.AddRange(
            new PerformancePositionQuestion { Id = 1, PositionId = 1, QuestionId = 1, MaxScore = 80, SortOrder = 1 },
            new PerformancePositionQuestion { Id = 2, PositionId = 1, QuestionId = 2, MaxScore = 20, SortOrder = 2 }
        );
        db.PerformanceEmployeePositions.Add(new PerformanceEmployeePosition { EmployeeId = 10, PositionId = 1 });
        db.PerformancePeriods.Add(new PerformancePeriod
        {
            Id = 1, Title = "دوره تست", StartAt = DateTime.Now.AddDays(-1),
            EndAt = DateTime.Now.AddDays(1), OrganizationStructureRevisionId = 1, IsOpen = true
        });
        db.PerformanceAssignments.Add(new PerformanceAssignment
        {
            Id = 1, PeriodId = 1, EmployeeId = 10, EvaluatorEmployeeId = 20
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task QuickEvaluationInitializesFullScoreAndNeverResetsDeductions()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = new PerformanceService(db);

        var first = await service.QuickEvaluateAsync(1, 20);
        Assert.Equal(1, first.Created);
        var evaluation = await db.PerformanceEvaluations.Include(x => x.Scores).SingleAsync();
        Assert.Equal(100m, evaluation.FinalMaxScore);
        Assert.Equal(100m, evaluation.FinalScore);
        Assert.Equal(new[] { 20m, 80m }, evaluation.Scores.OrderBy(x => x.Score).Select(x => x.Score));

        await service.SaveAsync(1, 10, 20,
        [
            new PerformanceService.ScoreInput(1, 60m, "تأخیر در رسیدگی"),
            new PerformanceService.ScoreInput(2, 20m, null)
        ], "کسر امتیاز کیفیت کار");

        var second = await service.QuickEvaluateAsync(1, 20);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.AlreadyEvaluated);

        var after = await db.PerformanceEvaluations.SingleAsync();
        Assert.Equal(80m, after.FinalScore);
        Assert.Equal(100m, after.FinalMaxScore);
        var history = await db.PerformanceScoreChanges.SingleAsync();
        Assert.Equal(80m, history.OldScore);
        Assert.Equal(60m, history.NewScore);
        Assert.Equal(20, history.ChangedByEmployeeId);
    }

    [Fact]
    public async Task UnassignedEvaluatorCannotReadOrChangeEmployeeEvaluation()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = new PerformanceService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ScoreRowsAsync(1, 10, 999));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(1, 10, 999,
            [new PerformanceService.ScoreInput(1, 80m, null), new PerformanceService.ScoreInput(2, 20m, null)], null));
    }

    [Fact]
    public async Task ClosedPeriodsCannotBeModified()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = new PerformanceService(db);
        var period = await db.PerformancePeriods.SingleAsync();
        period.IsOpen = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.QuickEvaluateAsync(1, 20));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(1, 10, 20,
            [new PerformanceService.ScoreInput(1, 80m, null), new PerformanceService.ScoreInput(2, 20m, null)], null));
    }

    [Fact]
    public async Task ExistingEvaluationRequiresReasonToChangeScore()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = new PerformanceService(db);
        await service.QuickEvaluateAsync(1, 20);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(1, 10, 20,
            [new PerformanceService.ScoreInput(1, 10m, null), new PerformanceService.ScoreInput(2, 20m, null)], ""));
        Assert.Equal(100m, (await db.PerformanceEvaluations.SingleAsync()).FinalScore);
    }
}
