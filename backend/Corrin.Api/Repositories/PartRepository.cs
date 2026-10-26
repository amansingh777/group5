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
}
