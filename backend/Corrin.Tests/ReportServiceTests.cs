using Corrin.Api.Domain;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Moq;

namespace Corrin.Tests;

public class ReportServiceTests
{
    private static ReportService Build(List<Job> jobs, List<Shift> shifts)
    {
        var engineers = new Mock<IEngineerRepository>();
        engineers.Setup(r => r.ListAsync(null, null, true)).ReturnsAsync(new List<Engineer>
        {
            new() { Id = 1, FullName = "Test Engineer", RegionId = 1, Active = true }
        });
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.GetCompletedBetweenAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null)).ReturnsAsync(jobs);
        var shiftRepo = new Mock<IShiftRepository>();
        shiftRepo.Setup(r => r.GetBetweenAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IEnumerable<int>>())).ReturnsAsync(shifts);
        return new ReportService(engineers.Object, jobRepo.Object, shiftRepo.Object);
    }

    [Fact]
    public async Task Utilisation_ReturnsWorkedHoursAsPercentage()
    {
        var jobs = Enumerable.Range(0, 3).Select(i => new Job
        {
            EngineerId = 1,
            Status = "Completed",
            ActualStart = new DateTime(2026, 9, 2 + i, 9, 0, 0),
            ActualEnd = new DateTime(2026, 9, 2 + i, 9, 0, 0).AddHours(30)
        }).ToList();
        var shifts = Enumerable.Range(0, 22).Select(i => new Shift
        {
            EngineerId = 1,
            ShiftDate = new DateOnly(2026, 9, 1).AddDays(i),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(16, 30),
            BreakMinutes = 30
        }).ToList();

        var rows = await Build(jobs, shifts).GetEngineerUtilisationAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);

        var row = Assert.Single(rows);
        Assert.Equal(90, row.HoursWorked);
        Assert.Equal(12.5, row.UtilisationPct);
    }

    [Fact]
    public async Task Utilisation_RejectsReversedDates()
    {
        var service = Build(new List<Job>(), new List<Shift>());
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GetEngineerUtilisationAsync(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1), null));
    }

    [Fact]
    public async Task Utilisation_EngineerWithNoJobsIsZero()
    {
        var rows = await Build(new List<Job>(), new List<Shift>()).GetEngineerUtilisationAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);
        Assert.Equal(0, Assert.Single(rows).UtilisationPct);
    }
}
