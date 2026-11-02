using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Moq;

namespace Corrin.Tests;

// Legacy POST /api/jobs and PUT /api/jobs/{id} (JobService). Repositories are mocked; EngineerBookingRules is the real class.
public class JobServiceBookingTests
{
    private const int JobId = 10;
    private const int EngineerId = 2;
    private static readonly DateTime Start = WorkOrderTestData.At(10);
    private static readonly DateTime End = WorkOrderTestData.At(12);

    private readonly Mock<IJobRepository> _jobs = new();
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<ISiteRepository> _sites = new();
    private readonly Mock<IEngineerRepository> _engineers = new();
    private readonly Mock<IPartRepository> _parts = new();
    private readonly Mock<IReorderService> _reorder = new();
    private readonly Mock<IReferenceRepository> _reference = new();

    public JobServiceBookingTests()
    {
        _sites.Setup(r => r.GetReadOnlyAsync(5)).ReturnsAsync(new Site { Id = 5, CustomerId = 1, RegionId = 1, Name = "Site 5" });
        _reference.Setup(r => r.GetJobTypeAsync(WorkOrderTestData.BoilerJobTypeId)).ReturnsAsync(WorkOrderTestData.BoilerJobType());
        _engineers.Setup(r => r.GetReadOnlyAsync(EngineerId))
            .ReturnsAsync(WorkOrderTestData.Engineer(EngineerId, skillIds: WorkOrderTestData.GasSkill));
        // Runs the transaction delegate inline, as the real repository would (minus the DB transaction).
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<Job>>>())).Returns<Func<Task<Job>>>(work => work());
        _jobs.Setup(r => r.GetActiveBookingsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<Job>());
    }

    private JobService Build() => new(_jobs.Object, _customers.Object, _sites.Object, _engineers.Object, _parts.Object, _reorder.Object,
        _reference.Object, new EngineerBookingRules(_jobs.Object));

    private void Bookings(params Job[] bookings) =>
        _jobs.Setup(r => r.GetActiveBookingsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(bookings.ToList());

    private static JobCreateRequest CreateRequest(int? engineerId, string priority = "P3", DateTime? start = null, DateTime? end = null) =>
        new(1, 5, null, 1, engineerId, priority, start, end, null);

    private static JobUpdateRequest UpdateRequest(string status, int? engineerId, DateTime? start = null, DateTime? end = null,
        string priority = "P3", string? description = null) =>
        new(engineerId, priority, status, start, end, null, null, null, description);

    private static Job Existing(string status = "Requested", int? engineerId = null, DateTime? start = null, DateTime? end = null) => new()
    {
        Id = JobId,
        JobRef = "J-10",
        CustomerId = 1,
        SiteId = 5,
        JobTypeId = WorkOrderTestData.BoilerJobTypeId,
        RegionId = 1,
        Priority = "P3",
        Status = status,
        EngineerId = engineerId,
        ScheduledStart = start,
        ScheduledEnd = end,
        Description = "old"
    };

    // ---- CreateAsync ----

    [Fact]
    public async Task Create_EngineerWithSkillAndBothTimes_IsScheduledInsideTransactionUnderEngineerLock()
    {
        var trace = new List<string>();
        _engineers.Setup(r => r.LockForUpdateAsync(EngineerId)).Callback(() => trace.Add("lock-engineer")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<Job>>>())).Returns<Func<Task<Job>>>(async work =>
        {
            trace.Add("tx-start");
            var result = await work();
            trace.Add("tx-end");
            return result;
        });
        _jobs.Setup(r => r.AddAsync(It.IsAny<Job>())).Callback<Job>(_ => trace.Add("add")).ReturnsAsync((Job j) => j);

        var dto = await Build().CreateAsync(CreateRequest(EngineerId, start: Start, end: End));

        Assert.Equal("Scheduled", dto.Status);
        Assert.Equal(EngineerId, dto.EngineerId);
        Assert.Equal(Start, dto.ScheduledStart);
        Assert.Equal(End, dto.ScheduledEnd);
        Assert.Equal(new[] { "tx-start", "lock-engineer", "add", "tx-end" }, trace);
    }

    [Fact]
    public async Task Create_EngineerWithoutTimes_IsAllocatedAndTakesNoEngineerLock()
    {
        var dto = await Build().CreateAsync(CreateRequest(EngineerId));

        Assert.Equal("Allocated", dto.Status);
        Assert.Equal(EngineerId, dto.EngineerId);
        _engineers.Verify(r => r.LockForUpdateAsync(It.IsAny<int>()), Times.Never);
        _jobs.Verify(r => r.AddAsync(It.Is<Job>(j => j.Status == "Allocated" && j.EngineerId == EngineerId)), Times.Once);
    }

    [Fact]
    public async Task Create_NoEngineer_IsRequested()
    {
        var dto = await Build().CreateAsync(CreateRequest(null));

        Assert.Equal("Requested", dto.Status);
        Assert.Null(dto.EngineerId);
        _jobs.Verify(r => r.AddAsync(It.Is<Job>(j => j.Status == "Requested" && j.EngineerId == null)), Times.Once);
    }

    [Fact]
    public async Task Create_EngineerWithoutRequiredSkill_Throws422AndSavesNothing()
    {
        _engineers.Setup(r => r.GetReadOnlyAsync(EngineerId))
            .ReturnsAsync(WorkOrderTestData.Engineer(EngineerId, skillIds: Array.Empty<int>()));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CreateAsync(CreateRequest(EngineerId, start: Start, end: End)));

        Assert.Equal("The engineer does not hold the skill required for this job type.", ex.Message);
        _jobs.Verify(r => r.AddAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.InTransactionAsync(It.IsAny<Func<Task<Job>>>()), Times.Never);
    }

    [Fact]
    public async Task Create_InactiveEngineerWithoutTimes_Throws422AndSavesNothing()
    {
        _engineers.Setup(r => r.GetReadOnlyAsync(EngineerId))
            .ReturnsAsync(WorkOrderTestData.Engineer(EngineerId, active: false, skillIds: WorkOrderTestData.GasSkill));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CreateAsync(CreateRequest(EngineerId)));

        Assert.Equal("An inactive engineer cannot be allocated.", ex.Message);
        _jobs.Verify(r => r.AddAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Create_InactiveEngineerWithBothTimes_Throws422WithScheduledWording()
    {
        _engineers.Setup(r => r.GetReadOnlyAsync(EngineerId))
            .ReturnsAsync(WorkOrderTestData.Engineer(EngineerId, active: false, skillIds: WorkOrderTestData.GasSkill));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CreateAsync(CreateRequest(EngineerId, start: Start, end: End)));

        Assert.Equal("An inactive engineer cannot be scheduled.", ex.Message);
        _jobs.Verify(r => r.AddAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Create_NonP1OverlappingBooking_Throws409AndSavesNothing()
    {
        Bookings(WorkOrderTestData.Booking(11, "J-11", EngineerId, WorkOrderTestData.At(9), WorkOrderTestData.At(11)));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Build().CreateAsync(CreateRequest(EngineerId, start: Start, end: End)));

        Assert.Equal("The engineer is already booked at that time by J-11.", ex.Message);
        _jobs.Verify(r => r.AddAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Create_P1OverlappingBooking_Throws409PointingToScheduleEndpointAndSavesNothing()
    {
        Bookings(WorkOrderTestData.Booking(11, "J-11", EngineerId, WorkOrderTestData.At(9), WorkOrderTestData.At(11)));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().CreateAsync(CreateRequest(EngineerId, priority: "P1", start: Start, end: End)));

        Assert.Equal("A P1 cannot be booked over J-11 through this endpoint. "
            + "Create the job without a booking, then use POST /api/work-orders/{id}/schedule.", ex.Message);
        _jobs.Verify(r => r.AddAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Create_AllocatedWithoutTimes_IsNotOverlapChecked()
    {
        // Only a booked job (both times) is checked for overlap. An allocation carries no window.
        Bookings(WorkOrderTestData.Booking(11, "J-11", EngineerId, WorkOrderTestData.At(9), WorkOrderTestData.At(11)));

        var dto = await Build().CreateAsync(CreateRequest(EngineerId));

        Assert.Equal("Allocated", dto.Status);
        _jobs.Verify(r => r.GetActiveBookingsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
    }

    // ---- UpdateAsync ----

    [Fact]
    public async Task Update_OnlyDescriptionChanged_SucceedsEvenWithLegacyOverlap()
    {
        // The row is an old double booking: J-11 overlaps the same engineer and times. An unrelated edit must not be blocked.
        var job = Existing("Scheduled", EngineerId, Start, End);
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        Bookings(WorkOrderTestData.Booking(11, "J-11", EngineerId, Start, End));

        var dto = await Build().UpdateAsync(JobId, UpdateRequest("Scheduled", EngineerId, Start, End, description: "new note"));

        Assert.Equal("new note", dto.Description);
        Assert.Equal("Scheduled", dto.Status);
        Assert.Equal(EngineerId, dto.EngineerId);
        _jobs.Verify(r => r.UpdateAsync(It.Is<Job>(j => j.Id == JobId && j.Description == "new note")), Times.Once);
        _jobs.Verify(r => r.GetActiveBookingsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        _reference.Verify(r => r.GetJobTypeAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Update_ChangedTimesOverlappingAnotherBooking_Throws409AndSavesNothing()
    {
        var job = Existing("Scheduled", EngineerId, WorkOrderTestData.At(14), WorkOrderTestData.At(16));
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        Bookings(WorkOrderTestData.Booking(11, "J-11", EngineerId, Start, End));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().UpdateAsync(JobId, UpdateRequest("Scheduled", EngineerId, Start, End)));

        Assert.Equal("The engineer is already booked at that time by J-11.", ex.Message);
        Assert.Equal(WorkOrderTestData.At(14), job.ScheduledStart);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_P1OverlappingBooking_Throws409PointingToScheduleEndpointAndSavesNothing()
    {
        var job = Existing("Allocated", EngineerId);
        job.Priority = "P1";
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        Bookings(WorkOrderTestData.Booking(11, "J-11", EngineerId, Start, End));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().UpdateAsync(JobId, UpdateRequest("Scheduled", EngineerId, Start, End, priority: "P1")));

        Assert.Equal("A P1 cannot be booked over J-11 through this endpoint. "
            + "Use POST /api/work-orders/10/schedule, which can displace a non-P1 booking.", ex.Message);
        Assert.Equal("Allocated", job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_EngineerWithoutRequiredSkill_Throws422AndSavesNothing()
    {
        var job = Existing("Requested");
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        _engineers.Setup(r => r.GetReadOnlyAsync(3))
            .ReturnsAsync(WorkOrderTestData.Engineer(3, skillIds: Array.Empty<int>()));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Build().UpdateAsync(JobId, UpdateRequest("Allocated", 3)));

        Assert.Equal("The engineer does not hold the skill required for this job type.", ex.Message);
        Assert.Equal("Requested", job.Status);
        Assert.Null(job.EngineerId);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_EngineerInactive_Throws422AndSavesNothing()
    {
        var job = Existing("Requested");
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        _engineers.Setup(r => r.GetReadOnlyAsync(EngineerId))
            .ReturnsAsync(WorkOrderTestData.Engineer(EngineerId, active: false, skillIds: WorkOrderTestData.GasSkill));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Build().UpdateAsync(JobId, UpdateRequest("Allocated", EngineerId)));

        Assert.Equal("An inactive engineer cannot be allocated.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_AllocatedWithoutEngineer_Throws422()
    {
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(Existing("Requested"));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Build().UpdateAsync(JobId, UpdateRequest("Allocated", null)));

        Assert.Equal("A work order with status Allocated needs an engineer.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_ScheduledWithoutBothTimes_Throws422WithWorkOrderWording()
    {
        // OnSite is not tested here: moving to OnSite is refused with 409 before this rule (see the 409 tests below).
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(Existing("Allocated", EngineerId));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Build().UpdateAsync(JobId, UpdateRequest("Scheduled", EngineerId, Start, null)));

        Assert.Equal("A work order with status Scheduled needs both a scheduled start and end.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_JobWithVisitsAndStatusChangeToAllocated_Throws409AndSavesNothing()
    {
        // Allocated is not one of the statuses the new guard refuses, so the has-visits rule is what blocks this change.
        var job = Existing("Scheduled", EngineerId, Start, End);
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        _jobs.Setup(r => r.HasVisitsAsync(JobId)).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().UpdateAsync(JobId, UpdateRequest("Allocated", EngineerId, Start, End)));

        Assert.Equal("This job has visits. Change its status, engineer or times through the work-order endpoints "
            + "(allocate, schedule, visits).", ex.Message);
        Assert.Equal("Scheduled", job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_JobWithVisitsAndEngineerChange_Throws409()
    {
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(Existing("OnSite", EngineerId, Start, End));
        _jobs.Setup(r => r.HasVisitsAsync(JobId)).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().UpdateAsync(JobId, UpdateRequest("OnSite", 3, Start, End)));

        Assert.StartsWith("This job has visits.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_JobWithVisitsAndTimesChange_Throws409()
    {
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(Existing("OnSite", EngineerId, Start, End));
        _jobs.Setup(r => r.HasVisitsAsync(JobId)).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().UpdateAsync(JobId, UpdateRequest("OnSite", EngineerId, Start, End.AddHours(1))));

        Assert.StartsWith("This job has visits.", ex.Message);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_JobWithVisitsAndUnchangedBookingAndNewDescription_Succeeds()
    {
        var job = Existing("OnSite", EngineerId, Start, End);
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        _jobs.Setup(r => r.HasVisitsAsync(JobId)).ReturnsAsync(true);

        var dto = await Build().UpdateAsync(JobId, UpdateRequest("OnSite", EngineerId, Start, End, description: "new"));

        Assert.Equal("new", dto.Description);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Once);
    }

    [Fact]
    public async Task Update_LocksEngineerThenJobBeforeReadingJob()
    {
        var trace = new List<string>();
        _engineers.Setup(r => r.LockForUpdateAsync(EngineerId)).Callback(() => trace.Add("lock-engineer")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.LockForUpdateAsync(JobId)).Callback(() => trace.Add("lock-job")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.GetAsync(JobId)).Callback(() => trace.Add("read-job")).ReturnsAsync(Existing("Requested"));

        await Build().UpdateAsync(JobId, UpdateRequest("Allocated", EngineerId));

        Assert.Equal(new[] { "lock-engineer", "lock-job", "read-job" }, trace);
    }

    [Fact]
    public async Task Update_UnknownJob_ThrowsNotFound()
    {
        _jobs.Setup(r => r.GetAsync(404)).ReturnsAsync((Job?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Build().UpdateAsync(404, UpdateRequest("Requested", null)));

        Assert.Equal("Job 404 was not found.", ex.Message);
    }

    // ---- UpdateAsync: OnSite, Completed and NotCompleted are reached only through the visit endpoints ----

    [Theory]
    [InlineData("OnSite")]
    [InlineData("Completed")]
    [InlineData("NotCompleted")]
    public async Task Update_MoveToVisitOnlyStatusOnJobWithNoVisits_Throws409AndSavesNothing(string status)
    {
        var job = Existing("Scheduled", EngineerId, Start, End);
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        _jobs.Setup(r => r.HasVisitsAsync(JobId)).ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().UpdateAsync(JobId, UpdateRequest(status, EngineerId, Start, End)));

        Assert.Equal($"A work order cannot be moved to {status} through PUT /api/jobs/{JobId}. "
            + $"Use POST /api/work-orders/{JobId}/visits/start and POST /api/work-orders/{JobId}/visits/complete.", ex.Message);
        Assert.Equal("Scheduled", job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task Update_CompletedJobDescriptionEditWithSameStatus_Succeeds()
    {
        var job = Existing("Completed", EngineerId, Start, End);
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);

        var dto = await Build().UpdateAsync(JobId, UpdateRequest("Completed", EngineerId, Start, End, description: "corrected note"));

        Assert.Equal("Completed", dto.Status);
        Assert.Equal("corrected note", dto.Description);
        _jobs.Verify(r => r.UpdateAsync(It.Is<Job>(j => j.Id == JobId && j.Status == "Completed" && j.Description == "corrected note")),
            Times.Once);
    }

    [Fact]
    public async Task Update_ScheduledToCancelledThroughPut_Succeeds()
    {
        // Cancelled is not a visit-owned status, so the guard does not apply to it.
        var job = Existing("Scheduled", EngineerId, Start, End);
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);

        var dto = await Build().UpdateAsync(JobId, UpdateRequest("Cancelled", EngineerId, Start, End));

        Assert.Equal("Cancelled", dto.Status);
        _jobs.Verify(r => r.UpdateAsync(It.Is<Job>(j => j.Id == JobId && j.Status == "Cancelled")), Times.Once);
    }

    [Fact]
    public async Task Cancel_ScheduledJob_SetsCancelledAndAddsReasonNote()
    {
        var job = Existing("Scheduled", EngineerId, Start, End);
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);

        var dto = await Build().CancelAsync(JobId, new JobCancelRequest("customer moved house"));

        Assert.Equal("Cancelled", dto.Status);
        _jobs.Verify(r => r.UpdateAsync(It.Is<Job>(j => j.Id == JobId && j.Status == "Cancelled")), Times.Once);
        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n => n.JobId == JobId && n.Note == "Cancelled: customer moved house")), Times.Once);
    }

    // ---- UpdateAsync: legacy FirstTimeFix for a job with no visit rows ----

    [Theory]
    [InlineData("Fixed", true)]
    [InlineData("NoAccess", false)]
    public async Task Update_LegacyJobWithNoVisits_FirstTimeFixFollowsOutcome(string outcome, bool expected)
    {
        var job = Existing("Requested");
        job.FirstTimeFix = null;
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        _jobs.Setup(r => r.HasVisitsAsync(JobId)).ReturnsAsync(false);

        // Status stays Requested and no booking changes, so only the outcome decides the flag.
        var dto = await Build().UpdateAsync(JobId, new JobUpdateRequest(null, "P3", "Requested", null, null, null, null, outcome, null));

        Assert.Equal(expected, dto.FirstTimeFix);
        Assert.Equal(outcome, dto.Outcome);
        _jobs.Verify(r => r.UpdateAsync(It.Is<Job>(j => j.FirstTimeFix == expected)), Times.Once);
    }
}
