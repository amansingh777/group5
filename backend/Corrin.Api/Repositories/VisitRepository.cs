using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface IVisitRepository
{
    // Tracked, ordered by visit number; used when changing visit state.
    Task<List<Visit>> GetForJobAsync(int jobId);
    Task<List<Visit>> ListForJobAsync(int jobId);
    Task<Visit> AddAsync(Visit visit);
}

public class VisitRepository : IVisitRepository
{
    private readonly CorrinDbContext _db;
    public VisitRepository(CorrinDbContext db) => _db = db;

    public Task<List<Visit>> GetForJobAsync(int jobId) =>
        _db.Visits.Where(v => v.JobId == jobId).OrderBy(v => v.VisitNo).ToListAsync();

    public Task<List<Visit>> ListForJobAsync(int jobId) =>
        _db.Visits.AsNoTracking().Where(v => v.JobId == jobId).OrderBy(v => v.VisitNo).ToListAsync();

    public async Task<Visit> AddAsync(Visit visit)
    {
        _db.Visits.Add(visit);
        await _db.SaveChangesAsync();
        return visit;
    }
}
