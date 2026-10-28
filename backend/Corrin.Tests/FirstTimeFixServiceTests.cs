using Corrin.Api.Dtos;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Corrin.Api.Validators;
using Moq;

namespace Corrin.Tests;

// The repository decides which jobs are in the cohort and which were fixed first time (SQL, see FirstTimeFixRepositoryTests).
// These tests check the rate arithmetic and grouping on the rows the repository returns.
public class FirstTimeFixServiceTests
{
    private const string BoilerService = "Boiler service";
    private const string MeterInstall = "Meter install";
    private const string East = "East";
    private const string North = "North";

    private readonly Mock<IFirstTimeFixRepository> _repo = new();

    private FirstTimeFixService Build() => new(_repo.Object);

    private void Cohort(params FirstTimeFixCohortRow[] rows) =>
        _repo.Setup(r => r.GetCohortCountsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(rows.ToList());

    private static FirstTimeFixCohortRow Row(int regionId, string region, int jobTypeId, string jobType, int attended, int fixedFirstTime) =>
        new(regionId, region, jobTypeId, jobType, attended, fixedFirstTime);

    [Fact]
    public async Task GetAsync_NoCompletedWorkOrders_ReturnsZeroRateAndEmptyGroups()
    {
        Cohort();

        var report = await Build().GetAsync(new FirstTimeFixQuery("2026-01", "2026-01"));

        Assert.Equal("2026-01", report.From);
        Assert.Equal("2026-01", report.To);
        Assert.Equal("Overall", report.Overall.Name);
        Assert.Equal(0, report.Overall.JobsAttended);
        Assert.Equal(0, report.Overall.FixedFirstTime);
        Assert.Equal(0.0, report.Overall.RatePercent);
        Assert.Empty(report.ByRegion);
        Assert.Empty(report.ByJobType);
    }

    [Theory]
    [InlineData(3, 1, 33.3)]   // 33.33...
    [InlineData(3, 2, 66.7)]   // 66.66...
    [InlineData(8, 1, 12.5)]   // exact
    [InlineData(16, 1, 6.3)]   // 6.25 exactly; rounds away from zero to 6.3, not to even 6.2
    [InlineData(1, 1, 100.0)]  // all fixed
    [InlineData(7, 0, 0.0)]    // attended, none fixed first time
    public async Task GetAsync_RateIsFixedOverAttendedRoundedToOneDecimal(int attended, int fixedFirstTime, double expected)
    {
        Cohort(Row(1, East, 1, BoilerService, attended, fixedFirstTime));

        var report = await Build().GetAsync(new FirstTimeFixQuery("2026-01", "2026-03"));

        Assert.Equal(expected, report.Overall.RatePercent);
        Assert.Equal(expected, report.ByRegion.Single().RatePercent);
        Assert.Equal(expected, report.ByJobType.Single().RatePercent);
    }

    [Fact]
    public async Task GetAsync_MultipleRows_OverallEqualsSumOfRowsAndGroupsAreOrderedByName()
    {
        // East/Boiler 3 attended, 1 fixed; East/Meter 2 and 2; North/Boiler 5 and 0.
        Cohort(
            Row(1, East, 1, BoilerService, 3, 1),
            Row(1, East, 2, MeterInstall, 2, 2),
            Row(2, North, 1, BoilerService, 5, 0));

        var report = await Build().GetAsync(new FirstTimeFixQuery("2026-01", "2026-12"));

        Assert.Equal(10, report.Overall.JobsAttended);
        Assert.Equal(3, report.Overall.FixedFirstTime);
        Assert.Equal(30.0, report.Overall.RatePercent);

        Assert.Equal(new[] { East, North }, report.ByRegion.Select(r => r.Name));
        Assert.Equal(5, report.ByRegion[0].JobsAttended);   // East: 3 + 2
        Assert.Equal(3, report.ByRegion[0].FixedFirstTime); // East: 1 + 2
        Assert.Equal(60.0, report.ByRegion[0].RatePercent);
        Assert.Equal(5, report.ByRegion[1].JobsAttended);   // North
        Assert.Equal(0, report.ByRegion[1].FixedFirstTime);
        Assert.Equal(0.0, report.ByRegion[1].RatePercent);

        Assert.Equal(new[] { BoilerService, MeterInstall }, report.ByJobType.Select(t => t.Name));
        Assert.Equal(8, report.ByJobType[0].JobsAttended);  // Boiler: 3 + 5
        Assert.Equal(1, report.ByJobType[0].FixedFirstTime);
        Assert.Equal(12.5, report.ByJobType[0].RatePercent);
        Assert.Equal(2, report.ByJobType[1].JobsAttended);  // Meter: 2
        Assert.Equal(2, report.ByJobType[1].FixedFirstTime);
        Assert.Equal(100.0, report.ByJobType[1].RatePercent);

        Assert.Equal(report.ByRegion.Sum(r => r.JobsAttended), report.Overall.JobsAttended);
        Assert.Equal(report.ByRegion.Sum(r => r.FixedFirstTime), report.Overall.FixedFirstTime);
        Assert.Equal(report.ByJobType.Sum(t => t.JobsAttended), report.Overall.JobsAttended);
        Assert.Equal(report.ByJobType.Sum(t => t.FixedFirstTime), report.Overall.FixedFirstTime);
    }

    [Fact]
    public async Task GetAsync_AttendedButNotFixedFirstTime_CountsInDenominatorOnly()
    {
        // Two work orders had a first visit that was not Fixed (or a return visit). Neither counts as fixed.
        Cohort(Row(1, East, 1, BoilerService, 2, 0));

        var report = await Build().GetAsync(new FirstTimeFixQuery("2026-02", "2026-02"));

        Assert.Equal(2, report.Overall.JobsAttended);
        Assert.Equal(0, report.Overall.FixedFirstTime);
        Assert.Equal(0.0, report.Overall.RatePercent);
    }

    [Theory]
    [InlineData("2026-01", "2026-12", 2026, 1, 2027, 1)]
    [InlineData("2026-03", "2026-03", 2026, 3, 2026, 4)]
    [InlineData("2025-12", "2026-01", 2025, 12, 2026, 2)]
    public async Task GetAsync_MonthRange_PassesFirstDayOfFromToFirstDayAfterTo(
        string from, string to, int fromYear, int fromMonth, int endYear, int endMonth)
    {
        Cohort();

        await Build().GetAsync(new FirstTimeFixQuery(from, to));

        _repo.Verify(r => r.GetCohortCountsAsync(new DateTime(fromYear, fromMonth, 1), new DateTime(endYear, endMonth, 1)), Times.Once);
    }
}

public class FirstTimeFixQueryValidatorTests
{
    private static readonly FirstTimeFixQueryValidator Validator = new();

