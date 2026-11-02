using Corrin.Api.Data;
using Corrin.Api.Domain;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Corrin.Tests;

// The displacement flag (R13) and the board's booking and unscheduled queries are SQL, so they run against the real
// seeded MySQL database. Every write happens inside one transaction that is rolled back after each test. The window
// used (6 January 2031) holds no seeded bookings, and job 235 is used only as a template for customer, site and job
// type. Shares the "api" collection so no API test runs while these transactions are open.
[Collection("api")]
public class WorkOrderReadRepositoryTests : IAsyncLifetime
{
    private const int TemplateJobId = 235;
    private const string DisplacedNote = "Displaced by P1 J-P1TEST at 2031-01-06 10:00.";
    private static readonly DateOnly BoardDay = new(2031, 1, 6);
    private static readonly DateTime SlotStart = new(2031, 1, 6, 10, 0, 0);

    private readonly TestApiFactory _factory;
    private IServiceScope _scope = null!;
    private CorrinDbContext _db = null!;
    private IDbContextTransaction _tx = null!;

    public WorkOrderReadRepositoryTests(TestApiFactory factory) => _factory = factory;

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

    private WorkOrderReadRepository Reads() => new(_db);

    private DispatchBoardService Board() =>
        new(new EngineerRepository(_db), new ShiftRepository(_db), Reads());

    private async Task<Job> AddJobAsync(string jobRef, string status, int? engineerId, DateTime? start, string? note)
    {
        var template = await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == TemplateJobId);
        var created = new DateTime(2031, 1, 1, 8, 0, 0);
        var job = new Job
        {
            JobRef = jobRef,
            CustomerId = template.CustomerId,
            SiteId = template.SiteId,
            JobTypeId = template.JobTypeId,
            RegionId = 1,
            Priority = "P3",
            Status = status,
            EngineerId = engineerId,
            ScheduledStart = start,
            ScheduledEnd = start?.AddHours(2),
            CreatedAt = created,
            UpdatedAt = created
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        if (note is not null)
        {
            _db.JobNotes.Add(new JobNote { JobId = job.Id, Author = "scheduler", Note = note, CreatedAt = created });
            await _db.SaveChangesAsync();
        }
        return job;
    }

    [Fact]
    public async Task Board_DisplacedAllocatedWithNoTime_IsFlaggedNeedsRebook_AndScheduledWithSameNoteIsNot()
    {
        var displaced = await AddJobAsync("TST-RBK-A", "Allocated", 1, null, DisplacedNote);
        var booked = await AddJobAsync("TST-RBK-B", "Scheduled", 1, SlotStart, DisplacedNote);
        var untouched = await AddJobAsync("TST-RBK-C", "Allocated", 1, null, null);

        var board = await Board().GetBoardAsync(BoardDay, 1, 1);

        var unscheduled = board.Unscheduled.ToDictionary(j => j.Id);
        Assert.True(unscheduled[displaced.Id].NeedsRebook);
        Assert.Equal("Allocated", unscheduled[displaced.Id].Status);
        Assert.Null(unscheduled[displaced.Id].Start);
        Assert.False(unscheduled[untouched.Id].NeedsRebook);
        Assert.False(unscheduled.ContainsKey(booked.Id));

        var placed = board.Engineers.Single(e => e.EngineerId == 1).Cells[0].Jobs.Single();
        Assert.Equal(booked.Id, placed.Id);
        Assert.Equal("Scheduled", placed.Status);
        Assert.Equal(SlotStart, placed.Start);
        Assert.False(placed.NeedsRebook);
    }

    [Fact]
    public async Task DisplacedJobIds_ReturnsOnlyJobsWhoseNoteStartsWithThePrefix()
    {
        var displaced = await AddJobAsync("TST-RBK-A", "Allocated", 1, null, DisplacedNote);
        var booked = await AddJobAsync("TST-RBK-B", "Scheduled", 1, SlotStart, DisplacedNote);
        var notAtStart = await AddJobAsync("TST-RBK-D", "Allocated", 1, null, "Note: " + DisplacedNote);
        var noTrailingSpace = await AddJobAsync("TST-RBK-E", "Allocated", 1, null, "Displaced by P1");
        var noNote = await AddJobAsync("TST-RBK-F", "Allocated", 1, null, null);

        var ids = await Reads().DisplacedJobIdsAsync(new[] { displaced.Id, booked.Id, notAtStart.Id, noTrailingSpace.Id, noNote.Id });

        // The query does not filter by status; the board applies the Allocated and no-time rules on top.
        Assert.Equal(new[] { displaced.Id, booked.Id }.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Fact]
    public async Task Board_CancelledBookingIsNotShown_AndTriagedWithNoTimeIsUnscheduledWithoutFlag()
    {
        var cancelled = await AddJobAsync("TST-RBK-X", "Cancelled", 1, SlotStart, null);
        var triaged = await AddJobAsync("TST-RBK-Y", "Triaged", null, null, null);

        var board = await Board().GetBoardAsync(BoardDay, 1, 1);

        var engineer1Day1 = board.Engineers.Single(e => e.EngineerId == 1).Cells[0];
        Assert.DoesNotContain(engineer1Day1.Jobs, j => j.Id == cancelled.Id);
        var row = board.Unscheduled.Single(j => j.Id == triaged.Id);
        Assert.Equal("Triaged", row.Status);
        Assert.False(row.NeedsRebook);
    }

    [Fact]
    public async Task Unscheduled_IncludesRequestedTriagedAndAllocatedWithoutTime_ExcludesScheduledAndCancelled()
    {
        var allocated = await AddJobAsync("TST-RBK-G", "Allocated", 1, null, null);
        var scheduled = await AddJobAsync("TST-RBK-H", "Scheduled", 1, SlotStart, null);
        var cancelled = await AddJobAsync("TST-RBK-I", "Cancelled", null, null, null);

        var rows = await Reads().UnscheduledAsync(1);
        var ids = rows.Select(r => r.Id).ToHashSet();

        Assert.Contains(allocated.Id, ids);
        Assert.DoesNotContain(scheduled.Id, ids);
        Assert.DoesNotContain(cancelled.Id, ids);
        Assert.All(rows, r => Assert.Contains(r.Status, new[] { "Requested", "Triaged", "Allocated" }));
    }
}
