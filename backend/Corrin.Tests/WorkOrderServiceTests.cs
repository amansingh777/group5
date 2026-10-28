using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Moq;

namespace Corrin.Tests;

internal static class WorkOrderTestData
{
    public const int GasSkill = 1;
    public const int BoilerJobTypeId = 1;
    public static readonly DateOnly Day = new(2030, 1, 7);

    public static DateTime At(int hour, int minute = 0) => Day.ToDateTime(new TimeOnly(hour, minute));

    public static JobType BoilerJobType(int minutes = 120, int skillId = GasSkill) => new()
    {
        Id = BoilerJobTypeId,
        Code = "BOILER_SERVICE",
        Name = "Boiler service",
        DefaultMinutes = minutes,
        RequiredSkillId = skillId
    };

    public static Engineer Engineer(int id, int regionId = 1, bool active = true, params int[] skillIds) => new()
    {
        Id = id,
        FullName = $"Engineer {id}",
        RegionId = regionId,
        Active = active,
        Skills = skillIds.Select(s => new EngineerSkill { EngineerId = id, SkillId = s }).ToList()
    };

    public static Shift Shift(int engineerId, TimeOnly start, TimeOnly end, int breakMinutes = 0) => new()
    {
        EngineerId = engineerId,
        ShiftDate = Day,
        StartTime = start,
        EndTime = end,
        BreakMinutes = breakMinutes,
        ShiftType = "Standard"
    };

    public static Job Job(string status = "Requested", string priority = "P3", int regionId = 1) => new()
    {
        Id = 10,
        JobRef = "J-10",
        CustomerId = 1,
        SiteId = 5,
        JobTypeId = BoilerJobTypeId,
        RegionId = regionId,
        Priority = priority,
        Status = status
    };

    // A booking already held by an engineer, as the repository would return it.
    public static Job Booking(int id, string jobRef, int engineerId, DateTime start, DateTime end,
        string priority = "P3", string status = "Scheduled") => new()
    {
        Id = id,
        JobRef = jobRef,
        EngineerId = engineerId,
        JobTypeId = BoilerJobTypeId,
        RegionId = 1,
        Priority = priority,
        Status = status,
        ScheduledStart = start,
        ScheduledEnd = end
    };

    // A copy of the job as an untracked read would return it. Lets a preview read differ from the locked read.
    public static Job Snapshot(Job j) => new()
    {
        Id = j.Id,
        JobRef = j.JobRef,
        CustomerId = j.CustomerId,
        SiteId = j.SiteId,
        JobTypeId = j.JobTypeId,
        EngineerId = j.EngineerId,
        RegionId = j.RegionId,
        Priority = j.Priority,
        Status = j.Status,
        ScheduledStart = j.ScheduledStart,
        ScheduledEnd = j.ScheduledEnd,
        ActualStart = j.ActualStart,
        ActualEnd = j.ActualEnd
    };
}

public class WorkOrderAllocationServiceTests
{
    private readonly Mock<IJobRepository> _jobs = new();
    private readonly Mock<IEngineerRepository> _engineers = new();
    private readonly Mock<IShiftRepository> _shifts = new();
    private readonly Mock<ISiteRepository> _sites = new();
    private readonly Mock<IReferenceRepository> _reference = new();

    private static readonly DateOnly Day = WorkOrderTestData.Day;

