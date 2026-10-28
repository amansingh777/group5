using Corrin.Api.Dtos;
using Corrin.Api.Validators;

namespace Corrin.Tests;

// Scheduled start and end rules on the legacy POST /api/jobs (create) and PUT /api/jobs/{id} (update) validators.
public class JobScheduleValidatorTests
{
    private const string BothOrNeither = "Give both a scheduled start and end, or neither.";
    private const string EndAfterStart = "The scheduled end must be after the scheduled start.";

    private static readonly DateTime Start = new(2030, 1, 7, 10, 0, 0);
    private static readonly DateTime End = new(2030, 1, 7, 12, 0, 0);

    private static readonly JobCreateRequestValidator CreateValidator = new();
    private static readonly JobUpdateRequestValidator UpdateValidator = new();

    private static JobCreateRequest Create(DateTime? start, DateTime? end, int? engineerId = null) =>
        new(1, 5, null, 1, engineerId, "P3", start, end, null);

    private static JobUpdateRequest Update(DateTime? start, DateTime? end, string status = "Requested", int? engineerId = null) =>
        new(engineerId, "P3", status, start, end, null, null, null, null);

    // ---- create ----

    [Fact]
    public void Create_StartWithoutEnd_IsRejectedWithBothOrNeitherMessage()
    {
        var result = CreateValidator.Validate(Create(Start, null));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("ScheduledEnd", error.PropertyName);
        Assert.Equal(BothOrNeither, error.ErrorMessage);
    }

    [Fact]
    public void Create_EndWithoutStart_IsRejectedWithBothOrNeitherMessage()
    {
        var result = CreateValidator.Validate(Create(null, End));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("ScheduledEnd", error.PropertyName);
        Assert.Equal(BothOrNeither, error.ErrorMessage);
    }

    [Fact]
    public void Create_EndEqualToStart_IsRejectedWithEndAfterStartMessage()
    {
        var result = CreateValidator.Validate(Create(Start, Start));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("ScheduledEnd", error.PropertyName);
        Assert.Equal(EndAfterStart, error.ErrorMessage);
    }

    [Fact]
    public void Create_EndBeforeStart_IsRejectedWithEndAfterStartMessage()
    {
        var result = CreateValidator.Validate(Create(End, Start));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(EndAfterStart, error.ErrorMessage);
    }

    [Fact]
    public void Create_BothTimesWithNoEngineer_IsValid()
    {
        // Times without an engineer are allowed; the job is simply Requested with a window.
        Assert.True(CreateValidator.Validate(Create(Start, End)).IsValid);
    }

    [Fact]
    public void Create_NeitherTime_IsValid()
    {
        Assert.True(CreateValidator.Validate(Create(null, null, engineerId: 2)).IsValid);
    }

    [Fact]
    public void Create_BothTimesInOrder_IsValid()
    {
        Assert.True(CreateValidator.Validate(Create(Start, End, engineerId: 2)).IsValid);
    }

    // ---- update ----

    [Fact]
    public void Update_StartWithoutEnd_IsRejectedWithBothOrNeitherMessage()
    {
        var result = UpdateValidator.Validate(Update(Start, null, status: "Scheduled", engineerId: 2));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("ScheduledEnd", error.PropertyName);
        Assert.Equal(BothOrNeither, error.ErrorMessage);
    }

    [Fact]
    public void Update_EndWithoutStart_IsRejectedWithBothOrNeitherMessage()
    {
        var result = UpdateValidator.Validate(Update(null, End));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("ScheduledEnd", error.PropertyName);
        Assert.Equal(BothOrNeither, error.ErrorMessage);
    }

    [Fact]
    public void Update_NeitherTime_IsValid()
    {
        Assert.True(UpdateValidator.Validate(Update(null, null)).IsValid);
    }

    [Fact]
    public void Update_BothTimesInOrder_IsValid()
    {
        Assert.True(UpdateValidator.Validate(Update(Start, End, status: "Scheduled", engineerId: 2)).IsValid);
    }

    [Fact]
    public void Update_EndBeforeStart_IsRejectedOnScheduledEnd()
    {
        var result = UpdateValidator.Validate(Update(End, Start, status: "Scheduled", engineerId: 2));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ScheduledEnd");
    }
}
