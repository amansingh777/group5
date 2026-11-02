using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface IReferenceRepository
{
    Task<List<Region>> GetRegionsAsync();
    Task<List<Skill>> GetSkillsAsync();
    Task<List<JobType>> GetJobTypesAsync();
    Task<JobType?> GetJobTypeAsync(int id);
    Task<bool> RegionExistsAsync(int id);
}

public class ReferenceRepository : IReferenceRepository
{
    private readonly CorrinDbContext _db;
    public ReferenceRepository(CorrinDbContext db) => _db = db;

    public Task<List<Region>> GetRegionsAsync() => _db.Regions.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
    public Task<List<Skill>> GetSkillsAsync() => _db.Skills.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
    public Task<List<JobType>> GetJobTypesAsync() => _db.JobTypes.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
    public Task<JobType?> GetJobTypeAsync(int id) => _db.JobTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
    public Task<bool> RegionExistsAsync(int id) => _db.Regions.AnyAsync(r => r.Id == id);
}
