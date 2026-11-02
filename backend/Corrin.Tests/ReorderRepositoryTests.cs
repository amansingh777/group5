using Corrin.Api.Data;
using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Corrin.Tests;

// The reorder rules that are SQL (conditional UPDATEs, the open-request check, the list join, the backfill) are checked
// against the real seeded MySQL database. Every write happens inside one transaction that is rolled back after each test,
// so no request, stock change or backfill row is left behind. Shares the "api" collection so that no API test runs
// while these transactions are open.
//
// Seeded facts used here: part 1 (PCB-WB-GR) has stock 6 and reorder level 3; part 17 (HP-CTRL) has stock 2 and level 2.
// The live request rows change as users work them, so tests that need a clean part first delete that part's requests
// inside their own transaction, and no test assumes the state of rows it did not create.
[Collection("api")]
public class ReorderRepositoryTests : IAsyncLifetime
{
    private const int PartOneId = 1;     // PCB-WB-GR, stock 6, level 3
    private const int PartSeventeenId = 17; // HP-CTRL, stock 2, level 2

    private readonly TestApiFactory _factory;
    private IServiceScope _scope = null!;
    private CorrinDbContext _db = null!;
    private IDbContextTransaction _tx = null!;

    public ReorderRepositoryTests(TestApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<CorrinDbContext>();
        _tx = await _db.Database.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        await _tx.RollbackAsync();
        await _tx.DisposeAsync();
        _scope.Dispose();
    }

    private ReorderRequestRepository Repo() => new(_db);
    private PartRepository Parts() => new(_db);

    private async Task<int> InsertAsync(int partId, string status)
    {
        var now = DateTime.UtcNow;
        var request = new ReorderRequest { PartId = partId, Status = status, CreatedAt = now, UpdatedAt = now };
        _db.ReorderRequests.Add(request);
        await _db.SaveChangesAsync();
        return request.Id;
    }

    private Task ClearRequestsAsync(int partId) =>
        _db.ReorderRequests.Where(r => r.PartId == partId).ExecuteDeleteAsync();