    public WorkOrderAllocationServiceTests()
    {
        _reference.Setup(r => r.GetJobTypeAsync(WorkOrderTestData.BoilerJobTypeId))
            .ReturnsAsync(WorkOrderTestData.BoilerJobType());
        // Runs the transaction delegate inline, as the real repository would (minus the DB transaction).
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<WorkOrderAllocationDto>>>()))
            .Returns<Func<Task<WorkOrderAllocationDto>>>(work => work());
        _jobs.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
    }

    private WorkOrderAllocationService Build() =>
        new(_jobs.Object, _engineers.Object, _shifts.Object, _sites.Object, _reference.Object, new EngineerBookingRules(_jobs.Object));

    private void Arrange(Job job, Engineer engineer, params Shift[] shifts)
    {
        _jobs.Setup(r => r.GetAsync(job.Id)).ReturnsAsync(job);
        _engineers.Setup(r => r.GetReadOnlyAsync(engineer.Id)).ReturnsAsync(engineer);
        _shifts.Setup(r => r.GetForDayAsync(engineer.Id, Day)).ReturnsAsync(shifts.ToList());
    }

    [Fact]
    public async Task Allocate_EngineerWithoutRequiredSkill_ThrowsBusinessRuleAndLeavesJobUnchanged()
    {
        var job = WorkOrderTestData.Job();
        Arrange(job, WorkOrderTestData.Engineer(2, regionId: 1, skillIds: Array.Empty<int>()),
            WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Build().AllocateAsync(10, new WorkOrderAllocateRequest(2, "dispatcher1", Day)));

        Assert.Equal("The engineer does not hold the skill required for this job type.", ex.Message);
        Assert.Equal("Requested", job.Status);
        Assert.Null(job.EngineerId);
        // The row lock is taken inside the transaction before validation, so the transaction is opened; nothing is written.
        _jobs.Verify(r => r.LockForUpdateAsync(10), Times.Once);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    [Fact]
    public async Task Allocate_InactiveEngineer_ThrowsBusinessRuleAndLeavesJobUnchanged()
    {
        var job = WorkOrderTestData.Job();
        Arrange(job, WorkOrderTestData.Engineer(2, active: false, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Build().AllocateAsync(10, new WorkOrderAllocateRequest(2, "dispatcher1", Day)));

        Assert.Equal("An inactive engineer cannot be allocated.", ex.Message);
        Assert.Equal("Requested", job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Allocate_DifferentRegionAndNoShift_SucceedsWithBothWarnings()
    {
        var job = WorkOrderTestData.Job(regionId: 1);
        Arrange(job, WorkOrderTestData.Engineer(2, regionId: 3, skillIds: WorkOrderTestData.GasSkill));

        var result = await Build().AllocateAsync(10, new WorkOrderAllocateRequest(2, "dispatcher1", Day));

        Assert.Equal(10, result.JobId);
        Assert.Equal("Allocated", result.Status);
        Assert.Equal(2, result.EngineerId);
        Assert.Equal(
            new[] { "Engineer is in a different region from the work order.", "Engineer has no shift on 2030-01-07." },
            result.Warnings);
        Assert.Equal("Allocated", job.Status);
        Assert.Equal(2, job.EngineerId);
        _jobs.Verify(r => r.UpdateAsync(It.Is<Job>(j => j.Status == "Allocated" && j.EngineerId == 2)), Times.Once);
    }

    [Fact]
    public async Task Allocate_SameRegionWithShift_ReturnsNoWarnings()
    {
        Arrange(WorkOrderTestData.Job(regionId: 1), WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        var result = await Build().AllocateAsync(10, new WorkOrderAllocateRequest(2, "dispatcher1", Day));

        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Triaged")]
    public async Task Allocate_OpenStatus_Succeeds(string status)
    {
        var job = WorkOrderTestData.Job(status: status);
        Arrange(job, WorkOrderTestData.Engineer(2, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        var result = await Build().AllocateAsync(10, new WorkOrderAllocateRequest(2, "dispatcher1", Day));

        Assert.Equal("Allocated", result.Status);
        Assert.Equal("Allocated", job.Status);
    }

    [Theory]
    [InlineData("Allocated")]
    [InlineData("Scheduled")]
    [InlineData("OnSite")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task Allocate_ClosedOrAdvancedStatus_ThrowsConflictAndLeavesJobUnchanged(string status)
    {
        var job = WorkOrderTestData.Job(status: status);
        Arrange(job, WorkOrderTestData.Engineer(2, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().AllocateAsync(10, new WorkOrderAllocateRequest(2, "dispatcher1", Day)));

        Assert.Equal($"A work order with status {status} cannot be allocated.", ex.Message);
        Assert.Equal(status, job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    [Fact]
    public async Task Allocate_Success_WritesAllocationNoteWithUserName()
    {
        var job = WorkOrderTestData.Job();
        Arrange(job, WorkOrderTestData.Engineer(2, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)));

        await Build().AllocateAsync(10, new WorkOrderAllocateRequest(2, "dispatcher1", Day));

        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n =>
            n.JobId == 10 && n.Author == "dispatcher1" && n.Note == "Allocated to Engineer 2.")), Times.Once);
    }

    [Fact]
    public async Task Allocate_UnknownJob_ThrowsNotFound()
    {
        _jobs.Setup(r => r.GetAsync(404)).ReturnsAsync((Job?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            Build().AllocateAsync(404, new WorkOrderAllocateRequest(2, "dispatcher1", Day)));

        Assert.Equal("Job 404 was not found.", ex.Message);
    }

    [Fact]
    public async Task Allocate_UnknownEngineer_ThrowsNotFound()
    {
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(WorkOrderTestData.Job());
        _engineers.Setup(r => r.GetReadOnlyAsync(99)).ReturnsAsync((Engineer?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            Build().AllocateAsync(10, new WorkOrderAllocateRequest(99, "dispatcher1", Day)));

        Assert.Equal("Engineer 99 was not found.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Allocate_LocksJobBeforeReadingStatus()
    {
        var job = WorkOrderTestData.Job();
        Arrange(job, WorkOrderTestData.Engineer(2, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)));
        var trace = new List<string>();
        _jobs.Setup(r => r.LockForUpdateAsync(10)).Callback(() => trace.Add("lock")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.GetAsync(10)).Callback(() => trace.Add("read")).ReturnsAsync(job);

        await Build().AllocateAsync(10, new WorkOrderAllocateRequest(2, "dispatcher1", Day));

        Assert.Equal(new[] { "lock", "read" }, trace);
    }

    [Fact]
    public async Task Allocate_StatusChangedWhileWaitingForLock_ThrowsConflictAndWritesNothing()
    {
        // Another dispatcher scheduled the job and committed while this call was waiting for the row lock.
        var job = WorkOrderTestData.Job(status: "Requested");
        Arrange(job, WorkOrderTestData.Engineer(2, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)));
        _jobs.Setup(r => r.LockForUpdateAsync(10)).Callback(() => job.Status = "Scheduled").Returns(Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().AllocateAsync(10, new WorkOrderAllocateRequest(2, "dispatcher1", Day)));

        Assert.Equal("A work order with status Scheduled cannot be allocated.", ex.Message);
        Assert.Equal("Scheduled", job.Status);
        Assert.Null(job.EngineerId);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    private void ArrangeCandidates(int jobRegion = 1, string jobPostcode = "LS6 1AA")
    {
        _jobs.Setup(r => r.GetReadOnlyAsync(10)).ReturnsAsync(WorkOrderTestData.Job(regionId: jobRegion));
        _sites.Setup(r => r.GetReadOnlyAsync(5)).ReturnsAsync(new Site { Id = 5, RegionId = jobRegion, Postcode = jobPostcode });
        _jobs.Setup(r => r.CountScheduledByEngineerAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<(int EngineerId, int Count)>());
        _jobs.Setup(r => r.GetEngineerSitePostcodesAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<(int EngineerId, string Postcode)>());
        _shifts.Setup(r => r.GetBetweenAsync(Day, Day, It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Shift>());
    }

    [Fact]
    public async Task GetCandidates_QueriesOnlyActiveEngineersWithRequiredSkill()
    {
        ArrangeCandidates();
        _engineers.Setup(r => r.ListAsync(null, WorkOrderTestData.GasSkill, true))
            .ReturnsAsync(new List<Engineer>());

        await Build().GetCandidatesAsync(10, Day);

        _engineers.Verify(r => r.ListAsync(null, WorkOrderTestData.GasSkill, true), Times.Once);
    }

    [Fact]
    public async Task GetCandidates_NoEngineersInPool_ReturnsEmptyList()
    {
        ArrangeCandidates();
        _engineers.Setup(r => r.ListAsync(null, WorkOrderTestData.GasSkill, true))
            .ReturnsAsync(new List<Engineer>());

        var result = await Build().GetCandidatesAsync(10, Day);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCandidates_RanksSameRegionFirstThenFewerJobs()
    {
        ArrangeCandidates(jobRegion: 1);
        _engineers.Setup(r => r.ListAsync(null, WorkOrderTestData.GasSkill, true)).ReturnsAsync(new List<Engineer>
        {
            WorkOrderTestData.Engineer(1, regionId: 2, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Engineer(3, regionId: 1, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Engineer(4, regionId: 1, skillIds: WorkOrderTestData.GasSkill)
        });
        _jobs.Setup(r => r.CountScheduledByEngineerAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<(int EngineerId, int Count)> { (2, 3), (3, 1), (4, 2) });

        var result = await Build().GetCandidatesAsync(10, Day);

        // Same region first (3 with 1 job, 4 with 2, 2 with 3), then the different-region engineer 1 with no jobs.
        Assert.Equal(new[] { 3, 4, 2, 1 }, result.Select(c => c.EngineerId).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Select(c => c.Rank).ToArray());
        Assert.Equal("same region; 1 job(s) that day", result[0].RankReason);
        Assert.Equal("different region; 0 job(s) that day", result[3].RankReason);
    }

    [Fact]
    public async Task GetCandidates_SameOutwardCodeRanksAheadWithinRegion()
    {
        ArrangeCandidates(jobRegion: 1, jobPostcode: "LS6 1AA");
        _engineers.Setup(r => r.ListAsync(null, WorkOrderTestData.GasSkill, true)).ReturnsAsync(new List<Engineer>
        {
            WorkOrderTestData.Engineer(7, regionId: 1, skillIds: WorkOrderTestData.GasSkill),
            WorkOrderTestData.Engineer(8, regionId: 1, skillIds: WorkOrderTestData.GasSkill)
        });
        _jobs.Setup(r => r.GetEngineerSitePostcodesAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<(int EngineerId, string Postcode)> { (8, "LS6 9ZZ") });

        var result = await Build().GetCandidatesAsync(10, Day);

        Assert.Equal(new[] { 8, 7 }, result.Select(c => c.EngineerId).ToArray());
        Assert.Equal("same region; has worked in outward code LS6; 0 job(s) that day", result[0].RankReason);
    }

    [Fact]
    public async Task GetCandidates_UnknownJob_ThrowsNotFound()
    {
        _jobs.Setup(r => r.GetReadOnlyAsync(404)).ReturnsAsync((Job?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Build().GetCandidatesAsync(404, Day));
    }
}

public class WorkOrderSchedulingServiceTests
{
    private readonly Mock<IJobRepository> _jobs = new();
    private readonly Mock<IEngineerRepository> _engineers = new();
    private readonly Mock<IShiftRepository> _shifts = new();
    private readonly Mock<IReferenceRepository> _reference = new();

    private static readonly DateOnly Day = WorkOrderTestData.Day;
    private static DateTime At(int hour, int minute = 0) => WorkOrderTestData.At(hour, minute);

    public WorkOrderSchedulingServiceTests()
    {
        _reference.Setup(r => r.GetJobTypeAsync(WorkOrderTestData.BoilerJobTypeId))
            .ReturnsAsync(WorkOrderTestData.BoilerJobType(minutes: 120));
        // Runs the transaction delegate inline, as the real repository would (minus the DB transaction).
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<WorkOrderScheduleDto>>>()))
            .Returns<Func<Task<WorkOrderScheduleDto>>>(work => work());
        _engineers.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        _engineers.Setup(r => r.GetReadOnlyAsync(2))
            .ReturnsAsync(WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill));
        // Default: one 08:00 to 17:00 shift with no break. Tests override where needed.
        _shifts.Setup(r => r.GetForDayAsync(2, Day))
            .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)) });
        SetBookings();
    }

    private WorkOrderSchedulingService Build() =>
        new(_jobs.Object, _engineers.Object, _shifts.Object, _reference.Object, new EngineerBookingRules(_jobs.Object));

    private Task<WorkOrderScheduleDto> Schedule(DateTime start, int? engineerId = 2) =>
        Build().ScheduleAsync(10, new WorkOrderScheduleRequest(engineerId, start, "dispatcher1"));

    // The preview read is an untracked copy; the locked read (GetAsync) returns the job itself, so tests can change it while "waiting for the lock".
    private void Arrange(Job job)
    {
        _jobs.Setup(r => r.GetAsync(job.Id)).ReturnsAsync(job);
        _jobs.Setup(r => r.GetReadOnlyAsync(job.Id)).ReturnsAsync(WorkOrderTestData.Snapshot(job));
    }

    // Fake of the repository's overlap contract: jobs for the engineers whose [ScheduledStart, ScheduledEnd)
    // overlaps [from, to). Cancelled and completed filtering happens in SQL and is not modelled here.
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

    [Fact]
    public async Task Schedule_NonP1OverlappingBooking_ThrowsConflictAndSavesNothing()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        SetBookings(WorkOrderTestData.Booking(11, "J-11", 2, At(9), At(11)));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("The engineer is already booked at that time by J-11.", ex.Message);
        Assert.Equal("Allocated", job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_BackToBackAfterExistingBooking_Succeeds()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        SetBookings(WorkOrderTestData.Booking(11, "J-11", 2, At(9), At(11)));

        var result = await Schedule(At(11));

        Assert.Equal(At(11), result.Start);
        Assert.Equal(At(13), result.End);
        Assert.Empty(result.Displaced);
        Assert.Equal("Scheduled", job.Status);
        Assert.Equal(At(11), job.ScheduledStart);
        Assert.Equal(At(13), job.ScheduledEnd);
    }

    [Fact]
    public async Task Schedule_BackToBackBeforeExistingBooking_Succeeds()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        SetBookings(WorkOrderTestData.Booking(11, "J-11", 2, At(11), At(13)));

        var result = await Schedule(At(9));

        Assert.Equal(At(9), result.Start);
        Assert.Equal(At(11), result.End);
        Assert.Empty(result.Displaced);
    }

    [Fact]
    public async Task Schedule_WindowOutsideShift_ThrowsBusinessRuleAndSavesNothing()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(At(16)));

        Assert.Equal("The window is not inside one of the engineer's shifts on 2030-01-07, outside its break.", ex.Message);
        Assert.Equal("Allocated", job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_WindowEndingExactlyAtShiftEnd_Succeeds()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);

        var result = await Schedule(At(15));

        Assert.Equal(At(15), result.Start);
        Assert.Equal(At(17), result.End);
    }

    [Fact]
    public async Task Schedule_WindowCrossingMidnight_ThrowsBusinessRule()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(At(23)));

        Assert.Equal("A booking cannot run past midnight.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_ExceedsRosteredHoursForDay_ThrowsBusinessRule()
    {
        // Shift 08:00 to 13:00 with an hour's break (10:00 to 11:00) is 4 rostered hours, 240 minutes.
        // 150 minutes already booked plus a 120-minute job is 270. The window 11:00 to 13:00 is outside the break,
        // so only the hours check fails. (A 08:00 to 12:00 shift with this break cannot hold a 2-hour window at all.)
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        _shifts.Setup(r => r.GetForDayAsync(2, Day))
            .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(13, 0), breakMinutes: 60) });
        SetBookings(WorkOrderTestData.Booking(11, "J-11", 2, At(8), At(10, 30)));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(At(11)));

        Assert.Equal("The booking would exceed the engineer's rostered hours for that day.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_P1OverlappingP3_MovesP3AndReturnsItAsDisplaced()
    {
        var p1 = WorkOrderTestData.Job(status: "Allocated", priority: "P1");
        Arrange(p1);
        var p3 = WorkOrderTestData.Booking(11, "J-11", 2, At(9), At(11));
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(p3);
        SetBookings(p3);

        var result = await Schedule(At(10));

        Assert.Equal(10, result.JobId);
        Assert.Equal("Scheduled", result.Status);
        Assert.Equal(2, result.EngineerId);
        Assert.Equal(At(10), result.Start);
        Assert.Equal(At(12), result.End);
        var displaced = Assert.Single(result.Displaced);
        Assert.Equal(new DisplacedJobDto(11, "J-11", "Allocated", 2), displaced);

        Assert.Null(p3.ScheduledStart);
        Assert.Null(p3.ScheduledEnd);
        Assert.Equal("Allocated", p3.Status);
        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n =>
            n.JobId == 11
            && n.Author == "dispatcher1"
            && n.Note.StartsWith("Displaced by P1 J-10 on ")
            && n.Note.EndsWith(" UTC by dispatcher1"))), Times.Once);
    }

    [Fact]
    public async Task Schedule_P1OverP1_ThrowsConflictAndMovesNothing()
    {
        var p1 = WorkOrderTestData.Job(status: "Allocated", priority: "P1");
        Arrange(p1);
        var otherP1 = WorkOrderTestData.Booking(11, "J-11", 2, At(9), At(11), priority: "P1");
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(otherP1);
        SetBookings(otherP1);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("A P1 cannot displace J-11: it is another P1 or is already on site.", ex.Message);
        Assert.Equal("Allocated", p1.Status);
        Assert.Equal("Scheduled", otherP1.Status);
        Assert.NotNull(otherP1.ScheduledStart);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_P1OverOnSiteJob_ThrowsConflictAndDoesNotMoveIt()
    {
        var p1 = WorkOrderTestData.Job(status: "Allocated", priority: "P1");
        Arrange(p1);
        var onSite = WorkOrderTestData.Booking(11, "J-11", 2, At(9), At(11), status: "OnSite");
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(onSite);
        SetBookings(onSite);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("A P1 cannot displace J-11: it is another P1 or is already on site.", ex.Message);
        Assert.Equal("OnSite", onSite.Status);
        Assert.NotNull(onSite.ScheduledStart);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_P1OverStartedJob_ThrowsConflictAndDoesNotMoveIt()
    {
        var p1 = WorkOrderTestData.Job(status: "Allocated", priority: "P1");
        Arrange(p1);
        var started = WorkOrderTestData.Booking(11, "J-11", 2, At(9), At(11));
        started.ActualStart = At(9, 5);
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync(started);
        SetBookings(started);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("A P1 cannot displace J-11: it is another P1 or is already on site.", ex.Message);
        Assert.NotNull(started.ScheduledStart);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_EngineerRowLockTakenBeforeBookingsAreRead()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        var trace = new List<string>();
        _engineers.Setup(r => r.LockForUpdateAsync(2)).Callback(() => trace.Add("lock")).Returns(Task.CompletedTask);
        _shifts.Setup(r => r.GetForDayAsync(2, Day)).Callback(() => trace.Add("shifts"))
            .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0)) });
        _jobs.Setup(r => r.GetActiveBookingsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns(() => { trace.Add("bookings"); return Task.FromResult(new List<Job>()); });

        await Schedule(At(10));

        _engineers.Verify(r => r.LockForUpdateAsync(2), Times.Once);
        Assert.Equal("lock", trace.First());
        Assert.True(trace.IndexOf("lock") < trace.IndexOf("bookings"));
        Assert.True(trace.IndexOf("lock") < trace.IndexOf("shifts"));
    }

    [Fact]
    public async Task Schedule_LocksEngineerThenJobBeforeReadingJobStatus()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        var trace = new List<string>();
        _engineers.Setup(r => r.LockForUpdateAsync(2)).Callback(() => trace.Add("lock-engineer")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.LockForUpdateAsync(10)).Callback(() => trace.Add("lock-job")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.GetAsync(10)).Callback(() => trace.Add("read-job")).ReturnsAsync(job);

        await Schedule(At(10));

        Assert.Equal(new[] { "lock-engineer", "lock-job", "read-job" }, trace);
    }

    [Fact]
    public async Task Schedule_StatusChangedWhileWaitingForLock_ThrowsConflictAndWritesNothing()
    {
        // The preview saw Allocated. A cancellation committed while this call waited for the job lock.
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        _jobs.Setup(r => r.LockForUpdateAsync(10)).Callback(() => job.Status = "Cancelled").Returns(Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("A work order with status Cancelled cannot be scheduled.", ex.Message);
        Assert.Equal("Cancelled", job.Status);
        Assert.Null(job.ScheduledStart);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_EngineerReassignedWhileWaitingForLock_ThrowsConflictAndWritesNothing()
    {
        // No engineer in the request, so the preview's engineer (2) is locked. Before the job lock is granted,
        // the job is reallocated to engineer 3. The booking must not go ahead against engineer 2.
        var job = WorkOrderTestData.Job(status: "Allocated");
        job.EngineerId = 2;
        Arrange(job);
        _jobs.Setup(r => r.LockForUpdateAsync(10)).Callback(() => job.EngineerId = 3).Returns(Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10), engineerId: null));

        Assert.Equal("The work order's engineer changed while it was being booked. Retry.", ex.Message);
        Assert.Equal(3, job.EngineerId);
        Assert.Equal("Allocated", job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_P1DisplacedBookingCannotBeLoaded_ThrowsConflictAndSavesNothing()
    {
        // The overlap read found J-11, but it cannot be loaded now. Skipping it would leave it double booked under the P1,
        // so the whole schedule fails and the transaction rolls back.
        var p1 = WorkOrderTestData.Job(status: "Allocated", priority: "P1");
        Arrange(p1);
        var p3 = WorkOrderTestData.Booking(11, "J-11", 2, At(9), At(11));
        SetBookings(p3);
        _jobs.Setup(r => r.GetAsync(11)).ReturnsAsync((Job?)null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("J-11 was removed while the P1 was being booked. Retry.", ex.Message);
        Assert.Equal("Allocated", p1.Status);
        Assert.Null(p1.EngineerId);
        Assert.Equal("Scheduled", p3.Status);
        Assert.Equal(At(9), p3.ScheduledStart);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_WindowOverlappingBreak_ThrowsBusinessRuleAndSavesNothing()
    {
        // Shift 08:00 to 17:00 with an hour's break centred on 12:30, so the break is 12:00 to 13:00.
        // The 120-minute window 11:30 to 13:30 overlaps it.
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        _shifts.Setup(r => r.GetForDayAsync(2, Day))
            .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0), breakMinutes: 60) });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(At(11, 30)));

        Assert.Equal("The window is not inside one of the engineer's shifts on 2030-01-07, outside its break.", ex.Message);
        Assert.Equal("Allocated", job.Status);
        Assert.Null(job.ScheduledStart);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Theory]
    [InlineData(10)] // 10:00 to 12:00 ends exactly where the break starts.
    [InlineData(13)] // 13:00 to 15:00 starts exactly where the break ends.
    public async Task Schedule_WindowAdjacentToBreak_Succeeds(int hour)
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        _shifts.Setup(r => r.GetForDayAsync(2, Day))
            .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0), breakMinutes: 60) });

        var result = await Schedule(At(hour));

        Assert.Equal(At(hour), result.Start);
        Assert.Equal("Scheduled", job.Status);
        Assert.Equal(At(hour), job.ScheduledStart);
    }

    [Fact]
    public async Task Schedule_EngineerWithoutRequiredSkill_ThrowsBusinessRule()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        _engineers.Setup(r => r.GetReadOnlyAsync(3))
            .ReturnsAsync(WorkOrderTestData.Engineer(3, regionId: 1, skillIds: Array.Empty<int>()));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(At(10), engineerId: 3));

        Assert.Equal("The engineer does not hold the skill required for this job type.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Schedule_InactiveEngineer_ThrowsBusinessRule()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);
        _engineers.Setup(r => r.GetReadOnlyAsync(3))
            .ReturnsAsync(WorkOrderTestData.Engineer(3, active: false, skillIds: WorkOrderTestData.GasSkill));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(At(10), engineerId: 3));

        Assert.Equal("An inactive engineer cannot be scheduled.", ex.Message);
    }

    [Fact]
    public async Task Schedule_NoEngineerSuppliedAndNoneOnJob_ThrowsBusinessRule()
    {
        var job = WorkOrderTestData.Job(status: "Allocated");
        Arrange(job);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Schedule(At(10), engineerId: null));

        Assert.Equal("The work order has no engineer. Supply engineerId or allocate first.", ex.Message);
    }

    [Fact]
    public async Task Schedule_JobNotAllocatedOrScheduled_ThrowsConflict()
    {
        var job = WorkOrderTestData.Job(status: "Requested");
        Arrange(job);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Schedule(At(10)));

        Assert.Equal("A work order with status Requested cannot be scheduled.", ex.Message);
    }

    [Fact]
    public async Task Schedule_UnknownJob_ThrowsNotFound()
    {
        _jobs.Setup(r => r.GetAsync(404)).ReturnsAsync((Job?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            Build().ScheduleAsync(404, new WorkOrderScheduleRequest(2, At(10), "dispatcher1")));

        Assert.Equal("Job 404 was not found.", ex.Message);
    }

    // ---- Next free slot ----

    private void ArrangeNextSlot(IEnumerable<Engineer> pool, IEnumerable<Shift> shifts, params Job[] bookings)
    {
        _jobs.Setup(r => r.GetReadOnlyAsync(10)).ReturnsAsync(WorkOrderTestData.Job(status: "Allocated"));
        _engineers.Setup(r => r.ListAsync(null, WorkOrderTestData.GasSkill, true)).ReturnsAsync(pool.ToList());
        _shifts.Setup(r => r.GetBetweenAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(shifts.ToList());
        SetBookings(bookings);
    }

    [Fact]
    public async Task NextSlot_ReturnsEarliestSlotAcrossEngineers()
    {
        ArrangeNextSlot(
            new[]
            {
                WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill),
                WorkOrderTestData.Engineer(3, regionId: 2, skillIds: WorkOrderTestData.GasSkill)
            },
            new[]
            {
                WorkOrderTestData.Shift(2, new TimeOnly(10, 0), new TimeOnly(17, 0)),
                WorkOrderTestData.Shift(3, new TimeOnly(8, 0), new TimeOnly(12, 0))
            });

        var slot = await Build().NextSlotAsync(10, Day);

        Assert.Equal(3, slot.EngineerId);
        Assert.Equal("Engineer 3", slot.EngineerName);
        Assert.Equal(At(8), slot.Start);
        Assert.Equal(At(10), slot.End);
    }

    [Fact]
    public async Task NextSlot_RespectsExistingBookings()
    {
        ArrangeNextSlot(
            new[] { WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill) },
            new[] { WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(12, 0)) },
            WorkOrderTestData.Booking(11, "J-11", 2, At(8), At(10)));

        var slot = await Build().NextSlotAsync(10, Day);

        Assert.Equal(2, slot.EngineerId);
        Assert.Equal(At(10), slot.Start);
        Assert.Equal(At(12), slot.End);
    }

    [Fact]
    public async Task NextSlot_EngineerWhoseRosteredHoursAreFullIsSkipped()
    {
        ArrangeNextSlot(
            new[]
            {
                WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill),
                WorkOrderTestData.Engineer(3, regionId: 1, skillIds: WorkOrderTestData.GasSkill)
            },
            new[]
            {
                WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(10, 0)),
                WorkOrderTestData.Shift(3, new TimeOnly(9, 0), new TimeOnly(17, 0))
            },
            WorkOrderTestData.Booking(11, "J-11", 2, At(8), At(10)));

        var slot = await Build().NextSlotAsync(10, Day);

        Assert.Equal(3, slot.EngineerId);
        Assert.Equal(At(9), slot.Start);
        Assert.Equal(At(11), slot.End);
    }

    [Fact]
    public async Task NextSlot_SameStartTime_PrefersSameRegionEngineer()
    {
        ArrangeNextSlot(
            new[]
            {
                WorkOrderTestData.Engineer(2, regionId: 2, skillIds: WorkOrderTestData.GasSkill),
                WorkOrderTestData.Engineer(3, regionId: 1, skillIds: WorkOrderTestData.GasSkill)
            },
            new[]
            {
                WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(12, 0)),
                WorkOrderTestData.Shift(3, new TimeOnly(8, 0), new TimeOnly(12, 0))
            });

        var slot = await Build().NextSlotAsync(10, Day);

        Assert.Equal(3, slot.EngineerId);
        Assert.Equal(At(8), slot.Start);
    }

    [Fact]
    public async Task NextSlot_NoEngineerWithRequiredSkill_ThrowsNotFound()
    {
        ArrangeNextSlot(Array.Empty<Engineer>(), Array.Empty<Shift>());

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Build().NextSlotAsync(10, Day));

        Assert.Equal("Free slot in the next 14 days for job 10 was not found.", ex.Message);
    }

    [Fact]
    public async Task NextSlot_NoShiftInWindow_ThrowsNotFound()
    {
        ArrangeNextSlot(
            new[] { WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill) },
            Array.Empty<Shift>());

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Build().NextSlotAsync(10, Day));

        Assert.Equal("Free slot in the next 14 days for job 10 was not found.", ex.Message);
    }

    [Fact]
    public async Task NextSlot_EveryShiftFullyBooked_ThrowsNotFound()
    {
        ArrangeNextSlot(
            new[] { WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill) },
            new[] { WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(10, 0)) },
            WorkOrderTestData.Booking(11, "J-11", 2, At(8), At(10)));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Build().NextSlotAsync(10, Day));

        Assert.Equal("Free slot in the next 14 days for job 10 was not found.", ex.Message);
    }

    [Fact]
    public async Task NextSlot_FirstFreeGridSlotFallsInBreak_OffersSlotAfterBreak()
    {
        // Shift 08:00 to 14:00 with an hour's break centred on 11:00, so the break is 10:30 to 11:30.
        // Booked 08:00 to 10:00. The first free 15-minute grid start is 10:00, and 10:00 to 12:00 runs into the break.
        // Every start up to 11:15 also overlaps. The first start that clears the break is 11:30 (11:30 to 13:30).
        ArrangeNextSlot(
            new[] { WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill) },
            new[] { WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(14, 0), breakMinutes: 60) },
            WorkOrderTestData.Booking(11, "J-11", 2, At(8), At(10)));

        var slot = await Build().NextSlotAsync(10, Day);

        Assert.Equal(2, slot.EngineerId);
        Assert.Equal(At(11, 30), slot.Start);
        Assert.Equal(At(13, 30), slot.End);
    }

    [Fact]
    public async Task NextSlot_WindowEndingExactlyAtBreakStart_IsOffered()
    {
        // Shift 08:00 to 14:00, break 10:30 to 11:30. Booked 08:00 to 08:30, so the first free start is 08:30.
        // A 120-minute job from 08:30 ends at 10:30, exactly where the break starts. That touch is allowed.
        ArrangeNextSlot(
            new[] { WorkOrderTestData.Engineer(2, regionId: 1, skillIds: WorkOrderTestData.GasSkill) },
            new[] { WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(14, 0), breakMinutes: 60) },
            WorkOrderTestData.Booking(11, "J-11", 2, At(8), At(8, 30)));

        var slot = await Build().NextSlotAsync(10, Day);

        Assert.Equal(At(8, 30), slot.Start);
        Assert.Equal(At(10, 30), slot.End);
    }

    [Fact]
    public async Task NextSlot_UnknownJob_ThrowsNotFound()
    {
        _jobs.Setup(r => r.GetReadOnlyAsync(404)).ReturnsAsync((Job?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Build().NextSlotAsync(404, Day));
    }
}
