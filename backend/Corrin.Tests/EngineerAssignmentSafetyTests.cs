using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Moq;

namespace Corrin.Tests;

// Every route that can put an engineer on a job applies the same hard rules (422, no override): the engineer must be
// active and must hold the job type's required skill. When either fails, nothing is saved. The engineer with the skill
// is accepted. The rules are real (EngineerBookingRules); the repositories are mocked.
//   legacy-create: POST /api/jobs (JobService.CreateAsync, with both times, so the engineer is booked)
//   legacy-update: PUT /api/jobs/{id} (JobService.UpdateAsync, Scheduled with both times)
//   allocate:      POST /api/work-orders/{id}/allocate
//   schedule:      POST /api/work-orders/{id}/schedule
public class EngineerAssignmentSafetyTests
{
    private const int JobId = 10;
    private const int EngineerId = 2;
    private const string NoSkill = "no-skill";
    private const string Inactive = "inactive";
    private static readonly DateOnly Day = WorkOrderTestData.Day;
    private static readonly DateTime Start = WorkOrderTestData.At(10);
    private static readonly DateTime End = WorkOrderTestData.At(12);

    public static TheoryData<string, string> RejectedCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var route in new[] { "legacy-create", "legacy-update", "allocate", "schedule" })
            foreach (var kind in new[] { NoSkill, Inactive })
                data.Add(route, kind);
        return data;
    }

    public static TheoryData<string, string> AcceptedCases() => new()
    {
        { "legacy-create", "Scheduled" },
        { "legacy-update", "Scheduled" },
        { "allocate", "Allocated" },
        { "schedule", "Scheduled" }
    };

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public async Task EngineerWithoutSkillOrInactive_Is422AndNothingIsSaved(string route, string kind)
    {
        var fixture = new Fixture(route, kind == NoSkill
            ? WorkOrderTestData.Engineer(EngineerId, skillIds: Array.Empty<int>())
            : WorkOrderTestData.Engineer(EngineerId, active: false, skillIds: WorkOrderTestData.GasSkill));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.RunAsync(route));

        Assert.Equal(ExpectedMessage(route, kind), ex.Message);
        fixture.Jobs.Verify(r => r.AddAsync(It.IsAny<Job>()), Times.Never);
        fixture.Jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), Times.Never);
        fixture.Jobs.Verify(r => r.AddNoteAsync(It.IsAny<JobNote>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(AcceptedCases))]
    public async Task ActiveEngineerWithRequiredSkill_IsAccepted(string route, string expectedStatus)
    {
        var fixture = new Fixture(route, WorkOrderTestData.Engineer(EngineerId, skillIds: WorkOrderTestData.GasSkill));

        var status = await fixture.RunAsync(route);

        Assert.Equal(expectedStatus, status);
        // Create inserts the job; the other routes update an existing one.
        fixture.Jobs.Verify(r => r.AddAsync(It.IsAny<Job>()), route == "legacy-create" ? Times.Once : Times.Never);
        fixture.Jobs.Verify(r => r.UpdateAsync(It.IsAny<Job>()), route == "legacy-create" ? Times.Never : Times.Once);
    }

    private static string ExpectedMessage(string route, string kind) => kind == NoSkill
        ? "The engineer does not hold the skill required for this job type."
        : $"An inactive engineer cannot be {(route == "allocate" ? "allocated" : "scheduled")}.";

    // Mocks for one route. The job starts Requested (allocate, legacy update, legacy create) or Allocated (schedule).
    private sealed class Fixture
    {
        public readonly Mock<IJobRepository> Jobs = new();
        private readonly Mock<IEngineerRepository> _engineers = new();
        private readonly Mock<IShiftRepository> _shifts = new();
        private readonly Mock<ISiteRepository> _sites = new();
        private readonly Mock<IReferenceRepository> _reference = new();
        private readonly Mock<ICustomerRepository> _customers = new();
        private readonly Mock<IPartRepository> _parts = new();
        private readonly Mock<IReorderService> _reorder = new();

        public Fixture(string route, Engineer engineer)
        {
            var job = WorkOrderTestData.Job(status: route == "schedule" ? "Allocated" : "Requested");

            _reference.Setup(r => r.GetJobTypeAsync(WorkOrderTestData.BoilerJobTypeId)).ReturnsAsync(WorkOrderTestData.BoilerJobType());
            _sites.Setup(r => r.GetReadOnlyAsync(5)).ReturnsAsync(new Site { Id = 5, CustomerId = 1, RegionId = 1 });
            _engineers.Setup(r => r.GetReadOnlyAsync(EngineerId)).ReturnsAsync(engineer);
            _engineers.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
            _shifts.Setup(r => r.GetForDayAsync(EngineerId, Day))
                .ReturnsAsync(new List<Shift> { WorkOrderTestData.Shift(EngineerId, new TimeOnly(8, 0), new TimeOnly(17, 0)) });

            Jobs.Setup(r => r.GetAsync(JobId)).ReturnsAsync(job);
            Jobs.Setup(r => r.GetReadOnlyAsync(JobId)).ReturnsAsync(WorkOrderTestData.Snapshot(job));
            Jobs.Setup(r => r.LockForUpdateAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
            Jobs.Setup(r => r.GetActiveBookingsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(new List<Job>());
            Jobs.Setup(r => r.AddAsync(It.IsAny<Job>())).ReturnsAsync((Job j) => j);
            Jobs.Setup(r => r.AddNoteAsync(It.IsAny<JobNote>())).ReturnsAsync((JobNote n) => n);
            // Runs each transaction delegate inline, as the real repository would (minus the DB transaction).
            Jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<Job>>>())).Returns<Func<Task<Job>>>(work => work());
            Jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<WorkOrderAllocationDto>>>()))
                .Returns<Func<Task<WorkOrderAllocationDto>>>(work => work());
            Jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<WorkOrderScheduleDto>>>()))
                .Returns<Func<Task<WorkOrderScheduleDto>>>(work => work());
        }

        // Runs the route and returns the resulting status.
        public async Task<string> RunAsync(string route)
        {
            var rules = new EngineerBookingRules(Jobs.Object);
            switch (route)
            {
                case "legacy-create":
                    return (await new JobService(Jobs.Object, _customers.Object, _sites.Object, _engineers.Object, _parts.Object,
                        _reorder.Object, _reference.Object, rules).CreateAsync(
                        new JobCreateRequest(1, 5, null, WorkOrderTestData.BoilerJobTypeId, EngineerId, "P3", Start, End, null))).Status;
                case "legacy-update":
                    return (await new JobService(Jobs.Object, _customers.Object, _sites.Object, _engineers.Object, _parts.Object,
                        _reorder.Object, _reference.Object, rules).UpdateAsync(JobId,
                        new JobUpdateRequest(EngineerId, "P3", "Scheduled", Start, End, null, null, null, null))).Status;
                case "allocate":
                    return (await new WorkOrderAllocationService(Jobs.Object, _engineers.Object, _shifts.Object, _sites.Object,
                        _reference.Object, rules).AllocateAsync(JobId, new WorkOrderAllocateRequest(EngineerId, "dispatcher1", Day))).Status;
                case "schedule":
                    return (await new WorkOrderSchedulingService(Jobs.Object, _engineers.Object, _shifts.Object, _reference.Object, rules)
                        .ScheduleAsync(JobId, new WorkOrderScheduleRequest(EngineerId, Start, "dispatcher1"))).Status;
                default:
                    throw new ArgumentOutOfRangeException(nameof(route));
            }
        }
    }
}
