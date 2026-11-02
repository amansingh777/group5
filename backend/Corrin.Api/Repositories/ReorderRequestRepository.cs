using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public record ReorderRequestFilter(string? Status, int? PartId);

// Reorder request joined to its part (one query, no per-row lookups).
public record ReorderRequestRow(int Id, int PartId, string PartSku, string PartName, int StockQty, int ReorderLevel,
    string Status, DateOnly? ExpectedOn, string? OrderedBy, string? ReceivedBy, DateTime CreatedAt, DateTime UpdatedAt);

public interface IReorderRequestRepository
{
    Task<(List<ReorderRequestRow> Items, int Total)> ListAsync(ReorderRequestFilter filter, int page, int pageSize);
    Task<ReorderRequestRow?> GetRowAsync(int id);
    Task<ReorderRequest?> GetAsync(int id);
    Task<bool> HasOpenOrOrderedAsync(int partId);
    Task<ReorderRequest> AddAsync(ReorderRequest request);
    // Conditional transitions: return rows affected (0 when the request is not in the expected status).
    Task<int> TryMarkOrderedAsync(int id, DateOnly expectedOn, string userName, DateTime now);
    Task<int> TryMarkReceivedAsync(int id, string userName, DateTime now);
    Task<T> InTransactionAsync<T>(Func<Task<T>> work);
}

public class ReorderRequestRepository : IReorderRequestRepository
{
    private readonly CorrinDbContext _db;
    public ReorderRequestRepository(CorrinDbContext db) => _db = db;

    // Filters, ordering and paging run on the join shape (anonymous type). The ReorderRequestRow projection is
    // applied last, so EF never has to translate a Where/OrderBy over a constructed record.
    public async Task<(List<ReorderRequestRow> Items, int Total)> ListAsync(ReorderRequestFilter f, int page, int pageSize)
    {
        var q = _db.ReorderRequests.AsNoTracking()
            .Join(_db.Parts.AsNoTracking(), r => r.PartId, p => p.Id, (r, p) => new { R = r, P = p });
        if (!string.IsNullOrEmpty(f.Status)) q = q.Where(x => x.R.Status == f.Status);
        if (f.PartId.HasValue) q = q.Where(x => x.R.PartId == f.PartId.Value);
        var total = await q.CountAsync();
        var items = await q.OrderBy(x => x.R.CreatedAt).ThenBy(x => x.R.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ReorderRequestRow(x.R.Id, x.R.PartId, x.P.Sku, x.P.Name, x.P.StockQty, x.P.ReorderLevel,
                x.R.Status, x.R.ExpectedOn, x.R.OrderedBy, x.R.ReceivedBy, x.R.CreatedAt, x.R.UpdatedAt))
            .ToListAsync();
        return (items, total);
    }

    public async Task<ReorderRequestRow?> GetRowAsync(int id)
    {
        var q = _db.ReorderRequests.AsNoTracking().Where(r => r.Id == id)
            .Join(_db.Parts.AsNoTracking(), r => r.PartId, p => p.Id, (r, p) => new { R = r, P = p });
        return await q.Select(x => new ReorderRequestRow(x.R.Id, x.R.PartId, x.P.Sku, x.P.Name, x.P.StockQty, x.P.ReorderLevel,
            x.R.Status, x.R.ExpectedOn, x.R.OrderedBy, x.R.ReceivedBy, x.R.CreatedAt, x.R.UpdatedAt))
            .FirstOrDefaultAsync();
    }

    public Task<ReorderRequest?> GetAsync(int id) => _db.ReorderRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);

    public Task<bool> HasOpenOrOrderedAsync(int partId) =>
        _db.ReorderRequests.AsNoTracking().AnyAsync(r => r.PartId == partId && (r.Status == "Open" || r.Status == "Ordered"));

    public async Task<ReorderRequest> AddAsync(ReorderRequest request)
    {
        _db.ReorderRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    public Task<int> TryMarkOrderedAsync(int id, DateOnly expectedOn, string userName, DateTime now) =>
        _db.ReorderRequests.Where(r => r.Id == id && r.Status == "Open")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "Ordered")
                .SetProperty(r => r.ExpectedOn, (DateOnly?)expectedOn)
                .SetProperty(r => r.OrderedBy, (string?)userName)
                .SetProperty(r => r.UpdatedAt, now));

    public Task<int> TryMarkReceivedAsync(int id, string userName, DateTime now) =>
        _db.ReorderRequests.Where(r => r.Id == id && r.Status == "Ordered")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "Received")
                .SetProperty(r => r.ReceivedBy, (string?)userName)
                .SetProperty(r => r.UpdatedAt, now));

    // Same pattern as JobRepository.InTransactionAsync: joins an open transaction on the shared DbContext.
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
    {
        if (_db.Database.CurrentTransaction is not null) return await work();
        await using var tx = await _db.Database.BeginTransactionAsync();
        var result = await work();
        await tx.CommitAsync();
        return result;
    }
}
