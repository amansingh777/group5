using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Corrin.Api.Validators;
using Moq;

namespace Corrin.Tests;

// Hand-written from spec R2 and R3: the only moves allowed through POST /api/work-orders/{id}/status.
// Every other pair of statuses is refused. Shared by the rule tests and the service matrix.
internal static class StatusMoveMatrix
{
    public static readonly HashSet<(string From, string To)> Allowed = new()
    {
        (WorkOrderStatus.Requested, WorkOrderStatus.Triaged),
        (WorkOrderStatus.Requested, WorkOrderStatus.Cancelled),
        (WorkOrderStatus.Triaged, WorkOrderStatus.Cancelled),
        (WorkOrderStatus.Allocated, WorkOrderStatus.Cancelled),
        (WorkOrderStatus.Scheduled, WorkOrderStatus.Cancelled),
        (WorkOrderStatus.OnSite, WorkOrderStatus.Cancelled),
    };

    public static IEnumerable<object[]> AllPairs()
    {
        foreach (var from in WorkOrderStatus.All)
            foreach (var to in WorkOrderStatus.All)
                yield return new object[] { from, to, Allowed.Contains((from, to)) };
    }
}

public class SlaStateTests
{
    // Window is 24 hours, so 25% of the window is 6 hours. Fixed clock: the helper takes "now" as a parameter.
    private static readonly DateTime Created = new(2030, 1, 1, 0, 0, 0);
    private static readonly DateTime Due = Created.AddHours(24);

    [Theory]
    [InlineData(WorkOrderStatus.Requested)]
    [InlineData(WorkOrderStatus.Triaged)]
    [InlineData(WorkOrderStatus.Allocated)]
    [InlineData(WorkOrderStatus.Scheduled)]
    [InlineData(WorkOrderStatus.OnSite)]
    public void Compute_NullSlaDueAt_ReturnsNone(string status)
    {
        Assert.Equal(SlaState.None, SlaState.Compute(status, Created, null, Due.AddDays(1)));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.NotCompleted)]
    [InlineData(WorkOrderStatus.Cancelled)]
    public void Compute_FinalStatusPastDue_ReturnsNone(string status)
    {
        Assert.Equal(SlaState.None, SlaState.Compute(status, Created, Due, Due.AddDays(1)));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.NotCompleted)]
    [InlineData(WorkOrderStatus.Cancelled)]
    public void Compute_FinalStatusInsideWindow_ReturnsNone(string status)
    {
        Assert.Equal(SlaState.None, SlaState.Compute(status, Created, Due, Created.AddHours(1)));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Requested)]
    [InlineData(WorkOrderStatus.Triaged)]
    [InlineData(WorkOrderStatus.Allocated)]
    [InlineData(WorkOrderStatus.Scheduled)]
    [InlineData(WorkOrderStatus.OnSite)]
    public void Compute_OpenStatusPastDue_ReturnsBreached(string status)
    {
        Assert.Equal(SlaState.Breached, SlaState.Compute(status, Created, Due, Due.AddMinutes(1)));
    }

    [Fact]
    public void Compute_NowExactlyAtDue_IsNotBreached()
    {
        // Breached needs now strictly after the due time. At the due time nothing is left, so the state is amber.
        Assert.Equal(SlaState.Amber, SlaState.Compute(WorkOrderStatus.Scheduled, Created, Due, Due));
    }

    [Fact]
    public void Compute_JustOverQuarterRemaining_ReturnsOk()
    {
        // 6 hours 1 minute left of a 24 hour window: more than 25%.
        Assert.Equal(SlaState.Ok, SlaState.Compute(WorkOrderStatus.Allocated, Created, Due, Due.AddHours(-6).AddMinutes(-1)));
    }

    [Fact]
    public void Compute_ExactlyQuarterRemaining_ReturnsOk()
    {
        // Exactly 6 hours left is exactly 25%. The rule is "less than 25%", so this is not amber.
        Assert.Equal(SlaState.Ok, SlaState.Compute(WorkOrderStatus.Allocated, Created, Due, Due.AddHours(-6)));
    }

    [Fact]
    public void Compute_JustUnderQuarterRemaining_ReturnsAmber()
    {
        // 5 hours 59 minutes left: less than 25%.
        Assert.Equal(SlaState.Amber, SlaState.Compute(WorkOrderStatus.Allocated, Created, Due, Due.AddHours(-5).AddMinutes(-59)));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Requested)]
    [InlineData(WorkOrderStatus.Triaged)]
    [InlineData(WorkOrderStatus.Allocated)]
    [InlineData(WorkOrderStatus.Scheduled)]
    [InlineData(WorkOrderStatus.OnSite)]
    public void Compute_OpenStatusInsideAmberWindow_ReturnsAmber(string status)
    {
        Assert.Equal(SlaState.Amber, SlaState.Compute(status, Created, Due, Due.AddHours(-1)));
    }

    [Fact]
    public void Compute_JustCreated_ReturnsOk()
    {
        Assert.Equal(SlaState.Ok, SlaState.Compute(WorkOrderStatus.Requested, Created, Due, Created));
    }

    [Fact]
    public void Compute_ExactlyThreeQuartersUsed_IsNotAmber_PerLessThanRule()
    {
        // Spec R25 says "less than 25% remains", so 18 of 24 hours used (exactly 25% left) is OK. Spec AC-14 says
        // "75% used is amber", which would need "at most 25% remains". The code follows R25; the gap is reported.
        Assert.Equal(SlaState.Ok, SlaState.Compute(WorkOrderStatus.Requested, Created, Due, Created.AddHours(18)));
        Assert.Equal(SlaState.Amber, SlaState.Compute(WorkOrderStatus.Requested, Created, Due, Created.AddHours(18).AddMinutes(1)));
    }
}

