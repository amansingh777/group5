using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public record JobFilter(int? RegionId, int? EngineerId, string? Status, DateTime? From, DateTime? To);

public interface IJobRepository
{
    Task<(List<Job> Items, int Total)> ListAsync(JobFilter filter, int page, int pageSize);
    Task<Job?> GetAsync(int id);
    Task<Job?> GetReadOnlyAsync(int id);
    Task<Job> AddAsync(Job job);
    Task UpdateAsync(Job job);
    Task<List<Job>> GetForEngineerAsync(int engineerId, DateTime from, DateTime to);
    Task<List<Job>> GetCompletedBetweenAsync(DateTime from, DateTime to, int? regionId);
    Task<List<JobNote>> GetNotesAsync(int jobId);
    Task<JobNote> AddNoteAsync(JobNote note);
    Task<List<JobPart>> GetPartsAsync(int jobId);
    Task<JobPart> AddPartAsync(JobPart jobPart);
    Task<List<(string Status, int Count)>> CountByStatusAsync(int? regionId);
}

public class JobRepository : IJobRepository
{
    private readonly CorrinDbContext _db;
    public JobRepository(CorrinDbContext db) => _db = db;

    public async Task<(List<Job> Items, int Total)> ListAsync(JobFilter f, int page, int pageSize)
    {
        var q = _db.Jobs.AsNoTracking().AsQueryable();
        if (f.RegionId.HasValue) q = q.Where(j => j.RegionId == f.RegionId);
        if (f.EngineerId.HasValue) q = q.Where(j => j.EngineerId == f.EngineerId);
        if (!string.IsNullOrEmpty(f.Status)) q = q.Where(j => j.Status == f.Status);
        if (f.From.HasValue) q = q.Where(j => j.ScheduledStart >= f.From);
        if (f.To.HasValue) q = q.Where(j => j.ScheduledStart < f.To);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(j => j.ScheduledStart).ThenBy(j => j.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<Job?> GetAsync(int id) => _db.Jobs.FirstOrDefaultAsync(j => j.Id == id);
    public Task<Job?> GetReadOnlyAsync(int id) => _db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);

    public async Task<Job> AddAsync(Job job)
    {
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    public Task UpdateAsync(Job job) => _db.SaveChangesAsync();

    public Task<List<Job>> GetForEngineerAsync(int engineerId, DateTime from, DateTime to) =>
        _db.Jobs.AsNoTracking().Where(j => j.EngineerId == engineerId && j.ScheduledStart >= from && j.ScheduledStart < to)
            .OrderBy(j => j.ScheduledStart).ToListAsync();

    public Task<List<Job>> GetCompletedBetweenAsync(DateTime from, DateTime to, int? regionId)
    {
        var q = _db.Jobs.AsNoTracking().Where(j => j.Status == "Completed" && j.ActualStart >= from && j.ActualStart < to && j.EngineerId != null);
        if (regionId.HasValue) q = q.Where(j => j.RegionId == regionId);
        return q.ToListAsync();
    }

    public Task<List<JobNote>> GetNotesAsync(int jobId) =>
        _db.JobNotes.AsNoTracking().Where(n => n.JobId == jobId).OrderBy(n => n.CreatedAt).ToListAsync();

    public async Task<JobNote> AddNoteAsync(JobNote note)
    {
        _db.JobNotes.Add(note);
        await _db.SaveChangesAsync();
        return note;
    }

    public Task<List<JobPart>> GetPartsAsync(int jobId) => _db.JobParts.AsNoTracking().Where(p => p.JobId == jobId).ToListAsync();

    public async Task<JobPart> AddPartAsync(JobPart jobPart)
    {
        _db.JobParts.Add(jobPart);
        await _db.SaveChangesAsync();
        return jobPart;
    }

    public async Task<List<(string Status, int Count)>> CountByStatusAsync(int? regionId)
    {
        var q = _db.Jobs.AsNoTracking().AsQueryable();
        if (regionId.HasValue) q = q.Where(j => j.RegionId == regionId);
        var rows = await q.GroupBy(j => j.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        return rows.Select(r => (r.Status, r.Count)).ToList();
    }
}
