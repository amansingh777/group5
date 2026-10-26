using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface ISiteRepository
{
    Task<List<Site>> ListAsync(int? regionId, int? customerId);
    Task<Site?> GetAsync(int id);
    Task<Site?> GetReadOnlyAsync(int id);
    Task<Site> AddAsync(Site site);
    Task UpdateAsync(Site site);
    Task<List<Asset>> GetAssetsAsync(int siteId);
}

public class SiteRepository : ISiteRepository
{
    private readonly CorrinDbContext _db;
    public SiteRepository(CorrinDbContext db) => _db = db;

    public Task<List<Site>> ListAsync(int? regionId, int? customerId)
    {
        var q = _db.Sites.AsNoTracking().AsQueryable();
        if (regionId.HasValue) q = q.Where(s => s.RegionId == regionId);
        if (customerId.HasValue) q = q.Where(s => s.CustomerId == customerId);
        return q.OrderBy(s => s.Name).Take(500).ToListAsync();
    }

    public Task<Site?> GetAsync(int id) => _db.Sites.FirstOrDefaultAsync(s => s.Id == id);
    public Task<Site?> GetReadOnlyAsync(int id) => _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

    public async Task<Site> AddAsync(Site site)
    {
        _db.Sites.Add(site);
        await _db.SaveChangesAsync();
        return site;
    }

    public Task UpdateAsync(Site site) => _db.SaveChangesAsync();

    public Task<List<Asset>> GetAssetsAsync(int siteId) =>
        _db.Assets.AsNoTracking().Where(a => a.SiteId == siteId).OrderBy(a => a.AssetType).ToListAsync();
}