public class WorkOrderStatusRuleTests
{
    [Theory]
    [MemberData(nameof(StatusMoveMatrix.AllPairs), MemberType = typeof(StatusMoveMatrix))]
    public void CanMoveByStatusEndpoint_WholeMatrix_MatchesSpecR2AndR3(string from, string to, bool allowed)
    {
        Assert.Equal(allowed, WorkOrderStatus.CanMoveByStatusEndpoint(from, to));
    }

    [Fact]
    public void CanMoveByStatusEndpoint_UnknownTargetStatus_IsFalse()
    {
        Assert.False(WorkOrderStatus.CanMoveByStatusEndpoint(WorkOrderStatus.Requested, "Raised"));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.NotCompleted)]
    [InlineData(WorkOrderStatus.Cancelled)]
    public void IsFinal_FinalStatuses_AreTrue(string status)
    {
        Assert.True(WorkOrderStatus.IsFinal(status));
    }

    [Theory]
    [InlineData(WorkOrderStatus.Requested)]
    [InlineData(WorkOrderStatus.Triaged)]
    [InlineData(WorkOrderStatus.Allocated)]
    [InlineData(WorkOrderStatus.Scheduled)]
    [InlineData(WorkOrderStatus.OnSite)]
    public void IsFinal_OpenStatuses_AreFalse(string status)
    {
        Assert.False(WorkOrderStatus.IsFinal(status));
    }
}

public class WorkOrderStatusRequestValidatorTests
{
    private readonly WorkOrderStatusRequestValidator _validator = new();

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Validate_EveryKnownStatus_IsAccepted(string status)
    {
        Assert.True(_validator.Validate(new WorkOrderStatusRequest(status, "dispatcher1")).IsValid);
    }

    public static IEnumerable<object[]> AllStatuses() => WorkOrderStatus.All.Select(s => new object[] { s });

    // "Raised" is the spec name; the stored name is "Requested", so the spec name is refused.
    [Theory]
    [InlineData("Raised")]
    [InlineData("Bogus")]
    [InlineData("requested")]
    [InlineData("")]
    public void Validate_UnknownStatus_IsRejected(string status)
    {
        var result = _validator.Validate(new WorkOrderStatusRequest(status, "dispatcher1"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WorkOrderStatusRequest.Status));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MissingUserName_IsRejected(string? userName)
    {
        var result = _validator.Validate(new WorkOrderStatusRequest(WorkOrderStatus.Triaged, userName!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WorkOrderStatusRequest.UserName));
    }

    [Fact]
    public void Validate_UserNameLongerThan80_IsRejected()
    {
        var result = _validator.Validate(new WorkOrderStatusRequest(WorkOrderStatus.Triaged, new string('a', 81)));

        Assert.False(result.IsValid);
    }
}

public class WorkOrderStatusServiceTests
{
    private const int JobId = 10;
    private static readonly DateTime Now = new(2030, 1, 1, 9, 0, 0);

    private readonly Mock<IWorkOrderReadRepository> _reads = new();
    private readonly Mock<IJobRepository> _jobs = new();
    private Job _job = null!;

