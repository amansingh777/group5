using Corrin.Api.Domain;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Moq;

namespace Corrin.Tests;

public class DispatchBoardServiceTests
{
    // Monday. Fixed dates so the cells are predictable; "now" in the board is UtcNow, so SLA cases use wide margins.
    private static readonly DateOnly From = new(2030, 1, 7);

    private readonly Mock<IEngineerRepository> _engineers = new();
    private readonly Mock<IShiftRepository> _shifts = new();
    private readonly Mock<IWorkOrderReadRepository> _reads = new();

    public DispatchBoardServiceTests()
    {
        _engineers.Setup(r => r.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool?>()))
            .ReturnsAsync(new List<Engineer> { Engineer(1, 1), Engineer(2, 1) });
        _shifts.Setup(r => r.GetBetweenAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Shift>());
        _reads.Setup(r => r.ScheduledForEngineersAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<BoardJobRow>());
        _reads.Setup(r => r.UnscheduledAsync(It.IsAny<int?>())).ReturnsAsync(new List<BoardJobRow>());
        _reads.Setup(r => r.DisplacedJobIdsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<int>());
    }

    private DispatchBoardService Build() => new(_engineers.Object, _shifts.Object, _reads.Object);

    private static Engineer Engineer(int id, int regionId) => new()
    {
        Id = id,
        FullName = $"Engineer {id}",
        RegionId = regionId,
        Active = true,
        Skills = new List<EngineerSkill> { new() { EngineerId = id, SkillId = 2 }, new() { EngineerId = id, SkillId = 1 } }
    };

    private static Shift Shift(int engineerId, DateOnly date, TimeOnly start, TimeOnly end, int breakMinutes = 0) => new()
    {
        EngineerId = engineerId,
        ShiftDate = date,
        StartTime = start,
        EndTime = end,
        BreakMinutes = breakMinutes,
        ShiftType = "Standard"
    };

    private static BoardJobRow Booked(int id, int engineerId, DateTime start, string status = "Scheduled",
        DateTime? slaDueAt = null, DateTime? createdAt = null) => new(id, $"J-{id}", "P3", status, engineerId,
        start, start.AddHours(2), createdAt ?? start.AddDays(-1), slaDueAt);

    private static BoardJobRow Unbooked(int id, string status = "Requested", DateTime? slaDueAt = null, DateTime? createdAt = null) =>
        new(id, $"J-{id}", "P3", status, null, null, null, createdAt ?? new DateTime(2030, 1, 1), slaDueAt);

    [Fact]
    public async Task GetBoardAsync_DefaultsToSevenCellsFromTheGivenDate()
    {
        var board = await Build().GetBoardAsync(From, null, null);

        Assert.Equal(From, board.From);
        Assert.Equal(7, board.Days);
        Assert.All(board.Engineers, row => Assert.Equal(
            new[] { From, From.AddDays(1), From.AddDays(2), From.AddDays(3), From.AddDays(4), From.AddDays(5), From.AddDays(6) },
            row.Cells.Select(c => c.Date)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    [InlineData(14, 14)]
    [InlineData(15, 14)]
    [InlineData(99, 14)]
    public async Task GetBoardAsync_Days_IsClampedToOneThroughFourteen(int requested, int expected)
    {
        var board = await Build().GetBoardAsync(From, requested, null);

        Assert.Equal(expected, board.Days);
        Assert.All(board.Engineers, row => Assert.Equal(expected, row.Cells.Count));
    }

    [Fact]
    public async Task GetBoardAsync_NoEngineers_ReturnsNoRowsAndSkipsShiftAndBookingQueries()
    {
        _engineers.Setup(r => r.ListAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool?>()))
            .ReturnsAsync(new List<Engineer>());
        _reads.Setup(r => r.UnscheduledAsync(It.IsAny<int?>())).ReturnsAsync(new List<BoardJobRow> { Unbooked(4) });

        var board = await Build().GetBoardAsync(From, 7, null);

        Assert.Empty(board.Engineers);
        Assert.Equal(new[] { 4 }, board.Unscheduled.Select(j => j.Id));
        _shifts.Verify(r => r.GetBetweenAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IEnumerable<int>>()), Times.Never);
        _reads.Verify(r => r.ScheduledForEngineersAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task GetBoardAsync_EmptyCell_HasNoShiftTimesZeroBreakAndNoJobs()
    {
        var board = await Build().GetBoardAsync(From, 1, null);

        var cell = board.Engineers[0].Cells[0];
        Assert.Null(cell.ShiftStart);
        Assert.Null(cell.ShiftEnd);
        Assert.Equal(0, cell.BreakMinutes);
        Assert.Empty(cell.Jobs);
    }

    [Fact]
    public async Task GetBoardAsync_TwoShiftsOnOneDay_ShowsEarliestStartLatestEndAndSummedBreaks()
    {
        _shifts.Setup(r => r.GetBetweenAsync(From, From.AddDays(6), It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Shift>
            {
                Shift(1, From, new TimeOnly(8, 0), new TimeOnly(12, 0), breakMinutes: 15),
                Shift(1, From, new TimeOnly(13, 0), new TimeOnly(17, 30), breakMinutes: 30),
            });

        var board = await Build().GetBoardAsync(From, 7, null);

        var engineer1Day1 = board.Engineers.Single(e => e.EngineerId == 1).Cells[0];
        Assert.Equal(new TimeOnly(8, 0), engineer1Day1.ShiftStart);
        Assert.Equal(new TimeOnly(17, 30), engineer1Day1.ShiftEnd);
        Assert.Equal(45, engineer1Day1.BreakMinutes);
        // Engineer 2 has no shift that day, and the next day for engineer 1 has none either.
        var engineer2Day1 = board.Engineers.Single(e => e.EngineerId == 2).Cells[0];
        Assert.Null(engineer2Day1.ShiftStart);
        Assert.Null(board.Engineers.Single(e => e.EngineerId == 1).Cells[1].ShiftStart);
    }

    [Fact]
    public async Task GetBoardAsync_BookedJob_AppearsOnlyInItsEngineerAndStartDayCell()
    {
        var start = From.AddDays(2).ToDateTime(new TimeOnly(10, 0));
        _reads.Setup(r => r.ScheduledForEngineersAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<BoardJobRow> { Booked(20, 1, start) });

        var board = await Build().GetBoardAsync(From, 7, null);

        var engineer1 = board.Engineers.Single(e => e.EngineerId == 1);
        var placed = engineer1.Cells[2].Jobs.Single();
        Assert.Equal(20, placed.Id);
        Assert.Equal(start, placed.Start);
        Assert.Equal(start.AddHours(2), placed.End);
        Assert.Equal("Scheduled", placed.Status);
        for (var i = 0; i < 7; i++)
        {
            if (i != 2) Assert.Empty(engineer1.Cells[i].Jobs);
        }
        Assert.All(board.Engineers.Single(e => e.EngineerId == 2).Cells, c => Assert.Empty(c.Jobs));
    }

    [Fact]
    public async Task GetBoardAsync_BookingWindowIsFromStartOfFirstDayToStartOfDayAfterLast()
    {
        await Build().GetBoardAsync(From, 3, null);

        _reads.Verify(r => r.ScheduledForEngineersAsync(
            It.Is<IEnumerable<int>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { 1, 2 })),
            From.ToDateTime(TimeOnly.MinValue),
            From.AddDays(3).ToDateTime(TimeOnly.MinValue)), Times.Once);
    }

    [Fact]
    public async Task GetBoardAsync_NeedsRebook_FlagsOnlyUnstartedAllocatedJobsThatAPrecedingP1Displaced()
    {
        // 1: Allocated, no time, displaced -> flagged.
        // 2: Allocated, no time, never displaced -> not flagged.
        // 3: Requested, displaced note somehow present -> not flagged (only Allocated can need a rebook).
        // 5: Scheduled booked job with the note -> not flagged (it has a time).
        var start = From.ToDateTime(new TimeOnly(9, 0));
        _reads.Setup(r => r.UnscheduledAsync(It.IsAny<int?>())).ReturnsAsync(new List<BoardJobRow>
        {
            Unbooked(1, status: "Allocated"),
            Unbooked(2, status: "Allocated"),
            Unbooked(3, status: "Requested"),
        });
        _reads.Setup(r => r.ScheduledForEngineersAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<BoardJobRow> { Booked(5, 1, start) });
        // The repository is asked only about the Allocated, unstarted jobs. Here it also returns 3 and 5 to check the
        // service still refuses to flag them on status and time.
        _reads.Setup(r => r.DisplacedJobIdsAsync(It.Is<IEnumerable<int>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { 1, 2 }))))
            .ReturnsAsync(new List<int> { 1, 3, 5 });

        var board = await Build().GetBoardAsync(From, 7, null);

        var flags = board.Unscheduled.ToDictionary(j => j.Id, j => j.NeedsRebook);
        Assert.True(flags[1]);
        Assert.False(flags[2]);
        Assert.False(flags[3]);
        Assert.False(board.Engineers.Single(e => e.EngineerId == 1).Cells[0].Jobs.Single().NeedsRebook);
    }

    [Fact]
    public async Task GetBoardAsync_NoDisplacedJobs_NothingFlagged()
    {
        _reads.Setup(r => r.UnscheduledAsync(It.IsAny<int?>())).ReturnsAsync(new List<BoardJobRow> { Unbooked(1, status: "Allocated") });

        var board = await Build().GetBoardAsync(From, 7, null);

        Assert.All(board.Unscheduled, j => Assert.False(j.NeedsRebook));
    }

    [Fact]
    public async Task GetBoardAsync_SlaStateOnCellsAndUnscheduled_FollowsTheSlaRule()
    {
        var now = DateTime.UtcNow;
        var start = From.ToDateTime(new TimeOnly(9, 0));
        _reads.Setup(r => r.ScheduledForEngineersAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<BoardJobRow>
            {
                // Open and due an hour ago: breached.
                Booked(30, 1, start, status: "Scheduled", slaDueAt: now.AddHours(-1), createdAt: now.AddDays(-1)),
                // Open, created an hour ago, due in ten days: ok.
                Booked(31, 1, start.AddHours(3), status: "Scheduled", slaDueAt: now.AddDays(10), createdAt: now.AddHours(-1)),
            });
        _reads.Setup(r => r.UnscheduledAsync(It.IsAny<int?>())).ReturnsAsync(new List<BoardJobRow>
        {
            // No SLA due time: none.
            Unbooked(40, status: "Requested", slaDueAt: null),
            Unbooked(41, status: "Triaged", slaDueAt: now.AddHours(-2), createdAt: now.AddDays(-2)),
        });

        var board = await Build().GetBoardAsync(From, 1, null);

        var cellJobs = board.Engineers.Single(e => e.EngineerId == 1).Cells[0].Jobs.ToDictionary(j => j.Id, j => j.SlaState);
        Assert.Equal("breached", cellJobs[30]);
        Assert.Equal("ok", cellJobs[31]);
        var unscheduled = board.Unscheduled.ToDictionary(j => j.Id, j => j.SlaState);
        Assert.Equal("none", unscheduled[40]);
        Assert.Equal("breached", unscheduled[41]);
    }

    [Fact]
    public async Task GetBoardAsync_RegionFilter_IsPassedToEngineersAndUnscheduled()
    {
        await Build().GetBoardAsync(From, 7, 3);

        _engineers.Verify(r => r.ListAsync(3, null, true), Times.Once);
        _reads.Verify(r => r.UnscheduledAsync(3), Times.Once);
    }

    private static BoardJobRow Pending(int id, string priority, string status = "Requested", DateTime? slaDueAt = null) =>
        new(id, $"J-{id}", priority, status, null, null, null, new DateTime(2030, 1, 1), slaDueAt);

    [Fact]
    public async Task GetBoardAsync_UnscheduledOverTheCap_IsCappedAtTwoHundredAndTotalIsUncapped()
    {
        _reads.Setup(r => r.UnscheduledAsync(It.IsAny<int?>()))
            .ReturnsAsync(Enumerable.Range(1, 250).Select(i => Pending(i, "P3")).ToList());

        var board = await Build().GetBoardAsync(From, 7, null);

        Assert.Equal(200, board.Unscheduled.Count);
        Assert.Equal(250, board.UnscheduledTotal);
        // All equal on priority and SLA, so the cut is by id: 1 to 200 are kept.
        Assert.Equal(Enumerable.Range(1, 200), board.Unscheduled.Select(j => j.Id));
    }

    [Fact]
    public async Task GetBoardAsync_UnscheduledExactlyAtTheCap_IsNotTruncated()
    {
        _reads.Setup(r => r.UnscheduledAsync(It.IsAny<int?>()))
            .ReturnsAsync(Enumerable.Range(1, 200).Select(i => Pending(i, "P3")).ToList());

        var board = await Build().GetBoardAsync(From, 7, null);

        Assert.Equal(200, board.Unscheduled.Count);
        Assert.Equal(200, board.UnscheduledTotal);
    }

    [Fact]
    public async Task GetBoardAsync_NoUnscheduledJobs_ReturnsEmptyListAndZeroTotal()
    {
        var board = await Build().GetBoardAsync(From, 7, null);

        Assert.Empty(board.Unscheduled);
        Assert.Equal(0, board.UnscheduledTotal);
    }

    [Fact]
    public async Task GetBoardAsync_Unscheduled_OrdersRebookThenPriorityThenSlaThenId()
    {
        var now = DateTime.UtcNow;
        _reads.Setup(r => r.UnscheduledAsync(It.IsAny<int?>())).ReturnsAsync(new List<BoardJobRow>
        {
            Pending(1, "P3", slaDueAt: now.AddDays(1)),
            Pending(2, "P1", slaDueAt: null),
            Pending(3, "P2", slaDueAt: now.AddDays(2)),
            Pending(4, "P1", slaDueAt: now.AddDays(5)),
            Pending(5, "P1", slaDueAt: now.AddDays(2)),
            // Allocated with no time and displaced by a P1: flagged, so it sorts first whatever its priority.
            Pending(6, "P3", status: "Allocated", slaDueAt: now.AddDays(9)),
            // Same priority and SLA as 5, higher id: sorts after it.
            Pending(7, "P1", slaDueAt: now.AddDays(2)),
        });
        _reads.Setup(r => r.DisplacedJobIdsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<int> { 6 });

        var board = await Build().GetBoardAsync(From, 7, null);

        // Flagged 6, then P1 by SLA (5, 7, 4) with no-SLA 2 last among P1, then P2 (3), then P3 (1).
        Assert.Equal(new[] { 6, 5, 7, 4, 2, 3, 1 }, board.Unscheduled.Select(j => j.Id));
        Assert.True(board.Unscheduled[0].NeedsRebook);
        Assert.All(board.Unscheduled.Skip(1), j => Assert.False(j.NeedsRebook));
    }

    [Fact]
    public async Task GetBoardAsync_CapFullOfP1_KeepsFlaggedRowAndDropsTheLastP1()
    {
        // 200 P1 jobs fill the cap. A flagged P3 displaced job is still kept, and the highest-id P1 is the one cut.
        var rows = Enumerable.Range(1, 200).Select(i => Pending(i, "P1")).ToList();
        rows.Add(Pending(999, "P3", status: "Allocated"));
        _reads.Setup(r => r.UnscheduledAsync(It.IsAny<int?>())).ReturnsAsync(rows);
        _reads.Setup(r => r.DisplacedJobIdsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<int> { 999 });

        var board = await Build().GetBoardAsync(From, 7, null);

        Assert.Equal(200, board.Unscheduled.Count);
        Assert.Equal(201, board.UnscheduledTotal);
        Assert.Equal(999, board.Unscheduled[0].Id);
        Assert.True(board.Unscheduled[0].NeedsRebook);
        Assert.DoesNotContain(board.Unscheduled, j => j.Id == 200);
        Assert.Contains(board.Unscheduled, j => j.Id == 199);
    }
}