    private Task SetStockAsync(int partId, int stock) =>
        _db.Parts.Where(p => p.Id == partId).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQty, stock));

    private Task<int> OpenCountAsync(int partId) =>
        _db.ReorderRequests.AsNoTracking().CountAsync(r => r.PartId == partId && r.Status == "Open");

    // ---- Conditional transitions ----

    [Fact]
    public async Task TryMarkOrdered_SecondCallOnSameRow_ReturnsZeroAndKeepsFirstCaller()
    {
        var id = await InsertAsync(PartOneId, "Open");
        var firstDate = new DateOnly(2031, 3, 1);

        var first = await Repo().TryMarkOrderedAsync(id, firstDate, "Alice", DateTime.UtcNow);
        var second = await Repo().TryMarkOrderedAsync(id, new DateOnly(2031, 4, 1), "Bob", DateTime.UtcNow);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var row = await Repo().GetRowAsync(id);
        Assert.NotNull(row);
        Assert.Equal("Ordered", row!.Status);
        Assert.Equal(firstDate, row.ExpectedOn);
        Assert.Equal("Alice", row.OrderedBy);
    }

    [Theory]
    [InlineData("Ordered")]
    [InlineData("Received")]
    public async Task TryMarkOrdered_RequestNotOpen_ReturnsZeroAndChangesNothing(string status)
    {
        var id = await InsertAsync(PartOneId, status);

        var affected = await Repo().TryMarkOrderedAsync(id, new DateOnly(2031, 3, 1), "Alice", DateTime.UtcNow);

        Assert.Equal(0, affected);
        var row = await Repo().GetRowAsync(id);
        Assert.Equal(status, row!.Status);
        Assert.Null(row.OrderedBy);
        Assert.Null(row.ExpectedOn);
    }

    [Fact]
    public async Task TryMarkReceived_OpenRequest_ReturnsZeroAndStaysOpen()
    {
        var id = await InsertAsync(PartOneId, "Open");

        var affected = await Repo().TryMarkReceivedAsync(id, "Sam", DateTime.UtcNow);

        Assert.Equal(0, affected);
        Assert.Equal("Open", (await Repo().GetRowAsync(id))!.Status);
    }

    [Fact]
    public async Task TryMarkReceived_SecondCallOnSameRow_ReturnsZeroAndKeepsFirstReceiver()
    {
        var id = await InsertAsync(PartOneId, "Ordered");

        var first = await Repo().TryMarkReceivedAsync(id, "Sam", DateTime.UtcNow);
        var second = await Repo().TryMarkReceivedAsync(id, "Lee", DateTime.UtcNow);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var row = await Repo().GetRowAsync(id);
        Assert.Equal("Received", row!.Status);
        Assert.Equal("Sam", row.ReceivedBy);
    }

    // ---- Stock ----

    [Fact]
    public async Task IncrementStock_AddsExactlyTheQuantity()
    {
        var before = await Parts().GetStockLevelAsync(PartOneId);

        var affected = await Parts().IncrementStockAsync(PartOneId, 7);

        Assert.Equal(1, affected);
        Assert.Equal((before!.Value.StockQty + 7, before.Value.ReorderLevel), await Parts().GetStockLevelAsync(PartOneId));
    }

    [Fact]
    public async Task IncrementStock_UnknownPart_ReturnsZero()
    {
        Assert.Equal(0, await Parts().IncrementStockAsync(999999, 5));
    }

    [Fact]
    public async Task TryDecrementStock_QuantityEqualsStock_DecrementsToZero()
    {
        // Boundary: stock 6 can give exactly 6, and then nothing is left.
        Assert.Equal(1, await Parts().TryDecrementStockAsync(PartOneId, 6));
        Assert.Equal((0, 3), await Parts().GetStockLevelAsync(PartOneId));

        Assert.Equal(0, await Parts().TryDecrementStockAsync(PartOneId, 1));
    }

    [Fact]
    public async Task GetStockLevel_UnknownPart_ReturnsNull()
    {
        Assert.Null(await Parts().GetStockLevelAsync(999999));
    }

    // ---- Open-request check ----

    [Theory]
    [InlineData("Open", true)]
    [InlineData("Ordered", true)]
    [InlineData("Received", false)]
    public async Task HasOpenOrOrdered_OnlyOpenAndOrderedBlock(string status, bool expected)
    {
        await ClearRequestsAsync(PartOneId);
        await InsertAsync(PartOneId, status);

        Assert.Equal(expected, await Repo().HasOpenOrOrderedAsync(PartOneId));
    }

    [Fact]
    public async Task HasOpenOrOrdered_NoRequests_ReturnsFalse()
    {
        await ClearRequestsAsync(PartOneId);

        Assert.False(await Repo().HasOpenOrOrderedAsync(PartOneId));
    }

    // ---- Reorder service against the real repositories ----

    [Fact]
    public async Task EnsureReorder_ReceivedRequestExists_StillCreatesOneOpenRequest()
    {
        await ClearRequestsAsync(PartSeventeenId);
        await InsertAsync(PartSeventeenId, "Received");
        var service = new ReorderService(Repo(), Parts());

        await service.EnsureReorderForPartAsync(PartSeventeenId);

        Assert.Equal(1, await OpenCountAsync(PartSeventeenId));
        Assert.Equal(1, await _db.ReorderRequests.AsNoTracking().CountAsync(r => r.PartId == PartSeventeenId && r.Status == "Received"));
    }

    [Fact]
    public async Task EnsureReorder_CalledTwiceAtLevel_CreatesOnlyOneOpenRequest()
    {
        await ClearRequestsAsync(PartSeventeenId);
        var service = new ReorderService(Repo(), Parts());

        await service.EnsureReorderForPartAsync(PartSeventeenId);
        await service.EnsureReorderForPartAsync(PartSeventeenId);

        Assert.Equal(1, await OpenCountAsync(PartSeventeenId));
    }

    [Fact]
    public async Task EnsureReorder_OneAboveLevel_CreatesNoRequest()
    {
        // Part 1 has level 3. Stock 4 is one above it.
        await ClearRequestsAsync(PartOneId);
        await SetStockAsync(PartOneId, 4);
        var service = new ReorderService(Repo(), Parts());

        await service.EnsureReorderForPartAsync(PartOneId);

        Assert.Equal(0, await _db.ReorderRequests.AsNoTracking().CountAsync(r => r.PartId == PartOneId));
    }

    [Fact]
    public async Task EnsureReorder_ExactlyAtLevel_CreatesOneOpenRequest()
    {
        await ClearRequestsAsync(PartOneId);
        await SetStockAsync(PartOneId, 3);
        var service = new ReorderService(Repo(), Parts());

        await service.EnsureReorderForPartAsync(PartOneId);

        Assert.Equal(1, await OpenCountAsync(PartOneId));
    }

    // ---- List query ----

    [Fact]
    public async Task ListAsync_JoinsPartFieldsAndFlagsBelowReorderLevelAtTheBoundary()
    {
        await ClearRequestsAsync(PartOneId);
        var id = await InsertAsync(PartOneId, "Open");
        var filter = new ReorderRequestFilter(null, PartOneId);

        // Stock equal to the level (3): below reorder level is true.
        await SetStockAsync(PartOneId, 3);
        var (atLevel, _) = await Repo().ListAsync(filter, 1, 100);
        var row = Assert.Single(atLevel, r => r.Id == id);
        Assert.Equal("PCB-WB-GR", row.PartSku);
        Assert.Equal("Printed circuit board, Worcester Greenstar", row.PartName);
        Assert.Equal(3, row.StockQty);
        Assert.Equal(3, row.ReorderLevel);
        Assert.True(row.ToDto().BelowReorderLevel);

        // Stock one above the level (4): below reorder level is false.
        await SetStockAsync(PartOneId, 4);
        var (aboveLevel, _) = await Repo().ListAsync(filter, 1, 100);
        var aboveRow = Assert.Single(aboveLevel, r => r.Id == id);
        Assert.Equal(4, aboveRow.StockQty);
        Assert.False(aboveRow.ToDto().BelowReorderLevel);
    }

    [Fact]
    public async Task ListAsync_StatusFilter_ExcludesOtherStatuses()
    {
        await ClearRequestsAsync(PartOneId);
        var openId = await InsertAsync(PartOneId, "Open");
        var orderedId = await InsertAsync(PartOneId, "Ordered");

        var (items, total) = await Repo().ListAsync(new ReorderRequestFilter("Open", PartOneId), 1, 100);

        Assert.Equal(1, total);
        Assert.Equal(new[] { openId }, items.Select(r => r.Id).ToArray());
        Assert.DoesNotContain(items, r => r.Id == orderedId);
    }

    [Fact]
    public async Task ListAsync_NoMatchingRows_ReturnsZeroTotalAndNoItems()
    {
        await ClearRequestsAsync(PartOneId);

        var (items, total) = await Repo().ListAsync(new ReorderRequestFilter("Received", PartOneId), 1, 25);

        Assert.Equal(0, total);
        Assert.Empty(items);
    }

    // ---- Successful transitions end to end (ReorderService + real repositories, rolled back) ----

    private ReorderService ServiceOverRealRepos() => new(Repo(), Parts());

    [Fact]
    public async Task MarkOrdered_OpenRequest_SetsOrderedFieldsAndReadBackReturnsJoinedRow()
    {
        await ClearRequestsAsync(PartOneId);
        await SetStockAsync(PartOneId, 2);
        var id = await InsertAsync(PartOneId, "Open");
        var before = DateTime.UtcNow;

        var dto = await ServiceOverRealRepos().MarkOrderedAsync(id, new ReorderOrderedRequest(new DateOnly(2031, 3, 1), "Priya"));

        var after = DateTime.UtcNow;
        // Read back from the database, not the tracked context.
        var stored = await _db.ReorderRequests.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal("Ordered", stored.Status);
        Assert.Equal(new DateOnly(2031, 3, 1), stored.ExpectedOn);
        Assert.Equal("Priya", stored.OrderedBy);
        Assert.Null(stored.ReceivedBy);
        Assert.InRange(stored.UpdatedAt, before.AddSeconds(-1), after.AddSeconds(1));

        // The returned DTO comes from GetRowAsync: part sku, name and stock are joined in.
        Assert.Equal(id, dto.Id);
        Assert.Equal(PartOneId, dto.PartId);
        Assert.Equal("PCB-WB-GR", dto.PartSku);
        Assert.Equal("Printed circuit board, Worcester Greenstar", dto.PartName);
        Assert.Equal(2, dto.StockQty);
        Assert.Equal(3, dto.ReorderLevel);
        Assert.True(dto.BelowReorderLevel);
        Assert.Equal("Ordered", dto.Status);
        Assert.Equal(new DateOnly(2031, 3, 1), dto.ExpectedOn);
        Assert.Equal("Priya", dto.OrderedBy);
        Assert.Null(dto.ReceivedBy);
        Assert.Equal(stored.UpdatedAt, dto.UpdatedAt);
    }

    [Fact]
    public async Task MarkReceived_OrderedRequest_SetsReceivedByAndIncrementsStockByExactlyQuantity()
    {
        await ClearRequestsAsync(PartOneId);
        await SetStockAsync(PartOneId, 2);
        var id = await InsertAsync(PartOneId, "Ordered");

        var dto = await ServiceOverRealRepos().MarkReceivedAsync(id, new ReorderReceivedRequest(5, "Sam"));

        var stored = await _db.ReorderRequests.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal("Received", stored.Status);
        Assert.Equal("Sam", stored.ReceivedBy);
        Assert.Equal("Received", dto.Status);
        Assert.Equal("Sam", dto.ReceivedBy);
        // 2 + 5 = 7, and the joined row reports the same stock.
        Assert.Equal(7, dto.StockQty);
        Assert.Equal((7, 3), await Parts().GetStockLevelAsync(PartOneId));
    }

    [Fact]
    public async Task AddPart_CrossesReorderLevel_CreatesOneOpenRequestAndSecondDecrementAddsNone()
    {
        // Part 1: stock 4, level 3. Job 1 is Completed, which AddPartAsync accepts (only Cancelled is refused).
        await ClearRequestsAsync(PartOneId);
        await SetStockAsync(PartOneId, 4);
        var jobService = new JobService(new JobRepository(_db), new CustomerRepository(_db), new SiteRepository(_db),
            new EngineerRepository(_db), Parts(), ServiceOverRealRepos(), new ReferenceRepository(_db),
            new EngineerBookingRules(new JobRepository(_db)));

        // First decrement: 4 -> 3, which is at the level, so one Open request is raised.
        await jobService.AddPartAsync(1, new JobPartRequest(PartOneId, 1));
        Assert.Equal((3, 3), await Parts().GetStockLevelAsync(PartOneId));
        Assert.Equal(1, await OpenCountAsync(PartOneId));

        // Second decrement: 3 -> 2, still below the level, but an Open request already exists, so no new one.
        await jobService.AddPartAsync(1, new JobPartRequest(PartOneId, 1));
        Assert.Equal((2, 3), await Parts().GetStockLevelAsync(PartOneId));
        Assert.Equal(1, await OpenCountAsync(PartOneId));
        Assert.Equal(1, await _db.ReorderRequests.AsNoTracking().CountAsync(r => r.PartId == PartOneId));
    }

    // ---- Migration 008 backfill ----

    // Only the INSERT of migration 008. The ALTER TABLE in the same file would fail on a rerun (the columns exist), and MySQL
    // DDL commits the open transaction, which would break the rollback.
    private static string BackfillInsertSql()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("Corrin.Api.db.migrations.008_reorder_audit_and_backfill.sql")
            ?? throw new InvalidOperationException("Migration 008 is not embedded in Corrin.Api.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        return sql[sql.IndexOf("INSERT INTO reorder_requests", StringComparison.Ordinal)..];
    }

    [Fact]
    public async Task BackfillReorderMigration_RerunOnAlreadyBackfilledData_InsertsZeroRows()
    {
        // The first run may add rows for any part at or below its level with no Open or Ordered request. That depends on the
        // live data, so it is rolled back with the transaction. The rerun must add nothing whatever the state is.
        await _db.Database.ExecuteSqlRawAsync(BackfillInsertSql());

        var inserted = await _db.Database.ExecuteSqlRawAsync(BackfillInsertSql());

        Assert.Equal(0, inserted);
    }

    [Fact]
    public async Task BackfillReorderMigration_PartAtLevelWithNoOpenOrOrdered_InsertsOneOpenRow()
    {
        // Guards against the rerun test passing only because the INSERT matches nothing.
        await ClearRequestsAsync(PartOneId);
        await SetStockAsync(PartOneId, 0);

        var inserted = await _db.Database.ExecuteSqlRawAsync(BackfillInsertSql());

        Assert.Equal(1, inserted);
        var created = await _db.ReorderRequests.AsNoTracking().Where(r => r.PartId == PartOneId).ToListAsync();
        var row = Assert.Single(created);
        Assert.Equal("Open", row.Status);
        Assert.Null(row.ExpectedOn);
        Assert.Null(row.OrderedBy);
        Assert.Null(row.ReceivedBy);

        // Running it again after that adds nothing.
        Assert.Equal(0, await _db.Database.ExecuteSqlRawAsync(BackfillInsertSql()));
    }
}
