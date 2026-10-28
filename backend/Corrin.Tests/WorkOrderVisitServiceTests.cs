using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Corrin.Api.Validators;
using Moq;

namespace Corrin.Tests;

public class WorkOrderVisitServiceTests
{
    private const int JobId = 10;

    private readonly Mock<IJobRepository> _jobs = new();
    private readonly Mock<IVisitRepository> _visits = new();
    private readonly Mock<IJobService> _jobService = new();

    public WorkOrderVisitServiceTests()
    {
        // Runs the transaction delegate inline, as the real repository would (minus the DB transaction).
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<VisitDto>>>()))
            .Returns<Func<Task<VisitDto>>>(work => work());
        _jobs.Setup(r => r.UpdateAsync(It.IsAny<Job>())).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.AddNoteAsync(It.IsAny<JobNote>()))
            .Returns<JobNote>(n => Task.FromResult(n));
        _visits.Setup(r => r.GetForJobAsync(JobId)).ReturnsAsync(new List<Visit>());
        _visits.Setup(r => r.AddAsync(It.IsAny<Visit>())).Returns<Visit>(v => Task.FromResult(v));
        _jobService.Setup(s => s.AddPartAsync(It.IsAny<int>(), It.IsAny<JobPartRequest>()))
            .Returns<int, JobPartRequest>((id, p) => Task.FromResult(new JobPartDto(1, id, p.PartId, p.Quantity)));
    }

    private WorkOrderVisitService Build() => new(_jobs.Object, _visits.Object, _jobService.Object);

    private static DateTime At(int hour, int minute = 0) => WorkOrderTestData.At(hour, minute);

    // Booked 09:00 to 11:00 with engineer 2, status Scheduled.
    private static Job ScheduledJob()
    {
        var job = WorkOrderTestData.Job("Scheduled");
        job.EngineerId = 2;
        job.ScheduledStart = At(9);
        job.ScheduledEnd = At(11);
        return job;
    }

    private static Job OnSiteJob()
    {
        var job = ScheduledJob();
        job.Status = "OnSite";
        job.ActualStart = At(9, 5);
        return job;
    }

    // A visit that has started and not yet ended.
    private static Visit OpenVisit(int visitNo = 1) => new()
    {
        Id = visitNo,
        JobId = JobId,
        VisitNo = visitNo,
        EngineerId = 2,
        ScheduledStart = At(9),
        ScheduledEnd = At(11),
        ActualStart = At(9, 5),
        CreatedAt = At(8)
    };

    private void Arrange(Job job, params Visit[] visits)
    {
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
        _visits.Setup(r => r.GetForJobAsync(JobId)).ReturnsAsync(visits.ToList());
    }

    private static VisitCompleteRequest Complete(string outcome, IReadOnlyList<JobPartRequest>? parts = null, string? notes = null) =>
        new(outcome, "engineer1", notes, parts);

    // ---- StartAsync ----

    [Fact]
    public async Task StartAsync_ScheduledJobWithNoVisits_CreatesVisitOneAndSetsJobOnSite()
    {
        var job = ScheduledJob();
        Arrange(job);

        var dto = await Build().StartAsync(JobId);

        Assert.Equal(1, dto.VisitNo);
        Assert.Equal(JobId, dto.JobId);
        Assert.Equal(2, dto.EngineerId);
        Assert.Equal(At(9), dto.ScheduledStart);
        Assert.Equal(At(11), dto.ScheduledEnd);
        Assert.NotNull(dto.ActualStart);
        Assert.Null(dto.ActualEnd);
        Assert.Null(dto.Outcome);
        Assert.Equal("OnSite", job.Status);
        Assert.Equal(dto.ActualStart, job.ActualStart);
        _visits.Verify(r => r.AddAsync(It.Is<Visit>(v => v.JobId == JobId && v.VisitNo == 1)), Times.Once);
        _jobs.Verify(r => r.UpdateAsync(job), Times.Once);
    }

    [Fact]
    public async Task StartAsync_FollowUpVisitAlreadyBooked_ReusesItAndCreatesNoNewVisit()
    {
        var job = ScheduledJob();
        var first = OpenVisit(1);
        first.ActualEnd = At(9, 30);
        first.Outcome = "NoAccess";
        var followUp = new Visit { Id = 2, JobId = JobId, VisitNo = 2, CreatedAt = At(9, 30) };
        Arrange(job, first, followUp);

        var dto = await Build().StartAsync(JobId);

        Assert.Equal(2, dto.VisitNo);
        Assert.NotNull(followUp.ActualStart);
        Assert.Equal("OnSite", job.Status);
        _visits.Verify(r => r.AddAsync(It.IsAny<Visit>()), Times.Never);
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Triaged")]
    [InlineData("Allocated")]
    [InlineData("OnSite")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task StartAsync_JobNotScheduled_ThrowsBusinessRuleAndChangesNothing(string status)
    {
        var job = ScheduledJob();
        job.Status = status;
        Arrange(job);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Build().StartAsync(JobId));

        Assert.Equal(status, job.Status);
        _visits.Verify(r => r.AddAsync(It.IsAny<Visit>()), Times.Never);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_LatestVisitAlreadyStarted_ThrowsBusinessRuleAndChangesNothing()
    {
        var job = ScheduledJob();
        Arrange(job, OpenVisit(1));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Build().StartAsync(JobId));

        Assert.Equal("Scheduled", job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_UnknownJob_ThrowsNotFound()
    {
        _jobs.Setup(r => r.GetAsync(404)).ReturnsAsync((Job?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Build().StartAsync(404));

        _visits.Verify(r => r.AddAsync(It.IsAny<Visit>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_LocksJobBeforeReadingStatus()
    {
        var job = ScheduledJob();
        var trace = new List<string>();
        _jobs.Setup(r => r.LockForUpdateAsync(JobId)).Callback(() => trace.Add("lock")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.GetAsync(JobId)).Callback(() => trace.Add("read")).ReturnsAsync(job);

        await Build().StartAsync(JobId);

        Assert.Equal(new[] { "lock", "read" }, trace);
    }

    [Fact]
    public async Task StartAsync_StatusChangedWhileWaitingForLock_ThrowsBusinessRuleAndChangesNothing()
    {
        // The job was Scheduled when this call began. A cancellation committed while this call waited for the row lock.
        var job = ScheduledJob();
        Arrange(job);
        _jobs.Setup(r => r.LockForUpdateAsync(JobId)).Callback(() => job.Status = "Cancelled").Returns(Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Build().StartAsync(JobId));

        Assert.Equal("A work order with status Cancelled cannot be started.", ex.Message);
        Assert.Equal("Cancelled", job.Status);
        Assert.Null(job.ActualStart);
        _visits.Verify(r => r.AddAsync(It.IsAny<Visit>()), Times.Never);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    // ---- CompleteAsync: outcomes and status ----

    [Fact]
    public async Task CompleteAsync_FixedOnVisitOne_CompletesJobAndMarksFirstTimeFix()
    {
        var job = OnSiteJob();
        Arrange(job, OpenVisit(1));

        var dto = await Build().CompleteAsync(JobId, Complete("Fixed"));

        Assert.Equal(1, dto.VisitNo);
        Assert.Equal("Fixed", dto.Outcome);
        Assert.NotNull(dto.ActualEnd);
        Assert.Equal("Completed", job.Status);
        Assert.Equal("Fixed", job.Outcome);
        Assert.True(job.FirstTimeFix);
        Assert.Equal(dto.ActualEnd, job.ActualEnd);
        _visits.Verify(r => r.AddAsync(It.IsAny<Visit>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n =>
            n.JobId == JobId && n.Author == "engineer1" && n.Note == "Visit 1 outcome: Fixed.")), Times.Once);
        _jobs.Verify(r => r.UpdateAsync(job), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_FixedOnVisitTwoAfterNoAccess_CompletesJobButIsNotFirstTimeFix()
    {
        var job = OnSiteJob();
        var first = OpenVisit(1);
        first.ActualEnd = At(9, 30);
        first.Outcome = "NoAccess";
        Arrange(job, first, OpenVisit(2));

        var dto = await Build().CompleteAsync(JobId, Complete("Fixed"));

        Assert.Equal(2, dto.VisitNo);
        Assert.Equal("Fixed", dto.Outcome);
        Assert.Equal("Completed", job.Status);
        Assert.Equal("Fixed", job.Outcome);
        Assert.False(job.FirstTimeFix);
        _visits.Verify(r => r.AddAsync(It.IsAny<Visit>()), Times.Never);
    }

    [Theory]
    [InlineData("PartsNeeded")]
    [InlineData("NoAccess")]
    [InlineData("FollowUpRequired")]
    public async Task CompleteAsync_NotFixedOutcome_ReopensJobAndCreatesUnscheduledFollowUpVisit(string outcome)
    {
        var job = OnSiteJob();
        Arrange(job, OpenVisit(1));

        var dto = await Build().CompleteAsync(JobId, Complete(outcome));

        Assert.Equal(1, dto.VisitNo);
        Assert.Equal(outcome, dto.Outcome);
        Assert.NotNull(dto.ActualEnd);
        Assert.Equal("Triaged", job.Status);
        Assert.Equal(outcome, job.Outcome);
        Assert.False(job.FirstTimeFix);
        Assert.Null(job.ScheduledStart);
        Assert.Null(job.ScheduledEnd);
        _visits.Verify(r => r.AddAsync(It.Is<Visit>(v =>
            v.JobId == JobId && v.VisitNo == 2 && v.ActualStart == null && v.ActualEnd == null
            && v.ScheduledStart == null && v.CreatedAt != default)), Times.Once);
        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n =>
            n.JobId == JobId && n.Author == "engineer1"
            && n.Note == $"Visit 1 outcome: {outcome}. Follow-up visit 2 needs booking.")), Times.Once);
    }

    [Theory]
    [InlineData("  Customer away until Monday  ", "Visit 1 outcome: NoAccess. Customer away until Monday. Follow-up visit 2 needs booking.")]
    [InlineData("Customer away until Monday.", "Visit 1 outcome: NoAccess. Customer away until Monday. Follow-up visit 2 needs booking.")]
    [InlineData("Gate is locked!", "Visit 1 outcome: NoAccess. Gate is locked! Follow-up visit 2 needs booking.")]
    [InlineData("Is anyone in?", "Visit 1 outcome: NoAccess. Is anyone in? Follow-up visit 2 needs booking.")]
    [InlineData("   ", "Visit 1 outcome: NoAccess. Follow-up visit 2 needs booking.")]
    public async Task CompleteAsync_NotFixedNotes_TrimmedAndFullStopAddedOnlyWhenMissingPunctuation(string notes, string expectedNote)
    {
        var job = OnSiteJob();
        Arrange(job, OpenVisit(1));

        await Build().CompleteAsync(JobId, Complete("NoAccess", notes: notes));

        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n => n.JobId == JobId && n.Note == expectedNote)), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_FixedWithNotes_NoteGetsFullStop()
    {
        var job = OnSiteJob();
        Arrange(job, OpenVisit(1));

        await Build().CompleteAsync(JobId, Complete("Fixed", notes: "Replaced valve"));

        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n => n.Note == "Visit 1 outcome: Fixed. Replaced valve.")), Times.Once);
    }

    [Theory]
    [InlineData("PartsNeeded")]
    [InlineData("NoAccess")]
    [InlineData("FollowUpRequired")]
    public async Task CompleteAsync_NotFixed_ClearsJobActualTimesButVisitKeepsItsOwn(string outcome)
    {
        var job = OnSiteJob(); // job.ActualStart is 09:05
        var visit = OpenVisit(1);
        visit.ActualStart = At(9, 7);
        Arrange(job, visit);

        var dto = await Build().CompleteAsync(JobId, Complete(outcome));

        Assert.Null(job.ActualStart);
        Assert.Null(job.ActualEnd);
        Assert.Equal(At(9, 7), visit.ActualStart);
        Assert.NotNull(visit.ActualEnd);
        Assert.Equal(visit.ActualEnd, dto.ActualEnd);
    }

    [Fact]
    public async Task CompleteAsync_Fixed_KeepsJobActualStartAndSetsActualEnd()
    {
        var job = OnSiteJob(); // job.ActualStart is 09:05
        var visit = OpenVisit(1);
        visit.ActualStart = At(9, 7);
        Arrange(job, visit);

        var dto = await Build().CompleteAsync(JobId, Complete("Fixed"));

        Assert.Equal(At(9, 5), job.ActualStart);
        Assert.NotNull(job.ActualEnd);
        Assert.Equal(dto.ActualEnd, job.ActualEnd);
    }

    [Fact]
    public async Task CompleteAsync_LocksJobBeforeReadingStatus()
    {
        var job = OnSiteJob();
        Arrange(job, OpenVisit(1));
        var trace = new List<string>();
        _jobs.Setup(r => r.LockForUpdateAsync(JobId)).Callback(() => trace.Add("lock")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.GetAsync(JobId)).Callback(() => trace.Add("read")).ReturnsAsync(job);

        await Build().CompleteAsync(JobId, Complete("Fixed"));

        Assert.Equal(new[] { "lock", "read" }, trace);
    }

    [Fact]
    public async Task CompleteAsync_StatusChangedWhileWaitingForLock_ThrowsBusinessRuleAndChangesNothing()
    {
        // The job was OnSite when this call began. Another completion committed while this call waited for the row lock.
        var job = OnSiteJob();
        var visit = OpenVisit(1);
        Arrange(job, visit);
        _jobs.Setup(r => r.LockForUpdateAsync(JobId)).Callback(() => job.Status = "Completed").Returns(Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CompleteAsync(JobId, Complete("NoAccess")));

        Assert.Equal("A work order with status Completed cannot be completed; it must be on site.", ex.Message);
        Assert.Equal("Completed", job.Status);
        Assert.Null(visit.ActualEnd);
        Assert.Null(visit.Outcome);
        _visits.Verify(r => r.AddAsync(It.IsAny<Visit>()), Times.Never);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    [Theory]
    [InlineData("Scheduled")]
    [InlineData("Triaged")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task CompleteAsync_JobNotOnSite_ThrowsBusinessRuleAndChangesNothing(string status)
    {
        var job = OnSiteJob();
        job.Status = status;
        var visit = OpenVisit(1);
        Arrange(job, visit);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CompleteAsync(JobId, Complete("Fixed")));

        Assert.Equal(status, job.Status);
        Assert.Null(visit.ActualEnd);
        Assert.Null(visit.Outcome);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_NoVisitRecorded_ThrowsBusinessRuleAndChangesNothing()
    {
        var job = OnSiteJob();
        Arrange(job);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CompleteAsync(JobId, Complete("Fixed")));

        Assert.Equal("OnSite", job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_VisitNotStarted_ThrowsBusinessRuleAndChangesNothing()
    {
        var job = OnSiteJob();
        var visit = OpenVisit(1);
        visit.ActualStart = null;
        Arrange(job, visit);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CompleteAsync(JobId, Complete("Fixed")));

        Assert.Null(visit.Outcome);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_VisitAlreadyEnded_ThrowsBusinessRuleAndChangesNothing()
    {
        var job = OnSiteJob();
        var visit = OpenVisit(1);
        visit.ActualEnd = At(10);
        visit.Outcome = "Fixed";
        Arrange(job, visit);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CompleteAsync(JobId, Complete("NoAccess")));

        Assert.Equal("Fixed", visit.Outcome);
        Assert.Equal("OnSite", job.Status);
        _visits.Verify(r => r.AddAsync(It.IsAny<Visit>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_UnknownJob_ThrowsNotFound()
    {
        _jobs.Setup(r => r.GetAsync(404)).ReturnsAsync((Job?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Build().CompleteAsync(404, Complete("Fixed")));
    }

    [Fact]
    public async Task CompleteAsync_OutcomeNotInVocabulary_ThrowsBusinessRuleBeforeReadingJob()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CompleteAsync(JobId, Complete("Done")));

        _jobs.Verify(r => r.GetAsync(It.IsAny<int>()), Times.Never);
        _jobService.Verify(s => s.AddPartAsync(It.IsAny<int>(), It.IsAny<JobPartRequest>()), Times.Never);
    }

    // ---- CompleteAsync: parts ----

    [Fact]
    public async Task CompleteAsync_WithParts_RecordsEachPartBeforeVisitAndJobAreSaved()
    {
        var job = OnSiteJob();
        Arrange(job, OpenVisit(1));
        var trace = new List<string>();
        _jobService.Setup(s => s.AddPartAsync(JobId, It.IsAny<JobPartRequest>()))
            .Callback<int, JobPartRequest>((_, p) => trace.Add($"part:{p.PartId}x{p.Quantity}"))
            .Returns<int, JobPartRequest>((id, p) => Task.FromResult(new JobPartDto(1, id, p.PartId, p.Quantity)));
        _jobs.Setup(r => r.UpdateAsync(It.IsAny<Job>()))
            .Callback<Job>(_ => trace.Add("job-saved"))
            .Returns(Task.CompletedTask);

        await Build().CompleteAsync(JobId, Complete("Fixed", new[] { new JobPartRequest(7, 2), new JobPartRequest(9, 1) }));

        Assert.Equal(new[] { "part:7x2", "part:9x1", "job-saved" }, trace);
    }

    [Fact]
    public async Task CompleteAsync_NoParts_DoesNotCallAddPart()
    {
        var job = OnSiteJob();
        Arrange(job, OpenVisit(1));

        await Build().CompleteAsync(JobId, Complete("Fixed", parts: null));

        _jobService.Verify(s => s.AddPartAsync(It.IsAny<int>(), It.IsAny<JobPartRequest>()), Times.Never);
        Assert.Equal("Completed", job.Status);
    }

    [Fact]
    public async Task CompleteAsync_InsufficientStock_ThrowsConflictAndLeavesVisitOpen()
    {
        // Real JobService, so the stock rule itself is exercised. Stock holds 2 of part 7; the visit asks for 3.
        var job = OnSiteJob();
        var visit = OpenVisit(1);
        Arrange(job, visit);
        var parts = new Mock<IPartRepository>();
        parts.Setup(r => r.GetAsync(7)).ReturnsAsync(new Part { Id = 7, Sku = "PRT-7", StockQty = 2 });
        parts.Setup(r => r.TryDecrementStockAsync(7, 3)).ReturnsAsync(0);
        parts.Setup(r => r.GetStockLevelAsync(7)).ReturnsAsync(((int StockQty, int ReorderLevel)?)(2, 0));
        _jobs.Setup(r => r.GetReadOnlyAsync(JobId)).ReturnsAsync(job);
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<JobPart>>>()))
            .Returns<Func<Task<JobPart>>>(work => work());
        var realJobService = new JobService(_jobs.Object, new Mock<ICustomerRepository>().Object,
            new Mock<ISiteRepository>().Object, new Mock<IEngineerRepository>().Object, parts.Object, new Mock<IReorderService>().Object,
            new Mock<IReferenceRepository>().Object, new EngineerBookingRules(_jobs.Object));
        var service = new WorkOrderVisitService(_jobs.Object, _visits.Object, realJobService);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CompleteAsync(JobId, Complete("Fixed", new[] { new JobPartRequest(7, 3) })));

        Assert.Equal("Only 2 of PRT-7 in stock.", ex.Message);
        Assert.Null(visit.ActualEnd);
        Assert.Null(visit.Outcome);
        Assert.Equal("OnSite", job.Status);
        Assert.Null(job.Outcome);
        _jobs.Verify(r => r.AddPartAsync(It.IsAny<JobPart>()), Times.Never);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
        _visits.Verify(r => r.AddAsync(It.IsAny<Visit>()), Times.Never);
    }

    // ---- ListAsync ----

    [Fact]
    public async Task ListAsync_UnknownJob_ThrowsNotFound()
    {
        _jobs.Setup(r => r.GetReadOnlyAsync(404)).ReturnsAsync((Job?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Build().ListAsync(404));
    }

    [Fact]
    public async Task ListAsync_JobWithNoVisits_ReturnsEmptyList()
    {
        _jobs.Setup(r => r.GetReadOnlyAsync(JobId)).ReturnsAsync(ScheduledJob());
        _visits.Setup(r => r.ListForJobAsync(JobId)).ReturnsAsync(new List<Visit>());

        var result = await Build().ListAsync(JobId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListAsync_TwoVisits_ReturnsThemInVisitOrderWithTheirOutcomes()
    {
        var first = OpenVisit(1);
        first.ActualEnd = At(9, 30);
        first.Outcome = "NoAccess";
        _jobs.Setup(r => r.GetReadOnlyAsync(JobId)).ReturnsAsync(ScheduledJob());
        // Visit 2 is a follow-up that has not been booked to start yet.
        var followUp = new Visit { Id = 2, JobId = JobId, VisitNo = 2, CreatedAt = At(9, 30) };
        _jobs.Setup(r => r.GetReadOnlyAsync(JobId)).ReturnsAsync(ScheduledJob());
        _visits.Setup(r => r.ListForJobAsync(JobId)).ReturnsAsync(new List<Visit> { first, followUp });

        var result = await Build().ListAsync(JobId);

        Assert.Equal(new[] { 1, 2 }, result.Select(v => v.VisitNo));
        Assert.Equal("NoAccess", result[0].Outcome);
        Assert.Equal(At(9, 30), result[0].ActualEnd);
        Assert.Null(result[1].Outcome);
        Assert.Null(result[1].ActualStart);
    }
}

// JobService.UpdateAsync sets FirstTimeFix from the Outcome only for legacy jobs with no visit rows.
// A job with visit rows gets FirstTimeFix from the visit service, so a generic update leaves the flag alone.
public class JobServiceFirstTimeFixBehaviourTests
{
    private static (Job Job, Mock<IJobRepository> Jobs, JobService Service) Arrange(bool hasVisits)
    {
        var job = WorkOrderTestData.Job("OnSite");
        var jobs = new Mock<IJobRepository>();
        jobs.Setup(r => r.GetAsync(job.Id)).ReturnsAsync(job);
        jobs.Setup(r => r.HasVisitsAsync(job.Id)).ReturnsAsync(hasVisits);
        jobs.Setup(r => r.UpdateAsync(It.IsAny<Job>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<Job>>>())).Returns<Func<Task<Job>>>(work => work());
        var service = new JobService(jobs.Object, new Mock<ICustomerRepository>().Object,
            new Mock<ISiteRepository>().Object, new Mock<IEngineerRepository>().Object, new Mock<IPartRepository>().Object,
            new Mock<IReorderService>().Object, new Mock<IReferenceRepository>().Object, new EngineerBookingRules(jobs.Object));
        return (job, jobs, service);
    }

    private static JobUpdateRequest Request(string? outcome, string status = "Completed") =>
        new(null, "P3", status, null, null, null, null, outcome, null);

    [Theory]
    [InlineData("Fixed", true)]
    [InlineData("NoAccess", false)]
    [InlineData("PartsNeeded", false)]
    [InlineData(null, null)]
    public async Task UpdateAsync_NoVisits_FirstTimeFixFollowsOutcome(string? outcome, bool? expected)
    {
        var (job, jobs, service) = Arrange(hasVisits: false);
        job.FirstTimeFix = true; // A stale value that the outcome must overwrite.

        // Status stays OnSite: a PUT that moves a job to Completed is now a 409, and this test is about FirstTimeFix only.
        await service.UpdateAsync(job.Id, Request(outcome, status: "OnSite"));

        Assert.Equal(expected, job.FirstTimeFix);
        jobs.Verify(r => r.HasVisitsAsync(job.Id), Times.Once);
    }

    [Theory]
    [InlineData("Fixed", false)]
    [InlineData("Fixed", null)]
    [InlineData("NoAccess", true)]
    [InlineData("PartsNeeded", false)]
    [InlineData(null, true)]
    public async Task UpdateAsync_WithVisits_FirstTimeFixUnchangedWhateverTheOutcome(string? outcome, bool? existing)
    {
        var (job, jobs, service) = Arrange(hasVisits: true);
        job.FirstTimeFix = existing;

        // Status stays OnSite: a status change on a job with visits is now a 409, and this test is about FirstTimeFix only.
        await service.UpdateAsync(job.Id, Request(outcome, status: "OnSite"));

        Assert.Equal(existing, job.FirstTimeFix);
        Assert.Equal(outcome, job.Outcome);
        jobs.Verify(r => r.HasVisitsAsync(job.Id), Times.Once);
        jobs.Verify(r => r.UpdateAsync(job), Times.Once);
    }
}

public class VisitCompleteRequestValidatorTests
{
    private static readonly VisitCompleteRequestValidator Validator = new();

    private static VisitCompleteRequest Request(string outcome = "Fixed", IReadOnlyList<JobPartRequest>? parts = null) =>
        new(outcome, "engineer1", null, parts);

    [Theory]
    [InlineData("Fixed")]
    [InlineData("PartsNeeded")]
    [InlineData("NoAccess")]
    [InlineData("FollowUpRequired")]
    public void Validate_AllowedOutcome_IsValid(string outcome)
    {
        Assert.True(Validator.Validate(Request(outcome)).IsValid);
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("Done")]
    [InlineData("")]
    public void Validate_OutcomeNotInVocabulary_IsRejected(string outcome)
    {
        var result = Validator.Validate(Request(outcome));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Outcome");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void Validate_PartQuantityOutsideOneToFifty_IsRejected(int quantity)
    {
        var result = Validator.Validate(Request(parts: new[] { new JobPartRequest(7, quantity) }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Parts[0].Quantity");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void Validate_PartQuantityAtBoundary_IsValid(int quantity)
    {
        Assert.True(Validator.Validate(Request(parts: new[] { new JobPartRequest(7, quantity) })).IsValid);
    }

    [Fact]
    public void Validate_SecondPartWithZeroQuantity_IsRejectedWithItsIndex()
    {
        var result = Validator.Validate(Request(parts: new[] { new JobPartRequest(7, 1), new JobPartRequest(9, 0) }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Parts[1].Quantity");
        Assert.DoesNotContain(result.Errors, e => e.PropertyName == "Parts[0].Quantity");
    }

    [Fact]
    public void Validate_PartIdZero_IsRejected()
    {
        var result = Validator.Validate(Request(parts: new[] { new JobPartRequest(0, 1) }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Parts[0].PartId");
    }

    [Fact]
    public void Validate_NullPartsList_IsValid()
    {
        Assert.True(Validator.Validate(Request(parts: null)).IsValid);
    }

    [Fact]
    public void Validate_EmptyUserName_IsRejected()
    {
        var result = Validator.Validate(new VisitCompleteRequest("Fixed", "", null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "UserName");
    }

    [Fact]
    public void Validate_NotesOver1000Characters_IsRejected()
    {
        var result = Validator.Validate(new VisitCompleteRequest("Fixed", "engineer1", new string('x', 1001), null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Notes");
    }
}
