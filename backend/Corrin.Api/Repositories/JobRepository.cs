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
    Task<T> InTransactionAsync<T>(Func<Task<T>> work);
    Task<List<(int EngineerId, int Count)>> CountScheduledByEngineerAsync(IEnumerable<int> engineerIds, DateTime from, DateTime to);
    Task<List<(int EngineerId, string Postcode)>> GetEngineerSitePostcodesAsync(IEnumerable<int> engineerIds);
    // Jobs booked to these engineers whose [ScheduledStart, ScheduledEnd) overlaps [from, to), excluding final statuses.
    Task<List<Job>> GetActiveBookingsAsync(IEnumerable<int> engineerIds, DateTime from, DateTime to);
    // Row-locks the job until the surrounding transaction ends; call inside InTransactionAsync before any plain read of the job.
    Task LockForUpdateAsync(int id);
    // True when the job has at least one Visit row (work-order jobs). Legacy jobs have none.
    Task<bool> HasVisitsAsync(int jobId);
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

    public async Task<List<(int EngineerId, int Count)>> CountScheduledByEngineerAsync(IEnumerable<int> engineerIds, DateTime from, DateTime to)
    {
        var ids = engineerIds.ToList();
        var rows = await _db.Jobs.AsNoTracking()
            .Where(j => j.EngineerId.HasValue && ids.Contains(j.EngineerId.Value) && j.ScheduledStart >= from && j.ScheduledStart < to && j.Status != "Cancelled")
            .GroupBy(j => j.EngineerId)
            .Select(g => new { EngineerId = g.Key, Count = g.Count() })
            .ToListAsync();
        return rows.Where(r => r.EngineerId.HasValue).Select(r => (r.EngineerId!.Value, r.Count)).ToList();
    }

    public async Task<List<Job>> GetActiveBookingsAsync(IEnumerable<int> engineerIds, DateTime from, DateTime to)
    {
        var ids = engineerIds.ToList();
        string[] finalStatuses = { "Cancelled", "Completed", "NotCompleted" };
        return await _db.Jobs.AsNoTracking()
            .Where(j => j.EngineerId.HasValue && ids.Contains(j.EngineerId.Value)
                && j.ScheduledStart != null && j.ScheduledStart < to
                && j.ScheduledEnd != null && j.ScheduledEnd > from
                && !finalStatuses.Contains(j.Status))
            .OrderBy(j => j.ScheduledStart)
            .ToListAsync();
    }

    // Postcodes of the sites an engineer has been assigned jobs at; used as the engineer's local area.
    public async Task<List<(int EngineerId, string Postcode)>> GetEngineerSitePostcodesAsync(IEnumerable<int> engineerIds)
    {
        var ids = engineerIds.ToList();
        var rows = await _db.Jobs.AsNoTracking()
            .Where(j => j.EngineerId.HasValue && ids.Contains(j.EngineerId.Value))
            .Join(_db.Sites.AsNoTracking(), j => j.SiteId, s => s.Id, (j, s) => new { j.EngineerId, s.Postcode })
            .Distinct()
            .ToListAsync();
        return rows.Where(r => r.EngineerId.HasValue).Select(r => (r.EngineerId!.Value, r.Postcode)).ToList();
    }

    public async Task LockForUpdateAsync(int id) =>
        await _db.Jobs.FromSqlInterpolated($"SELECT * FROM jobs WHERE id = {id} FOR UPDATE").AsNoTracking().ToListAsync();

    public Task<bool> HasVisitsAsync(int jobId) => _db.Visits.AnyAsync(v => v.JobId == jobId);

    // Runs work in one DB transaction on the shared DbContext; an exception rolls it back.
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
    {
        // A nested call joins the transaction already open on the shared DbContext (e.g. AddPartAsync inside a visit completion).
        if (_db.Database.CurrentTransaction is not null) return await work();
        await using var tx = await _db.Database.BeginTransactionAsync();
        var result = await work();
        await tx.CommitAsync();
        return result;
    }

    public async Task<List<(string Status, int Count)>> CountByStatusAsync(int? regionId)
    {
        var q = _db.Jobs.AsNoTracking().AsQueryable();
        if (regionId.HasValue) q = q.Where(j => j.RegionId == regionId);
        var rows = await q.GroupBy(j => j.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        return rows.Select(r => (r.Status, r.Count)).ToList();
    }
}
