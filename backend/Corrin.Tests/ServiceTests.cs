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
    private readonly Mock<IReorderService> _reorder = new();
    private readonly Mock<IReferenceRepository> _reference = new();

    private JobService Build() => new(_jobs.Object, _customers.Object, _sites.Object, _engineers.Object, _parts.Object, _reorder.Object,
        _reference.Object, new EngineerBookingRules(_jobs.Object));

    // Runs the transaction delegate inline, as the real repository would (minus the DB transaction).
    private void RunTransactionsInline() =>
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<JobPart>>>()))
            .Returns<Func<Task<JobPart>>>(work => work());

    [Fact]
    public async Task AddPart_InsufficientStock_ThrowsConflict()
    {
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 1 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 2)).ReturnsAsync(0);
        RunTransactionsInline();

        await Assert.ThrowsAsync<ConflictException>(() => Build().AddPartAsync(10, new JobPartRequest(2, 2)));
    }

    [Fact]
    public async Task AddPart_Success_DecrementsStockAndAddsJobPart()
    {
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 5 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 3)).ReturnsAsync(1);
        _jobs.Setup(r => r.AddPartAsync(It.IsAny<JobPart>()))
            .ReturnsAsync((JobPart jp) => { jp.Id = 77; return jp; });
        RunTransactionsInline();

        var dto = await Build().AddPartAsync(10, new JobPartRequest(2, 3));

        Assert.Equal(new JobPartDto(77, 10, 2, 3), dto);
        _parts.Verify(r => r.TryDecrementStockAsync(2, 3), Times.Once);
        _jobs.Verify(r => r.AddPartAsync(It.Is<JobPart>(jp => jp.JobId == 10 && jp.PartId == 2 && jp.Quantity == 3)), Times.Once);
    }

    [Fact]
    public async Task AddPart_ZeroRowsAffected_ThrowsConflictWithStockMessageAndSkipsJobPart()
    {
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 1 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 2)).ReturnsAsync(0);
        // The stock as it is now, re-read inside the transaction.
        _parts.Setup(r => r.GetStockLevelAsync(2)).ReturnsAsync(((int StockQty, int ReorderLevel)?)(1, 0));
        RunTransactionsInline();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Build().AddPartAsync(10, new JobPartRequest(2, 2)));

        Assert.Equal("Only 1 of DV-WB-01 in stock.", ex.Message);
        _jobs.Verify(r => r.AddPartAsync(It.IsAny<JobPart>()), Times.Never);
    }

    [Fact]
    public async Task AddPart_ZeroRowsAffected_MessageUsesReReadStockNotStaleRead()
    {
        // The part was read with 5 in stock, but another booking took 3 before this decrement ran. The message must say 2.
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 5 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 4)).ReturnsAsync(0);
        _parts.Setup(r => r.GetStockLevelAsync(2)).ReturnsAsync(((int StockQty, int ReorderLevel)?)(2, 0));
        RunTransactionsInline();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Build().AddPartAsync(10, new JobPartRequest(2, 4)));

        Assert.Equal("Only 2 of DV-WB-01 in stock.", ex.Message);
        _parts.Verify(r => r.GetStockLevelAsync(2), Times.Once);
        _jobs.Verify(r => r.AddPartAsync(It.IsAny<JobPart>()), Times.Never);
    }

    [Fact]
    public async Task AddPart_ZeroRowsAffectedAndPartNoLongerReadable_SaysOnlyZero()
    {
        // The re-read returns no row (null), so the message falls back to 0.
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 5 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 4)).ReturnsAsync(0);
        _parts.Setup(r => r.GetStockLevelAsync(2)).ReturnsAsync(((int StockQty, int ReorderLevel)?)null);
        RunTransactionsInline();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Build().AddPartAsync(10, new JobPartRequest(2, 4)));

        Assert.Equal("Only 0 of DV-WB-01 in stock.", ex.Message);
        _jobs.Verify(r => r.AddPartAsync(It.IsAny<JobPart>()), Times.Never);
    }

    [Fact]
    public async Task AddPart_ZeroStockOnHand_ThrowsConflictSayingZeroInStock()
    {
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 0 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 1)).ReturnsAsync(0);
        _parts.Setup(r => r.GetStockLevelAsync(2)).ReturnsAsync(((int StockQty, int ReorderLevel)?)(0, 0));
        RunTransactionsInline();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Build().AddPartAsync(10, new JobPartRequest(2, 1)));

        Assert.Equal("Only 0 of DV-WB-01 in stock.", ex.Message);
    }

    [Fact]
    public async Task AddPart_CancelledJob_ThrowsBusinessRuleAndDoesNotTouchStock()
    {
        // The status is checked inside the transaction, after the job row lock, so the check sees committed state.
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 5 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Cancelled" });
        RunTransactionsInline();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Build().AddPartAsync(10, new JobPartRequest(2, 1)));

        Assert.Equal("Parts cannot be added to a cancelled job.", ex.Message);
        _parts.Verify(r => r.TryDecrementStockAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _jobs.Verify(r => r.AddPartAsync(It.IsAny<JobPart>()), Times.Never);
    }

    [Fact]
    public async Task AddPart_UnknownPart_ThrowsNotFoundAndDoesNotTouchStock()
    {
        _parts.Setup(r => r.GetAsync(99)).ReturnsAsync((Part?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Build().AddPartAsync(10, new JobPartRequest(99, 1)));

        Assert.Contains("99", ex.Message);
        _parts.Verify(r => r.TryDecrementStockAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AddPart_UnknownJob_ThrowsNotFoundAndDoesNotTouchStock()
    {
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 5 });
        _jobs.Setup(r => r.GetAsync(404)).ReturnsAsync((Job?)null);
        RunTransactionsInline();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Build().AddPartAsync(404, new JobPartRequest(2, 1)));

        Assert.Contains("404", ex.Message);
        _parts.Verify(r => r.TryDecrementStockAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    // Runs the transaction delegate inline and records where the transaction starts and ends in the trace.
    private void TraceTransaction(List<string> trace) =>
        _jobs.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<JobPart>>>()))
            .Returns<Func<Task<JobPart>>>(async work =>
            {
                trace.Add("tx-start");
                var result = await work();
                trace.Add("tx-end");
                return result;
            });

    [Fact]
    public async Task AddPart_Success_ReorderCheckRunsAfterDecrementInsideTransaction()
    {
        var trace = new List<string>();
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 5 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 3)).ReturnsAsync(1).Callback(() => trace.Add("decrement"));
        _reorder.Setup(r => r.EnsureReorderForPartAsync(2)).Returns(Task.CompletedTask).Callback(() => trace.Add("reorder"));
        _jobs.Setup(r => r.AddPartAsync(It.IsAny<JobPart>()))
            .ReturnsAsync((JobPart jp) => { trace.Add("job-part"); jp.Id = 77; return jp; });
        TraceTransaction(trace);

        await Build().AddPartAsync(10, new JobPartRequest(2, 3));

        Assert.Equal(new[] { "tx-start", "decrement", "reorder", "job-part", "tx-end" }, trace);
        _reorder.Verify(r => r.EnsureReorderForPartAsync(2), Times.Once);
    }

    [Fact]
    public async Task AddPart_ReorderCheckFails_ThrowsAndDoesNotSaveJobPart()
    {
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 5 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 3)).ReturnsAsync(1);
        _reorder.Setup(r => r.EnsureReorderForPartAsync(2)).ThrowsAsync(new InvalidOperationException("reorder failed"));
        RunTransactionsInline();

        // In the real repository the exception rolls the decrement back with the transaction.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Build().AddPartAsync(10, new JobPartRequest(2, 3)));

        _jobs.Verify(r => r.AddPartAsync(It.IsAny<JobPart>()), Times.Never);
    }

    [Fact]
    public async Task AddPart_InsufficientStock_DoesNotCallReorderCheck()
    {
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 1 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Scheduled" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 2)).ReturnsAsync(0);
        RunTransactionsInline();

        await Assert.ThrowsAsync<ConflictException>(() => Build().AddPartAsync(10, new JobPartRequest(2, 2)));

        _reorder.Verify(r => r.EnsureReorderForPartAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AddPart_LocksJobBeforeReadingStatusAndDecrementingStock()
    {
        var trace = new List<string>();
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 5 });
        _jobs.Setup(r => r.LockForUpdateAsync(10)).Callback(() => trace.Add("lock")).Returns(Task.CompletedTask);
        _jobs.Setup(r => r.GetAsync(10)).Callback(() => trace.Add("read")).ReturnsAsync(new Job { Id = 10, Status = "OnSite" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 1)).ReturnsAsync(1).Callback(() => trace.Add("decrement"));
        _jobs.Setup(r => r.AddPartAsync(It.IsAny<JobPart>()))
            .ReturnsAsync((JobPart jp) => { jp.Id = 5; return jp; });
        TraceTransaction(trace);

        await Build().AddPartAsync(10, new JobPartRequest(2, 1));

        Assert.Equal(new[] { "tx-start", "lock", "read", "decrement", "tx-end" }, trace);
    }

    [Fact]
    public async Task AddPart_CompletedJob_IsAllowed()
    {
        // Only a cancelled job refuses parts. A completed job (for example a visit that finished) still takes them.
        _parts.Setup(r => r.GetAsync(2)).ReturnsAsync(new Part { Id = 2, Sku = "DV-WB-01", StockQty = 5 });
        _jobs.Setup(r => r.GetAsync(10)).ReturnsAsync(new Job { Id = 10, Status = "Completed" });
        _parts.Setup(r => r.TryDecrementStockAsync(2, 1)).ReturnsAsync(1);
        _jobs.Setup(r => r.AddPartAsync(It.IsAny<JobPart>()))
            .ReturnsAsync((JobPart jp) => { jp.Id = 6; return jp; });
        RunTransactionsInline();

        var dto = await Build().AddPartAsync(10, new JobPartRequest(2, 1));

        Assert.Equal(new JobPartDto(6, 10, 2, 1), dto);
        _parts.Verify(r => r.TryDecrementStockAsync(2, 1), Times.Once);
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
