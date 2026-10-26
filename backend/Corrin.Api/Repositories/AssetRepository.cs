using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface IAssetRepository
{
    Task<List<Asset>> ListAsync(int? siteId, string? assetType);
    Task<Asset?> GetAsync(int id);
    Task<Asset> AddAsync(Asset asset);
    Task UpdateAsync(Asset asset);
    Task<List<Job>> GetJobHistoryAsync(int assetId);
}

public class AssetRepository : IAssetRepository
{
    private readonly CorrinDbContext _db;
    public AssetRepository(CorrinDbContext db) => _db = db;

    public Task<List<Asset>> ListAsync(int? siteId, string? assetType)
    {
        var q = _db.Assets.AsNoTracking().AsQueryable();
        if (siteId.HasValue) q = q.Where(a => a.SiteId == siteId);
        if (!string.IsNullOrEmpty(assetType)) q = q.Where(a => a.AssetType == assetType);
        return q.OrderBy(a => a.Id).Take(500).ToListAsync();
    }

    public Task<Asset?> GetAsync(int id) => _db.Assets.FirstOrDefaultAsync(a => a.Id == id);

    public async Task<Asset> AddAsync(Asset asset)
    {
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();
        return asset;
    }

    public Task UpdateAsync(Asset asset) => _db.SaveChangesAsync();

    public Task<List<Job>> GetJobHistoryAsync(int assetId) =>
        _db.Jobs.AsNoTracking().Where(j => j.AssetId == assetId).OrderByDescending(j => j.ScheduledStart).ToListAsync();
}
