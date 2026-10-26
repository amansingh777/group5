using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface IEngineerRepository
{
    Task<List<Engineer>> ListAsync(int? regionId, int? skillId, bool? active);
    Task<Engineer?> GetAsync(int id);
    Task<Engineer?> GetReadOnlyAsync(int id);
    Task<Engineer> AddAsync(Engineer engineer);
    Task UpdateAsync(Engineer engineer);
    Task SetSkillsAsync(int engineerId, IEnumerable<int> skillIds);
    Task<int> CountAsync();
}

public class EngineerRepository : IEngineerRepository
{
    private readonly CorrinDbContext _db;
    public EngineerRepository(CorrinDbContext db) => _db = db;

    public Task<List<Engineer>> ListAsync(int? regionId, int? skillId, bool? active)
    {
        var q = _db.Engineers.AsNoTracking().Include(e => e.Skills).AsQueryable();
        if (regionId.HasValue) q = q.Where(e => e.RegionId == regionId);
        if (skillId.HasValue) q = q.Where(e => e.Skills.Any(s => s.SkillId == skillId));
        if (active.HasValue) q = q.Where(e => e.Active == active);
        return q.OrderBy(e => e.FullName).ToListAsync();
    }

    public Task<Engineer?> GetAsync(int id) => _db.Engineers.Include(e => e.Skills).FirstOrDefaultAsync(e => e.Id == id);
    public Task<Engineer?> GetReadOnlyAsync(int id) => _db.Engineers.AsNoTracking().Include(e => e.Skills).FirstOrDefaultAsync(e => e.Id == id);

    public async Task<Engineer> AddAsync(Engineer engineer)
    {
        _db.Engineers.Add(engineer);
        await _db.SaveChangesAsync();
        return engineer;
    }

    public Task UpdateAsync(Engineer engineer) => _db.SaveChangesAsync();

    public async Task SetSkillsAsync(int engineerId, IEnumerable<int> skillIds)
    {
        var existing = await _db.EngineerSkills.Where(s => s.EngineerId == engineerId).ToListAsync();
        _db.EngineerSkills.RemoveRange(existing);
        foreach (var id in skillIds.Distinct())
            _db.EngineerSkills.Add(new EngineerSkill { EngineerId = engineerId, SkillId = id });
        await _db.SaveChangesAsync();
    }

    public Task<int> CountAsync() => _db.Engineers.CountAsync();
}
