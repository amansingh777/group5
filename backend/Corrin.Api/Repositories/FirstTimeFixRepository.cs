using Corrin.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public record FirstTimeFixCohortRow(int RegionId, string RegionName, int JobTypeId, string JobTypeName, int JobsAttended, int FixedFirstTime);

public interface IFirstTimeFixRepository
{
    // Cohort = jobs (not Cancelled) whose visit 1 has an outcome and whose visit 1 ActualEnd is in [from, to).
    // Fixed = visit 1 outcome is Fixed and the job has no visit numbered above 1.
    Task<List<FirstTimeFixCohortRow>> GetCohortCountsAsync(DateTime from, DateTime to);
}

public class FirstTimeFixRepository : IFirstTimeFixRepository
{
    private readonly CorrinDbContext _db;
    public FirstTimeFixRepository(CorrinDbContext db) => _db = db;

    public async Task<List<FirstTimeFixCohortRow>> GetCohortCountsAsync(DateTime from, DateTime to)
    {
        var groups = await _db.Visits.AsNoTracking()
            .Where(v => v.VisitNo == 1 && v.Outcome != null && v.ActualEnd >= from && v.ActualEnd < to)
            .Join(_db.Jobs.AsNoTracking().Where(j => j.Status != "Cancelled"),
                v => v.JobId, j => j.Id, (v, j) => new
                {
                    j.RegionId,
                    j.JobTypeId,
                    Fixed = v.Outcome == "Fixed" && !_db.Visits.Any(later => later.JobId == v.JobId && later.VisitNo > 1)
                })
            .GroupBy(x => new { x.RegionId, x.JobTypeId })
            .Select(g => new
            {
                g.Key.RegionId,
                g.Key.JobTypeId,
                Attended = g.Count(),
                Fixed = g.Sum(x => x.Fixed ? 1 : 0)
            })
            .ToListAsync();

        var regions = await _db.Regions.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name);
        var jobTypes = await _db.JobTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);

        return groups
            .Select(g => new FirstTimeFixCohortRow(g.RegionId, regions[g.RegionId], g.JobTypeId, jobTypes[g.JobTypeId], g.Attended, g.Fixed))
            .ToList();
    }
}
