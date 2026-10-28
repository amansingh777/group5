using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Moq;

namespace Corrin.Tests;

// P1 displacement in WorkOrderSchedulingService: the overlap read is an untracked snapshot, and every displaced job is
// re-read under its row lock before it is moved. Repositories are mocked; EngineerBookingRules is real.
public class WorkOrderP1DisplacementTests
{
    private const int EngineerId = 2;
    private const int P1JobId = 10;
    private static readonly DateOnly Day = WorkOrderTestData.Day;

    private readonly Mock<IJobRepository> _jobs = new();
    private readonly Mock<IEngineerRepository> _engineers = new();
    private readonly Mock<IShiftRepository> _shifts = new();
    private readonly Mock<IReferenceRepository> _reference = new();

    public WorkOrderP1DisplacementTests()
    {
        _reference.Setup(r => r.GetJobTypeAsync(WorkOrderTestData.BoilerJobTypeId))
            .ReturnsAsync(WorkOrderTestData.BoilerJobType(minutes: 120));
        // Runs the transaction delegate inline, as the real repository would (minus the DB transaction).
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<WorkOrderScheduleDto>>>()))
            .Returns<Func<Task<WorkOrderScheduleDto>>>(work => work());
        _engineers.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        _engineers.Setup(r => r.GetReadOnlyAsync(EngineerId))
            .ReturnsAsync(WorkOrderTestData.Engineer(EngineerId, skillIds: WorkOrderTestData.GasSkill));
        _shifts.Setup(r => r.GetForDayAsync(EngineerId, Day))
            .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(EngineerId, new TimeOnly(8, 0), new TimeOnly(17, 0)) });
        SetBookings();
    }

    private WorkOrderSchedulingService Build() =>
        new(_jobs.Object, _engineers.Object, _shifts.Object, _reference.Object, new EngineerBookingRules(_jobs.Object));

    private Task<WorkOrderScheduleDto> Schedule(DateTime start) =>
        Build().ScheduleAsync(P1JobId, new WorkOrderScheduleRequest(EngineerId, start, "dispatcher1"));

    private Job ArrangeP1(string status = "Allocated")
    {
        var p1 = WorkOrderTestData.Job(status: status, priority: "P1");
        _jobs.Setup(r => r.GetAsync(P1JobId)).ReturnsAsync(p1);
        _jobs.Setup(r => r.GetReadOnlyAsync(P1JobId)).ReturnsAsync(WorkOrderTestData.Snapshot(p1));
        return p1;
    }

    // The overlap and day reads. Tests pass copies here when the locked read should see a later change.
    private void SetBookings(params Job[] bookings) =>
        _jobs.Setup(r => r.GetActiveBookingsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns<IEnumerable<int>, DateTime, DateTime>((ids, from, to) =>
            {
                var engineerIds = ids.ToHashSet();
                return Task.FromResult(bookings
                    .Where(j => j.EngineerId.HasValue && engineerIds.Contains(j.EngineerId.Value)
                        && j.ScheduledStart < to && j.ScheduledEnd > from)
                    .ToList());
            });

    private static DateTime At(int hour, int minute = 0) => WorkOrderTestData.At(hour, minute);

    // ---- Re-check under the lock ----

    [Fact]
    public async Task P1_DisplacedJobStartedAfterOverlapRead_Throws409AndSavesNothing()
    {
        var p1 = ArrangeP1();
        var p3 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(9), At(11));
        SetBookings(WorkOrderTestData.Snapshot(p3)); // the overlap read sees J-11 as not yet started
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(p3);
        p3.ActualStart = At(9, 5); // it starts before the row lock is granted

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("J-11 cannot be displaced by this P1: it has started or is on site (status Scheduled). Retry.", ex.Message);
        AssertNothingSaved(p1, p3);
    }

    [Fact]
    public async Task P1_DisplacedJobOnSiteAfterOverlapRead_Throws409AndSavesNothing()
    {
        var p1 = ArrangeP1();
        var p3 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(9), At(11));
        SetBookings(WorkOrderTestData.Snapshot(p3));
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(p3);
        p3.Status = "OnSite";

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("J-11 cannot be displaced by this P1: it has started or is on site (status OnSite). Retry.", ex.Message);
        AssertNothingSaved(p1, p3);
    }

    [Fact]
    public async Task P1_DisplacedJobBecameP1AfterOverlapRead_Throws409AndSavesNothing()
    {
        var p1 = ArrangeP1();
        var p3 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(9), At(11));
        SetBookings(WorkOrderTestData.Snapshot(p3));
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(p3);
        p3.Priority = "P1";

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("J-11 cannot be displaced by this P1: it has become a P1, and a P1 cannot displace another P1. Retry.", ex.Message);
        AssertNothingSaved(p1, p3);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    [InlineData("NotCompleted")]
    public async Task P1_DisplacedJobFinalisedAfterOverlapRead_Throws409AndSavesNothing(string status)
    {
        var p1 = ArrangeP1();
        var p3 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(9), At(11));
        SetBookings(WorkOrderTestData.Snapshot(p3));
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(p3);
        p3.Status = status;

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal($"J-11 cannot be displaced by this P1: it is now {status}. Retry.", ex.Message);
        AssertNothingSaved(p1, p3);
    }

    [Fact]
    public async Task P1_DisplacedJobMovedAfterOverlapRead_Throws409AndSavesNothing()
    {
        var p1 = ArrangeP1();
        var p3 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(9), At(11));
        SetBookings(WorkOrderTestData.Snapshot(p3));
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(p3);
        // Moved to 14:00 to 16:00, clear of the P1 window 10:00 to 12:00.
        p3.ScheduledStart = At(14);
        p3.ScheduledEnd = At(16);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("J-11 cannot be displaced by this P1: it was moved while the P1 was being booked. Retry.", ex.Message);
        AssertNothingSaved(p1, p3);
    }

    [Fact]
    public async Task P1_SecondDisplacedJobFailsRecheck_ThrowsAndNeverSavesTheP1()
    {
        // J-11 passes its re-check and is moved first, in Id order. J-12 has started, so the whole P1 fails.
        var p1 = ArrangeP1();
        var j11 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(9), At(10, 30));
        var j12 = WorkOrderTestData.Booking(12, "J-12", EngineerId, At(10), At(11));
        SetBookings(WorkOrderTestData.Snapshot(j11), WorkOrderTestData.Snapshot(j12));
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(j11);
        _jobs.Setup(r => r.GetAsync(12)).ReturnsAsync(j12);
        j12.ActualStart = At(10, 2);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("J-12 cannot be displaced by this P1: it has started or is on site (status Scheduled). Retry.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n => n.JobId == 12)), Times.Never);
    }

    [Fact]
    public async Task P1_DisplacedJobRemovedAfterOverlapRead_Throws409AndSavesNothing()
    {
        var p1 = ArrangeP1();
        var p3 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(9), At(11));
        SetBookings(WorkOrderTestData.Snapshot(p3));
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync((Job?)null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("J-11 was removed while the P1 was being booked. Retry.", ex.Message);
        Assert.Equal("Allocated", p1.Status);
        Assert.Equal("Scheduled", p3.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    // The P1 is untouched, the displaced job still holds its booking (the test may have changed its status or times), and nothing was written.
    private void AssertNothingSaved(Job p1, Job displaced)
    {
        Assert.Equal("Allocated", p1.Status);
        Assert.Null(p1.EngineerId);
        Assert.Null(p1.ScheduledStart);
        Assert.NotNull(displaced.ScheduledStart);
        Assert.NotNull(displaced.ScheduledEnd);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    // ---- Lock order ----

    [Fact]
    public async Task P1_LocksEngineerThenP1ThenDisplacedJobsInAscendingIdThenRereads()
    {
        var trace = new List<string>();
        _engineers.Setup(r => r.LockForUpdateAsync(EngineerId)).Callback(() => trace.Add($"lock-engineer:{EngineerId}")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Callback<int>(id => trace.Add($"lock-job:{id}")).Returns(Task.CompletedTask);
        var p1 = ArrangeP1();
        _jobs.Setup(r => r.GetAsync(P1JobId)).Callback(() => trace.Add($"read-job:{P1JobId}")).ReturnsAsync(p1);
        // Listed out of Id order on purpose: the locks must still go 11 then 12.
        var j12 = WorkOrderTestData.Booking(12, "J-12", EngineerId, At(9, 30), At(10, 30));
        var j11 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(10, 30), At(11, 30));
        SetBookings(j12, j11);
        _jobs.Setup(r => r.GetAsync(11)).Callback(() => trace.Add("read-job:11")).ReturnsAsync(j11);
        _jobs.Setup(r => r.GetAsync(12)).Callback(() => trace.Add("read-job:12")).ReturnsAsync(j12);

        var result = await Schedule(At(10));

        Assert.Equal(
            new[] { "lock-engineer:2", "lock-job:10", "read-job:10", "lock-job:11", "lock-job:12", "read-job:11", "read-job:12" },
            trace);
        Assert.Equal(new[] { 11, 12 }, result.Displaced.Select(d => d.JobId));
    }

    // ---- Day capacity ----

    [Fact]
    public async Task P1_SwapOnAFullDay_SucceedsAndDisplacesBothBookings()
    {
        // Shift 08:00 to 12:00 is 240 rostered minutes, and two 120-minute P3 jobs fill it.
        // A P1 at 09:00 (120 minutes) overlaps both, so their minutes do not count against the day.
        _shifts.Setup(r => r.GetForDayAsync(EngineerId, Day))
            .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(EngineerId, new TimeOnly(8, 0), new TimeOnly(12, 0)) });
        var p1 = ArrangeP1();
        var j11 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(8), At(10));
        var j12 = WorkOrderTestData.Booking(12, "J-12", EngineerId, At(10), At(12));
        SetBookings(j11, j12);
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(j11);
        _jobs.Setup(r => r.GetAsync(12)).ReturnsAsync(j12);

        var result = await Schedule(At(9));

        Assert.Equal("Scheduled", result.Status);
        Assert.Equal(At(9), result.Start);
        Assert.Equal(At(11), result.End);
        Assert.Equal(new[]
        {
            new DisplacedJobDto(11, "J-11", "Allocated", EngineerId),
            new DisplacedJobDto(12, "J-12", "Allocated", EngineerId)
        }, result.Displaced);
        Assert.Equal("Scheduled", p1.Status);
        _jobs.Verify(r => r.UpdateAsync(It.Is<Job>(j => j.Id == P1JobId && j.Status == "Scheduled")), Times.Once);
    }

    [Fact]
    public async Task NonP1_OnAFullDay_Throws422RatherThanDisplacing()
    {
        // The same window for a P3 job: its overlaps still count against the day, so the hours check refuses it.
        _shifts.Setup(r => r.GetForDayAsync(EngineerId, Day))
            .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(EngineerId, new TimeOnly(8, 0), new TimeOnly(12, 0)) });
        var p3 = WorkOrderTestData.Job(status: "Allocated", priority: "P3");
        _jobs.Setup(r => r.GetAsync(P1JobId)).ReturnsAsync(p3);
        _jobs.Setup(r => r.GetReadOnlyAsync(P1JobId)).ReturnsAsync(WorkOrderTestData.Snapshot(p3));
        var j11 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(8), At(10));
        var j12 = WorkOrderTestData.Booking(12, "J-12", EngineerId, At(10), At(12));
        SetBookings(j11, j12);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(At(9)));

        Assert.Equal("The booking would exceed the engineer's rostered hours for that day.", ex.Message);
        Assert.Equal("Scheduled", j11.Status);
        Assert.Equal("Scheduled", j12.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task P1_OverAnotherP1OnAFullDay_Throws409NotCapacity()
    {
        // The blocking rule runs before the day check for a P1, so the reason reported is the P1 clash.
        _shifts.Setup(r => r.GetForDayAsync(EngineerId, Day))
            .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(EngineerId, new TimeOnly(8, 0), new TimeOnly(12, 0)) });
        var p1 = ArrangeP1();
        var otherP1 = WorkOrderTestData.Booking(11, "J-11", EngineerId, At(8), At(10), priority: "P1");
        var j12 = WorkOrderTestData.Booking(12, "J-12", EngineerId, At(10), At(12));
        SetBookings(otherP1, j12);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(9)));

        Assert.Equal("A P1 cannot displace J-11: it is another P1 or is already on site.", ex.Message);
        AssertNothingSavedForP1(p1);
    }

    private void AssertNothingSavedForP1(Job p1)
    {
        Assert.Equal("Allocated", p1.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }
}
