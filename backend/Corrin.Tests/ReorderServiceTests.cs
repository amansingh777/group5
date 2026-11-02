using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Moq;

namespace Corrin.Tests;

// Mocked repositories. The repository's HasOpenOrOrderedAsync is set to what the real query returns for each status;
// the real query itself is checked in ReorderRepositoryTests.
public class ReorderServiceTests
{
    private readonly Mock<IReorderRequestRepository> _reorders = new();
    private readonly Mock<IPartRepository> _parts = new();

    private ReorderService Build() => new(_reorders.Object, _parts.Object);

    private static ReorderRequestRow Row(int id, string status, DateOnly? expectedOn = null, string? orderedBy = null,
        string? receivedBy = null, int stock = 1, int level = 4) =>
        new(id, 2, "DV-WB-01", "Diverter valve, Worcester Bosch", stock, level, status, expectedOn, orderedBy, receivedBy,
            new DateTime(2026, 10, 1, 9, 0, 0), new DateTime(2026, 10, 1, 9, 0, 0));

    // Runs the transaction delegate inline, as the real repository would (minus the DB transaction).
    private void RunTransactionsInline() =>
        _reorders.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<int>>>()))
            .Returns<Func<Task<int>>>(work => work());

    // ---- EnsureReorderForPartAsync ----

    [Theory]
    [InlineData(4, 4)] // exactly at the reorder level
    [InlineData(3, 4)] // below it
    [InlineData(0, 4)] // out of stock
    public async Task EnsureReorder_AtOrBelowLevelWithNoOpenRequest_CreatesOneOpenRequest(int stock, int level)
    {
        _parts.Setup(r => r.GetStockLevelAsync(2)).ReturnsAsync((stock, level));
        _reorders.Setup(r => r.HasOpenOrOrderedAsync(2)).ReturnsAsync(false);

        await Build().EnsureReorderForPartAsync(2);

        _reorders.Verify(r => r.AddAsync(It.Is<ReorderRequest>(q =>
            q.PartId == 2 && q.Status == "Open" && q.ExpectedOn == null && q.OrderedBy == null && q.ReceivedBy == null
            && q.CreatedAt == q.UpdatedAt)), Times.Once);
    }

    [Theory]
    [InlineData(5, 4)]  // one above the level
    [InlineData(100, 4)]
    public async Task EnsureReorder_AboveLevel_CreatesNoRequest(int stock, int level)
    {
        _parts.Setup(r => r.GetStockLevelAsync(2)).ReturnsAsync((stock, level));

        await Build().EnsureReorderForPartAsync(2);

        _reorders.Verify(r => r.AddAsync(It.IsAny<ReorderRequest>()), Times.Never);
        _reorders.Verify(r => r.HasOpenOrOrderedAsync(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(4, 4)] // exactly at the level, open request exists
    [InlineData(1, 4)]
    public async Task EnsureReorder_OpenRequestExists_CreatesNoDuplicate(int stock, int level)
    {
        _parts.Setup(r => r.GetStockLevelAsync(2)).ReturnsAsync((stock, level));
        _reorders.Setup(r => r.HasOpenOrOrderedAsync(2)).ReturnsAsync(true); // the real query is true for Open

        await Build().EnsureReorderForPartAsync(2);

        _reorders.Verify(r => r.AddAsync(It.IsAny<ReorderRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(4, 4)] // exactly at the level, ordered request exists
    [InlineData(1, 4)]
    public async Task EnsureReorder_OrderedRequestExists_CreatesNoDuplicate(int stock, int level)
    {
        _parts.Setup(r => r.GetStockLevelAsync(2)).ReturnsAsync((stock, level));
        _reorders.Setup(r => r.HasOpenOrOrderedAsync(2)).ReturnsAsync(true); // the real query is true for Ordered

        await Build().EnsureReorderForPartAsync(2);

        _reorders.Verify(r => r.AddAsync(It.IsAny<ReorderRequest>()), Times.Never);
    }

    [Fact]
    public async Task EnsureReorder_OnlyReceivedRequestExists_CreatesOpenRequest()
    {
        // A received request does not count as open or ordered, so the repository reports false and a new request is raised.
        _parts.Setup(r => r.GetStockLevelAsync(2)).ReturnsAsync((4, 4));
        _reorders.Setup(r => r.HasOpenOrOrderedAsync(2)).ReturnsAsync(false);

        await Build().EnsureReorderForPartAsync(2);

        _reorders.Verify(r => r.AddAsync(It.Is<ReorderRequest>(q => q.PartId == 2 && q.Status == "Open")), Times.Once);
    }

    [Fact]
    public async Task EnsureReorder_UnknownPart_CreatesNoRequest()
    {
        _parts.Setup(r => r.GetStockLevelAsync(99)).ReturnsAsync(default((int StockQty, int ReorderLevel)?));

        await Build().EnsureReorderForPartAsync(99);

        _reorders.Verify(r => r.AddAsync(It.IsAny<ReorderRequest>()), Times.Never);
    }

    // ---- ListAsync ----

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(500, 100)]
    public async Task ListAsync_PageSize_IsClampedToOneToOneHundred(int requested, int expected)
    {
        _reorders.Setup(r => r.ListAsync(It.IsAny<ReorderRequestFilter>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((new List<ReorderRequestRow>(), 0));

        var page = await Build().ListAsync(new ReorderRequestListQuery(1, requested));

        Assert.Equal(expected, page.PageSize);
        _reorders.Verify(r => r.ListAsync(It.IsAny<ReorderRequestFilter>(), 1, expected), Times.Once);
    }

    [Fact]
    public async Task ListAsync_PageBelowOne_IsTreatedAsPageOne()
    {
        _reorders.Setup(r => r.ListAsync(It.IsAny<ReorderRequestFilter>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((new List<ReorderRequestRow>(), 0));

        var page = await Build().ListAsync(new ReorderRequestListQuery(0, 25));

        Assert.Equal(1, page.Page);
        _reorders.Verify(r => r.ListAsync(It.IsAny<ReorderRequestFilter>(), 1, 25), Times.Once);
    }

    [Fact]
    public async Task ListAsync_PassesStatusAndPartFilterThroughAndMapsRows()
    {
        var rows = new List<ReorderRequestRow> { Row(5, "Open", stock: 1, level: 4) };
        _reorders.Setup(r => r.ListAsync(new ReorderRequestFilter("Open", 2), 2, 10)).ReturnsAsync((rows, 11));

        var page = await Build().ListAsync(new ReorderRequestListQuery(2, 10, "Open", 2));

        Assert.Equal(11, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal(10, page.PageSize);
        var item = Assert.Single(page.Items);
        Assert.Equal(new ReorderRequestDto(5, 2, "DV-WB-01", "Diverter valve, Worcester Bosch", 1, 4, true, "Open",
            null, null, null, new DateTime(2026, 10, 1, 9, 0, 0), new DateTime(2026, 10, 1, 9, 0, 0)), item);
    }

    [Fact]
    public async Task ListAsync_NoRows_ReturnsEmptyPage()
    {
        _reorders.Setup(r => r.ListAsync(It.IsAny<ReorderRequestFilter>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((new List<ReorderRequestRow>(), 0));

        var page = await Build().ListAsync(new ReorderRequestListQuery());

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    // ---- MarkOrderedAsync ----

    [Fact]
    public async Task MarkOrdered_Open_MovesToOrderedWithDateAndUser()
    {
        var expected = new DateOnly(2026, 11, 10);
        _reorders.Setup(r => r.GetAsync(5)).ReturnsAsync(new ReorderRequest { Id = 5, PartId = 2, Status = "Open" });
        _reorders.Setup(r => r.TryMarkOrderedAsync(5, expected, "Priya Shah", It.IsAny<DateTime>())).ReturnsAsync(1);
        _reorders.Setup(r => r.GetRowAsync(5))
            .ReturnsAsync(Row(5, "Ordered", expectedOn: expected, orderedBy: "Priya Shah"));

        var dto = await Build().MarkOrderedAsync(5, new ReorderOrderedRequest(expected, "Priya Shah"));

        Assert.Equal("Ordered", dto.Status);
        Assert.Equal(expected, dto.ExpectedOn);
        Assert.Equal("Priya Shah", dto.OrderedBy);
        Assert.Null(dto.ReceivedBy);
        _reorders.Verify(r => r.TryMarkOrderedAsync(5, expected, "Priya Shah", It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task MarkOrdered_UnknownId_ThrowsNotFound()
    {
        _reorders.Setup(r => r.GetAsync(999)).ReturnsAsync((ReorderRequest?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            Build().MarkOrderedAsync(999, new ReorderOrderedRequest(new DateOnly(2026, 11, 10), "Priya")));

        Assert.Equal("Reorder request 999 was not found.", ex.Message);
        _reorders.Verify(r => r.TryMarkOrderedAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Theory]
    [InlineData("Ordered")]
    [InlineData("Received")]
    public async Task MarkOrdered_NotOpen_ThrowsConflictAndDoesNotChangeRow(string status)
    {
        _reorders.Setup(r => r.GetAsync(5)).ReturnsAsync(new ReorderRequest { Id = 5, PartId = 2, Status = status });
        _reorders.Setup(r => r.TryMarkOrderedAsync(5, It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(0);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Build().MarkOrderedAsync(5, new ReorderOrderedRequest(new DateOnly(2026, 11, 10), "Priya")));

        Assert.Equal($"Reorder request 5 is {status}, not Open.", ex.Message);
        _reorders.Verify(r => r.GetRowAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task MarkOrdered_LostRace_SecondCallerGetsConflict()
    {
        // Both callers read the request while it is still Open. The first conditional UPDATE wins; the second gets 0 rows.
        _reorders.Setup(r => r.GetAsync(5)).ReturnsAsync(new ReorderRequest { Id = 5, PartId = 2, Status = "Open" });
        _reorders.SetupSequence(r => r.TryMarkOrderedAsync(5, It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync(1)
            .ReturnsAsync(0);
        var service = Build();
        var request = new ReorderOrderedRequest(new DateOnly(2026, 11, 10), "Priya");
        _reorders.Setup(r => r.GetRowAsync(5)).ReturnsAsync(Row(5, "Ordered", expectedOn: request.ExpectedOn, orderedBy: "Priya"));

        var first = await service.MarkOrderedAsync(5, request);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.MarkOrderedAsync(5, new ReorderOrderedRequest(request.ExpectedOn, "Sam")));

        Assert.Equal("Ordered", first.Status);
        Assert.Equal("Priya", first.OrderedBy);
        Assert.Equal("Reorder request 5 is no longer Open.", ex.Message);
        _reorders.Verify(r => r.TryMarkOrderedAsync(5, It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Exactly(2));
    }

    // ---- MarkReceivedAsync ----

    [Fact]
    public async Task MarkReceived_Ordered_IncrementsStockInsideTransactionAndRecordsUser()
    {
        var trace = new List<string>();
        _reorders.Setup(r => r.GetAsync(5)).ReturnsAsync(new ReorderRequest { Id = 5, PartId = 2, Status = "Ordered" });
        _reorders.Setup(r => r.InTransactionAsync(It.IsAny<Func<Task<int>>>()))
            .Returns<Func<Task<int>>>(async work =>
            {
                trace.Add("tx-start");
                var result = await work();
                trace.Add("tx-end");
                return result;
            });
        _reorders.Setup(r => r.TryMarkReceivedAsync(5, "Sam Lee", It.IsAny<DateTime>())).ReturnsAsync(1)
            .Callback(() => trace.Add("mark-received"));
        _parts.Setup(r => r.IncrementStockAsync(2, 7)).ReturnsAsync(1).Callback(() => trace.Add("increment"));
        _reorders.Setup(r => r.GetRowAsync(5))
            .ReturnsAsync(Row(5, "Received", expectedOn: new DateOnly(2026, 11, 10), orderedBy: "Priya", receivedBy: "Sam Lee"));

        var dto = await Build().MarkReceivedAsync(5, new ReorderReceivedRequest(7, "Sam Lee"));

        Assert.Equal(new[] { "tx-start", "mark-received", "increment", "tx-end" }, trace);
        Assert.Equal("Received", dto.Status);
        Assert.Equal("Sam Lee", dto.ReceivedBy);
        Assert.Equal("Priya", dto.OrderedBy);
        _parts.Verify(r => r.IncrementStockAsync(2, 7), Times.Once);
        _reorders.Verify(r => r.InTransactionAsync(It.IsAny<Func<Task<int>>>()), Times.Once);
    }

    [Fact]
    public async Task MarkReceived_UnknownId_ThrowsNotFoundAndDoesNotTouchStock()
    {
        _reorders.Setup(r => r.GetAsync(999)).ReturnsAsync((ReorderRequest?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Build().MarkReceivedAsync(999, new ReorderReceivedRequest(3, "Sam")));

        _parts.Verify(r => r.IncrementStockAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _reorders.Verify(r => r.TryMarkReceivedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("Received")]
    public async Task MarkReceived_NotOrdered_ThrowsConflictAndAddsNoStock(string status)
    {
        _reorders.Setup(r => r.GetAsync(5)).ReturnsAsync(new ReorderRequest { Id = 5, PartId = 2, Status = status });

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Build().MarkReceivedAsync(5, new ReorderReceivedRequest(3, "Sam")));

        Assert.Equal($"Reorder request 5 is {status}, not Ordered.", ex.Message);
        _parts.Verify(r => r.IncrementStockAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _reorders.Verify(r => r.InTransactionAsync(It.IsAny<Func<Task<int>>>()), Times.Never);
    }

    [Fact]
    public async Task MarkReceived_LostRace_ConflictAndNoStockAdded()
    {
        // Read as Ordered, but another caller received it first, so the conditional UPDATE matches 0 rows.
        _reorders.Setup(r => r.GetAsync(5)).ReturnsAsync(new ReorderRequest { Id = 5, PartId = 2, Status = "Ordered" });
        _reorders.Setup(r => r.TryMarkReceivedAsync(5, "Sam", It.IsAny<DateTime>())).ReturnsAsync(0);
        RunTransactionsInline();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Build().MarkReceivedAsync(5, new ReorderReceivedRequest(3, "Sam")));

        Assert.Equal("Reorder request 5 is no longer Ordered.", ex.Message);
        _parts.Verify(r => r.IncrementStockAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _reorders.Verify(r => r.GetRowAsync(It.IsAny<int>()), Times.Never);
    }
}
