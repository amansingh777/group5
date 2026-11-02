using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface IPartRepository
{
    Task<List<Part>> ListAsync(string? search);
    Task<List<Part>> GetLowStockAsync();
    Task<Part?> GetAsync(int id);
    Task<Part> AddAsync(Part part);
    Task UpdateAsync(Part part);
    Task<int> TryDecrementStockAsync(int partId, int quantity);
    // Fresh (untracked) read of stock and reorder level, so it sees changes made by ExecuteUpdate in the same transaction.
    Task<(int StockQty, int ReorderLevel)?> GetStockLevelAsync(int partId);
    // Atomic increment (used when a reorder is received): returns rows affected.
    Task<int> IncrementStockAsync(int partId, int quantity);
}

public class PartRepository : IPartRepository
{
    private readonly CorrinDbContext _db;
    public PartRepository(CorrinDbContext db) => _db = db;

    public Task<List<Part>> ListAsync(string? search)
    {
        var q = _db.Parts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));
        return q.OrderBy(p => p.Name).ToListAsync();
    }

    public Task<List<Part>> GetLowStockAsync() =>
        _db.Parts.AsNoTracking().Where(p => p.StockQty <= p.ReorderLevel).OrderBy(p => p.StockQty).ToListAsync();

    public Task<Part?> GetAsync(int id) => _db.Parts.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Part> AddAsync(Part part)
    {
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    public Task UpdateAsync(Part part) => _db.SaveChangesAsync();

    // Conditional, atomic decrement: returns rows affected (0 when stock is insufficient or the part is missing).
    public Task<int> TryDecrementStockAsync(int partId, int quantity) =>
        _db.Parts.Where(p => p.Id == partId && p.StockQty >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQty, p => p.StockQty - quantity));

    public async Task<(int StockQty, int ReorderLevel)?> GetStockLevelAsync(int partId)
    {
        var row = await _db.Parts.AsNoTracking().Where(p => p.Id == partId)
            .Select(p => new { p.StockQty, p.ReorderLevel }).FirstOrDefaultAsync();
        return row is null ? null : (row.StockQty, row.ReorderLevel);
    }

    public Task<int> IncrementStockAsync(int partId, int quantity) =>
        _db.Parts.Where(p => p.Id == partId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQty, p => p.StockQty + quantity));
}
