using Corrin.Api.Data;
using Corrin.Api.Domain;
using Corrin.Api.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Corrin.Tests;

// The first-time-fix rules (later visits, cancelled jobs, range bounds) are SQL, so they are checked against the real
// seeded MySQL database. Every write happens inside one transaction that is rolled back after each test, and the
// range used (January 2031) holds no seeded visits, so the counts are exact. Shares the "api" collection so that
// no API test runs while these transactions are open.
// Migration 007 backfills visit 1 for the 207 seeded Completed jobs, so the jobs these tests write visits to are
// chosen from jobs that have no visit rows (Scheduled or Requested). Otherwise visit 1 would clash with the backfill.
[Collection("api")]
public class FirstTimeFixRepositoryTests : IAsyncLifetime
{
    private static readonly DateTime RangeFrom = new(2031, 1, 1);
    private static readonly DateTime RangeTo = new(2031, 2, 1);

    private readonly TestApiFactory _factory;
    private IServiceScope _scope = null!;
    private CorrinDbContext _db = null!;
    private IDbContextTransaction _tx = null!;

    public FirstTimeFixRepositoryTests(TestApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<CorrinDbContext>();
        _tx = await _db.Database.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        await _tx.RollbackAsync();
        await _tx.DisposeAsync();
        _scope.Dispose();
    }

    private static Visit NewVisit(Job job, int visitNo, string? outcome, DateTime? actualEnd) => new()
    {
        JobId = job.Id,
        VisitNo = visitNo,
        Outcome = outcome,
        ActualStart = actualEnd?.AddHours(-1),
        ActualEnd = actualEnd,
        CreatedAt = new DateTime(2031, 1, 1)
    };

    // Non-cancelled seeded jobs that have no visit rows yet (migration 007 only backfills Completed jobs).
    private Task<List<Job>> UnvisitedJobsAsync(int count) =>
        _db.Jobs.Where(j => j.Status != "Cancelled" && !_db.Visits.Any(v => v.JobId == j.Id))
            .OrderBy(j => j.Id).Take(count).ToListAsync();

    // Seven non-cancelled seeded jobs with no visits, with the scenarios described on each line. Returned in the order listed.
    private async Task<List<Job>> ArrangeScenarios()
    {
        var jobs = await UnvisitedJobsAsync(7);
        Assert.Equal(7, jobs.Count);

        jobs[3].Status = "Cancelled";                    // Fixed visit 1 in range, but the job is cancelled
        _db.Visits.AddRange(
            NewVisit(jobs[0], 1, "Fixed", new DateTime(2031, 1, 5, 10, 0, 0)),     // counted, fixed
            NewVisit(jobs[1], 1, "Fixed", new DateTime(2031, 1, 10, 10, 0, 0)),    // counted, NOT fixed: a later visit exists
            NewVisit(jobs[1], 2, "Fixed", new DateTime(2031, 1, 12, 10, 0, 0)),
            NewVisit(jobs[2], 1, "NoAccess", new DateTime(2031, 1, 15, 10, 0, 0)), // counted, NOT fixed: no access
            NewVisit(jobs[3], 1, "Fixed", new DateTime(2031, 1, 20, 10, 0, 0)),    // excluded: cancelled
            NewVisit(jobs[4], 1, "Fixed", RangeTo),                                // excluded: ends exactly at range end
            NewVisit(jobs[5], 1, "Fixed", RangeFrom),                              // counted, fixed: ends exactly at range start
            NewVisit(jobs[6], 1, null, null));                                     // excluded: visit 1 not yet completed
        await _db.SaveChangesAsync();
        return jobs;
    }

    [Fact]
    public async Task GetCohortCountsAsync_ScenarioRows_CountsFirstVisitsInRangeNotCancelledAndFixedOnlyWithoutLaterVisit()
    {
        await ArrangeScenarios();
        var repo = new FirstTimeFixRepository(_db);

        var rows = await repo.GetCohortCountsAsync(RangeFrom, RangeTo);

        // Attended: jobs 0, 1, 2 and 5. Fixed first time: jobs 0 and 5 (job 1 has a later visit, job 2 was NoAccess).
        Assert.Equal(4, rows.Sum(r => r.JobsAttended));
        Assert.Equal(2, rows.Sum(r => r.FixedFirstTime));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.RegionName)));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.JobTypeName)));
    }

    [Fact]
    public async Task GetCohortCountsAsync_JobFirstTimeFixFlagDisagreesWithVisits_VisitHistoryDecides()
    {
        var jobs = await UnvisitedJobsAsync(2);
        // Flag says not fixed on a job whose visit 1 was Fixed; flag says fixed on a job whose visit 1 was NoAccess.
        jobs[0].FirstTimeFix = false;
        jobs[1].FirstTimeFix = true;
        _db.Visits.AddRange(
            NewVisit(jobs[0], 1, "Fixed", new DateTime(2031, 1, 5, 10, 0, 0)),
            NewVisit(jobs[1], 1, "NoAccess", new DateTime(2031, 1, 6, 10, 0, 0)));
        await _db.SaveChangesAsync();
        var repo = new FirstTimeFixRepository(_db);

        var rows = await repo.GetCohortCountsAsync(RangeFrom, RangeTo);

        Assert.Equal(2, rows.Sum(r => r.JobsAttended));
        Assert.Equal(1, rows.Sum(r => r.FixedFirstTime));
    }

    [Fact]
    public async Task GetCohortCountsAsync_NoVisitsInRange_ReturnsNoRows()
    {
        var repo = new FirstTimeFixRepository(_db);

        var rows = await repo.GetCohortCountsAsync(RangeFrom, RangeTo);

        Assert.Empty(rows);
    }

    // The body of migration 007, read from the same embedded resource that DbUp runs at API startup.
    private static string BackfillMigrationSql()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("Corrin.Api.db.migrations.007_backfill_visits.sql")
            ?? throw new InvalidOperationException("Migration 007 is not embedded in Corrin.Api.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task BackfillVisitsMigration_RerunOnAlreadyBackfilledTable_InsertsZeroRows()
    {
        // Seeded state after 007: one visit per Completed job with an outcome, and nothing else.
        Assert.Equal(207, await _db.Visits.CountAsync());

        var inserted = await _db.Database.ExecuteSqlRawAsync(BackfillMigrationSql());

        Assert.Equal(0, inserted);
        Assert.Equal(207, await _db.Visits.CountAsync());
    }

    [Fact]
    public async Task BackfillVisitsMigration_RerunAfterVisitsRemoved_RestoresOneVisitPerCompletedJob()
    {
        // Guards against the idempotency test passing only because the INSERT matches nothing.
        await _db.Visits.ExecuteDeleteAsync();

        var inserted = await _db.Database.ExecuteSqlRawAsync(BackfillMigrationSql());

        Assert.Equal(207, inserted);
        Assert.Equal(207, await _db.Visits.CountAsync());
        Assert.All(await _db.Visits.AsNoTracking().ToListAsync(), v => Assert.Equal(1, v.VisitNo));
    }
}
