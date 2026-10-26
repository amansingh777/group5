using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Moq;

namespace Corrin.Tests;

public class ShiftServiceTests
{
    private readonly Mock<IShiftRepository> _shifts = new();
    private readonly Mock<IEngineerRepository> _engineers = new();

    [Fact]
    public void IsOnShift_TrueWhenNowIsInsideTheShift()
    {
        var service = new ShiftService(_shifts.Object, _engineers.Object);
        var shift = new Shift
        {
            ShiftDate = DateOnly.FromDateTime(DateTime.Now),
            StartTime = TimeOnly.FromDateTime(DateTime.Now.AddHours(-1)),
            EndTime = TimeOnly.FromDateTime(DateTime.Now.AddHours(1))
        };

        Assert.True(service.IsOnShift(shift));
    }

    [Fact]
    public void IsOnShift_FalseForYesterday()
    {
        var service = new ShiftService(_shifts.Object, _engineers.Object);
        var shift = new Shift
        {
            ShiftDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-1)),
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59)
        };

        Assert.False(service.IsOnShift(shift));
    }

    [Fact]
    public async Task Create_OverlappingShift_ThrowsConflict()
    {
        _engineers.Setup(r => r.GetReadOnlyAsync(1)).ReturnsAsync(new Engineer { Id = 1 });
        _shifts.Setup(r => r.GetForDayAsync(1, new DateOnly(2026, 10, 5))).ReturnsAsync(new List<Shift>
        {
            new() { Id = 9, EngineerId = 1, ShiftDate = new DateOnly(2026, 10, 5), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0) }
        });
        var service = new ShiftService(_shifts.Object, _engineers.Object);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(
            new ShiftRequest(1, new DateOnly(2026, 10, 5), new TimeOnly(15, 0), new TimeOnly(20, 0), 0, "Standard")));
    }
}

public class JobServiceTests
{
    private readonly Mock<IJobRepository> _jobs = new();
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<ISiteRepository> _sites = new();
    private readonly Mock<IEngineerRepository> _engineers = new();
    private readonly Mock<IPartRepository> _parts = new();

    private JobService Build() => new(_jobs.Object, _customers.Object, _sites.Object, _engineers.Object, _parts.Object);

    [Fact]
    public async Task AddPart_InsufficientStock_ThrowsConflict()
    {
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 1 });
        _jobs.Setup(r => r.GetReadOnlyAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });

        await Assert.ThrowsAsync<ConflictException>(() => Build().AddPartAsync(10, new JobPartRequest(2, 2)));
    }

    [Fact]
    public async Task Cancel_CompletedJob_ThrowsConflict()
    {
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Completed" });

        await Assert.ThrowsAsync<ConflictException>(() => Build().CancelAsync(10, new JobCancelRequest("changed mind")));
    }

    [Fact]
    public async Task Create_SiteOfAnotherCustomer_IsRejected()
    {
        _sites.Setup(r => r.GetReadOnlyAsync(5)).ReturnsAsync(new Site { Id = 5, CustomerId = 99, RegionId = 1 });

        await Assert.ThrowsAsync<BusinessRuleException>(() => Build().CreateAsync(
            new JobCreateRequest(1, 5, null, 1, null, "P3", null, null, null)));
    }
}

public class CustomerServiceTests
{
    [Fact]
    public async Task Create_UnknownRegion_IsRejected()
    {
        var customers = new Mock<ICustomerRepository>();
        var reference = new Mock<IReferenceRepository>();
        reference.Setup(r => r.RegionExistsAsync(42)).ReturnsAsync(false);
        var service = new CustomerService(customers.Object, reference.Object);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new CustomerRequest("A Name", "Residential", null, null, "1 High Street", "Leeds", "LS6 1AA", 42)));
    }
}