    public WorkOrderStatusServiceTests()
    {
        // Runs the transaction delegate inline, as the real repository would (minus the DB transaction).
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<bool>>>()))
            .Returns<Func<Task<bool>>>(work => work());
        _jobs.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        _reads.Setup(r => r.NotesForJobsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<JobNote>());
        _reads.Setup(r => r.PartsForJobsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<JobPart>());
    }

    private WorkOrderService Build() => new(_reads.Object, _jobs.Object);

    private Job ArrangeJob(string status)
    {
        _job = new Job
        {
            Id = JobId,
            JobRef = "J-10",
            CustomerId = 1,
            SiteId = 5,
            JobTypeId = 1,
            RegionId = 1,
            Priority = "P3",
            Status = status,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(_job);
        // The read after the change returns the job as it now stands, so the response reflects the new status.
        _reads.Setup(r => r.GetAsync(JobId)).Returns(() => Task.FromResult<WorkOrderRow?>(Row(_job.Status)));
        return _job;
    }

    private static WorkOrderRow Row(string status) => new(JobId, "J-10", 1, "Customer", "Site", "LS6 1AA", null, null, null,
        "Boiler service", "Gas Safe", "P3", status, null, null, null, null, null, Now, null, null);

    public static IEnumerable<object[]> StatusMoves() => StatusMoveMatrix.AllPairs();

    [Theory]
    [MemberData(nameof(StatusMoves))]
    public async Task ChangeStatus_WholeMatrix_AllowedMovesSaveAndEveryOtherMoveConflicts(string from, string to, bool allowed)
    {
        var job = ArrangeJob(from);
        var service = Build();
        var request = new WorkOrderStatusRequest(to, "dispatcher1");

        if (allowed)
        {
            var result = await service.ChangeStatusAsync(JobId, request);

            Assert.Equal(to, job.Status);
            Assert.Equal(to, result.Status);
            _jobs.Verify(r => r.UpdateAsync(job), Times.Once);
            _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Once);
        }
        else
        {
            await Assert.ThrowsAsync<ConflictException>(() => service.ChangeStatusAsync(JobId, request));

            Assert.Equal(from, job.Status);
            _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
            _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
        }
    }

    [Fact]
    public async Task ChangeStatus_RequestedToTriaged_WritesNoteWithAuthorAndUserName()
    {
        ArrangeJob(WorkOrderStatus.Requested);

        await Build().ChangeStatusAsync(JobId, new WorkOrderStatusRequest(WorkOrderStatus.Triaged, "dispatcher1"));

        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n =>
            n.JobId == JobId
            && n.Author == "dispatcher1"
            && n.Note == "Status changed from Requested to Triaged by dispatcher1"
            && n.CreatedAt == _job.UpdatedAt)), Times.Once);
    }

    [Fact]
    public async Task ChangeStatus_Cancel_RecordsThePreviousStatusInTheNote()
    {
        ArrangeJob(WorkOrderStatus.OnSite);

        await Build().ChangeStatusAsync(JobId, new WorkOrderStatusRequest(WorkOrderStatus.Cancelled, "ops-lead"));

        _jobs.Verify(r => r.AddNoteAsync(It.Is<JobNote>(n =>
            n.Note == "Status changed from OnSite to Cancelled by ops-lead" && n.Author == "ops-lead")), Times.Once);
    }

    [Fact]
    public async Task ChangeStatus_LocksJobBeforeReadingStatus()
    {
        ArrangeJob(WorkOrderStatus.Requested);
        var trace = new List<string>();
        _jobs.Setup(r => r.LockForUpdateAsync(JobId)).Callback(() => trace.Add("lock")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.GetAsync(JobId)).Callback(() => trace.Add("read")).ReturnsAsync(_job);

        await Build().ChangeStatusAsync(JobId, new WorkOrderStatusRequest(WorkOrderStatus.Triaged, "dispatcher1"));

        Assert.Equal(new[] { "lock", "read" }, trace);
    }

    [Fact]
    public async Task ChangeStatus_StatusChangedWhileWaitingForLock_ConflictsAndWritesNothing()
    {
        // Another user cancelled the job and committed while this call was waiting for the row lock.
        var job = ArrangeJob(WorkOrderStatus.Requested);
        _jobs.Setup(r => r.LockForUpdateAsync(JobId)).Callback(() => job.Status = WorkOrderStatus.Cancelled).Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Build().ChangeStatusAsync(JobId, new WorkOrderStatusRequest(WorkOrderStatus.Triaged, "dispatcher1")));

        Assert.Equal(WorkOrderStatus.Cancelled, job.Status);
        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    [Fact]
    public async Task ChangeStatus_UnknownJob_ThrowsNotFoundAndWritesNothing()
    {
        _jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync((Job?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Build().ChangeStatusAsync(JobId, new WorkOrderStatusRequest(WorkOrderStatus.Triaged, "dispatcher1")));

        _jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        _jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    [Fact]
    public async Task ChangeStatus_CompletedJob_CancelIsRefusedWithConflict()
    {
        // Spec R3 and AC-09 say 422; the code returns 409 (ConflictException). This test pins the code.
        var job = ArrangeJob(WorkOrderStatus.Completed);

        await Assert.ThrowsAsync<ConflictException>(() =>
            Build().ChangeStatusAsync(JobId, new WorkOrderStatusRequest(WorkOrderStatus.Cancelled, "dispatcher1")));

        Assert.Equal(WorkOrderStatus.Completed, job.Status);
    }
}
