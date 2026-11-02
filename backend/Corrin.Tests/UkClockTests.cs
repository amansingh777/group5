using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Moq;

namespace Corrin.Tests;

// Slots and scheduled starts are UK wall-clock times, so "now" is UK local time (UkClock). These tests fix the clock with
// UkClock.Override on British Summer Time dates and reset it after each test. They all live in this one class, which
// xUnit runs sequentially, so the static override is never shared between two tests that set it.
public class UkClockTests : IDisposable
{
    private static readonly DateOnly BstDay = new(2026, 7, 15);
    private const int EngineerId = 2;
    private const int JobId = 10;

    private readonly Mock<IJobRepository> _jobs = new();
    private readonly Mock<IEngineerRepository> _engineers = new();
    private readonly Mock<IShiftRepository> _shifts = new();
    private readonly Mock<IReferenceRepository> _reference = new();

    public UkClockTests()
    {
        _reference.Setup(r => r.GetJobTypeAsync(WorkOrderTestData.BoilerJobTypeId))
            .ReturnsAsync(WorkOrderTestData.BoilerJobType(minutes: 120));
        var engineer = WorkOrderTestData.Engineer(EngineerId, skillIds: WorkOrderTestData.GasSkill);
        _engineers.Setup(r => r.GetReadOnlyAsync(EngineerId)).ReturnsAsync(engineer);
        _engineers.Setup(r => r.ListAsync(null, WorkOrderTestData.GasSkill, true)).ReturnsAsync(new List<Engineer> { engineer });
        _engineers.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<WorkOrderScheduleDto>>>()))
            .Returns<Func<Task<WorkOrderScheduleDto>>>(work => work());
        _shifts.Setup(r => r.GetForDayAsync(EngineerId, BstDay)).ReturnsAsync(new List<Shift> { BstShift() });
        _shifts.Setup(r => r.GetBetweenAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Shift> { BstShift() });
        _jobs.Setup(r => r.GetActiveBookingsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<Job>());
        UseJob("P3");
    }

    public void Dispose() => UkClock.Override = null;

    private static Shift BstShift() => new()
    {
        EngineerId = EngineerId,
        ShiftDate = BstDay,
        StartTime = new TimeOnly(8, 0),
        EndTime = new TimeOnly(17, 0),
        BreakMinutes = 0,
        ShiftType = "Standard"
    };

    private void UseJob(string priority)
    {
        var job = WorkOrderTestData.Job(status: "Allocated", priority: priority);
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        _jobs.Setup(r => r.GetReadOnlyAsync(JobId)).ReturnsAsync(WorkOrderTestData.Snapshot(job));
    }

    private static void FixUkNow(DateTime ukNow) => UkClock.Override = () => ukNow;

    private static DateTime Bst(int hour, int minute = 0) => BstDay.ToDateTime(new TimeOnly(hour, minute));

    private WorkOrderSchedulingService Build() =>
        new(_jobs.Object, _engineers.Object, _shifts.Object, _reference.Object, new EngineerBookingRules(_jobs.Object));

    private Task<WorkOrderScheduleDto> Schedule(DateTime start) =>
        Build().ScheduleAsync(JobId, new WorkOrderScheduleRequest(EngineerId, start, "dispatcher1"));

    // ---- Conversion ----

    [Fact]
    public void FromUtc_InBritishSummerTime_IsOneHourAheadOfUtc()
    {
        Assert.Equal(new DateTime(2026, 7, 15, 10, 20, 0), UkClock.FromUtc(new DateTime(2026, 7, 15, 9, 20, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void FromUtc_InGreenwichTime_IsTheSameAsUtc()
    {
        Assert.Equal(new DateTime(2026, 1, 15, 9, 20, 0), UkClock.FromUtc(new DateTime(2026, 1, 15, 9, 20, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void FromUtc_AtTheOctoberChangeOver_FallsBackToGmt()
    {
        // BST ends at 01:00 UTC on 2026-10-25, when UK clocks go from 02:00 BST back to 01:00 GMT.
        Assert.Equal(new DateTime(2026, 10, 25, 1, 59, 0), UkClock.FromUtc(new DateTime(2026, 10, 25, 0, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 10, 25, 1, 0, 0), UkClock.FromUtc(new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Now_WithOverride_ReturnsTheOverriddenUkTime()
    {
        FixUkNow(Bst(10, 20));

        Assert.Equal(Bst(10, 20), UkClock.Now());
    }

    // ---- NextSlot ----

    [Fact]
    public async Task NextSlot_BstMorning_NeverOffersAStartBeforeUkNow()
    {
        // UK 10:20 is 09:20 UTC. Rounded up to the grid that is 10:30. Using UTC would wrongly offer 09:30.
        FixUkNow(Bst(10, 20));

        var slot = await Build().NextSlotAsync(JobId, BstDay);

        Assert.Equal(EngineerId, slot.EngineerId);
        Assert.Equal(Bst(10, 30), slot.Start);
        Assert.Equal(Bst(12, 30), slot.End);
    }

    [Fact]
    public async Task NextSlot_BstMorningOnTheGrid_OffersThatExactSlot()
    {
        FixUkNow(Bst(10, 30));

        var slot = await Build().NextSlotAsync(JobId, BstDay);

        Assert.Equal(Bst(10, 30), slot.Start);
    }

    // ---- ScheduleAsync past-start rule ----

    [Fact]
    public async Task Schedule_NonP1StartInThePast_Throws422AndSavesNothing()
    {
        FixUkNow(Bst(10, 20));
        UseJob("P3");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(Bst(9)));

        Assert.Equal("The start 2026-07-15 09:00 is in the past. UK time is 2026-07-15 10:20; choose a later start.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_BstStartBetweenUtcAndUkNow_IsRejected()
    {
        // 09:30 is after UTC now (09:20) but before UK now (10:20). It must be refused.
        FixUkNow(Bst(10, 20));
        UseJob("P3");

        await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(Bst(9, 30)));

        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_NonP1StartExactlyAtUkNow_IsAccepted()
    {
        FixUkNow(Bst(10, 20));
        UseJob("P3");

        var result = await Schedule(Bst(10, 20));

        Assert.Equal(Bst(10, 20), result.Start);
        Assert.Equal(Bst(12, 20), result.End);
        Assert.Equal("Scheduled", result.Status);
    }

    [Fact]
    public async Task Schedule_P1ExactlyFifteenMinutesInThePast_IsAccepted()
    {
        FixUkNow(Bst(10, 20));
        UseJob("P1");

        var result = await Schedule(Bst(10, 5));

        Assert.Equal(Bst(10, 5), result.Start);
        Assert.Equal(Bst(12, 5), result.End);
        Assert.Equal("Scheduled", result.Status);
    }

    [Fact]
    public async Task Schedule_P1MoreThanFifteenMinutesInThePast_Throws422AndSavesNothing()
    {
        FixUkNow(Bst(10, 20));
        UseJob("P1");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(Bst(10)));

        Assert.Equal("The start 2026-07-15 10:00 is more than 15 minutes in the past. UK time is 2026-07-15 10:20.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }
}