    [Theory]
    [InlineData("2026-01", "2026-12")]
    [InlineData("2026-06", "2026-06")]
    [InlineData("2025-12", "2026-01")]
    public void Validate_WellFormedRange_IsValid(string from, string to)
    {
        Assert.True(Validator.Validate(new FirstTimeFixQuery(from, to)).IsValid);
    }

    [Theory]
    [InlineData("2026-1", "2026-12")]
    [InlineData("2026-13", "2026-12")]
    [InlineData("2026-00", "2026-12")]
    [InlineData("26-01", "2026-12")]
    [InlineData("2026/01", "2026-12")]
    [InlineData("2026-01", "2026-1")]
    [InlineData("2026-01", "2026-00")]
    [InlineData("", "2026-12")]
    public void Validate_BadMonthFormat_IsRejected(string from, string to)
    {
        var result = Validator.Validate(new FirstTimeFixQuery(from, to));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ToBeforeFrom_IsRejectedOnTo()
    {
        var result = Validator.Validate(new FirstTimeFixQuery("2026-06", "2026-05"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "To" && e.ErrorMessage == "To must not be before From.");
    }

    [Fact]
    public void Validate_ToBeforeFromInSameYear_IsRejected()
    {
        Assert.False(Validator.Validate(new FirstTimeFixQuery("2026-12", "2026-11")).IsValid);
    }
}
