using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public record WorkOrderFilter(int? RegionId, int? EngineerId, string? Status, string? Priority);

// Flat read model: the job plus the names it is shown with, from one joined query.
public record WorkOrderRow(int Id, string JobRef, int RegionId, string CustomerName, string SiteName, string SitePostcode,
    string? AssetType, string? AssetMake, string? AssetModel, string JobType, string RequiredSkill, string Priority, string Status,
    int? EngineerId, string? EngineerName, DateTime? ScheduledStart, DateTime? ScheduledEnd, DateTime? SlaDueAt, DateTime CreatedAt,
    string? Description, string? Outcome);

public record BoardJobRow(int Id, string JobRef, string Priority, string Status, int? EngineerId, DateTime? ScheduledStart,
    DateTime? ScheduledEnd, DateTime CreatedAt, DateTime? SlaDueAt);

public interface IWorkOrderReadRepository
{
    Task<(List<WorkOrderRow> Items, int Total)> ListAsync(WorkOrderFilter filter, int page, int pageSize);
    Task<WorkOrderRow?> GetAsync(int id);
    Task<List<JobNote>> NotesForJobsAsync(IEnumerable<int> jobIds);
    Task<List<JobPart>> PartsForJobsAsync(IEnumerable<int> jobIds);
    // Non-cancelled jobs booked to these engineers with ScheduledStart in [from, to).
    Task<List<BoardJobRow>> ScheduledForEngineersAsync(IEnumerable<int> engineerIds, DateTime from, DateTime to);
    // Allocated with no time, plus Requested and Triaged.
    Task<List<BoardJobRow>> UnscheduledAsync(int? regionId);
    // Jobs that have a displacement note (R13). Only the note marks a rebook.
    Task<List<int>> DisplacedJobIdsAsync(IEnumerable<int> jobIds);
}

public class WorkOrderReadRepository : IWorkOrderReadRepository
{
    private readonly CorrinDbContext _db;
    public WorkOrderReadRepository(CorrinDbContext db) => _db = db;

    public async Task<(List<WorkOrderRow> Items, int Total)> ListAsync(WorkOrderFilter f, int page, int pageSize)
    {
        var jobs = Filter(_db.Jobs.AsNoTracking(), f);
        var total = await jobs.CountAsync();
        var items = await Project(jobs).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<WorkOrderRow?> GetAsync(int id) =>
        Project(_db.Jobs.AsNoTracking().Where(j => j.Id == id)).FirstOrDefaultAsync();

    public Task<List<JobNote>> NotesForJobsAsync(IEnumerable<int> jobIds)
    {
        var ids = jobIds.ToList();
        return _db.JobNotes.AsNoTracking().Where(n => ids.Contains(n.JobId)).OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).ToListAsync();
    }

    public Task<List<JobPart>> PartsForJobsAsync(IEnumerable<int> jobIds)
    {
        var ids = jobIds.ToList();
        return _db.JobParts.AsNoTracking().Where(p => ids.Contains(p.JobId)).OrderBy(p => p.Id).ToListAsync();
    }

    public Task<List<BoardJobRow>> ScheduledForEngineersAsync(IEnumerable<int> engineerIds, DateTime from, DateTime to)
    {
        var ids = engineerIds.ToList();
        return _db.Jobs.AsNoTracking()
            .Where(j => j.EngineerId.HasValue && ids.Contains(j.EngineerId.Value)
                && j.ScheduledStart != null && j.ScheduledStart >= from && j.ScheduledStart < to
                && j.Status != WorkOrderStatus.Cancelled)
            .OrderBy(j => j.ScheduledStart).ThenBy(j => j.Id)
            .Select(j => new BoardJobRow(j.Id, j.JobRef, j.Priority, j.Status, j.EngineerId, j.ScheduledStart, j.ScheduledEnd, j.CreatedAt, j.SlaDueAt))
            .ToListAsync();
    }

    public Task<List<BoardJobRow>> UnscheduledAsync(int? regionId)
    {
        var q = _db.Jobs.AsNoTracking().Where(j => (j.Status == WorkOrderStatus.Allocated && j.ScheduledStart == null)
            || j.Status == WorkOrderStatus.Requested || j.Status == WorkOrderStatus.Triaged);
        if (regionId.HasValue) q = q.Where(j => j.RegionId == regionId.Value);
        return q.OrderBy(j => j.SlaDueAt == null).ThenBy(j => j.SlaDueAt).ThenBy(j => j.Id)
            .Select(j => new BoardJobRow(j.Id, j.JobRef, j.Priority, j.Status, j.EngineerId, j.ScheduledStart, j.ScheduledEnd, j.CreatedAt, j.SlaDueAt))
            .ToListAsync();
    }

    public Task<List<int>> DisplacedJobIdsAsync(IEnumerable<int> jobIds)
    {
        var ids = jobIds.ToList();
        return _db.JobNotes.AsNoTracking()
            .Where(n => ids.Contains(n.JobId) && n.Note.StartsWith(WorkOrderNotes.DisplacedByP1NotePrefix))
            .Select(n => n.JobId).Distinct().ToListAsync();
    }

    private static IQueryable<Job> Filter(IQueryable<Job> jobs, WorkOrderFilter f)
    {
        if (f.RegionId.HasValue) jobs = jobs.Where(j => j.RegionId == f.RegionId.Value);
        if (f.EngineerId.HasValue) jobs = jobs.Where(j => j.EngineerId == f.EngineerId.Value);
        if (!string.IsNullOrEmpty(f.Status)) jobs = jobs.Where(j => j.Status == f.Status);
        if (!string.IsNullOrEmpty(f.Priority)) jobs = jobs.Where(j => j.Priority == f.Priority);
        return jobs;
    }

    // Customer, site, job type and skill are inner joins; asset and engineer are optional (left joins).
    private IQueryable<WorkOrderRow> Project(IQueryable<Job> jobs) =>
        from j in jobs
        join c in _db.Customers.AsNoTracking() on j.CustomerId equals c.Id
        join s in _db.Sites.AsNoTracking() on j.SiteId equals s.Id
        join jt in _db.JobTypes.AsNoTracking() on j.JobTypeId equals jt.Id
        join sk in _db.Skills.AsNoTracking() on jt.RequiredSkillId equals sk.Id
        join a0 in _db.Assets.AsNoTracking() on j.AssetId equals a0.Id into assets
        from a in assets.DefaultIfEmpty()
        join e0 in _db.Engineers.AsNoTracking() on j.EngineerId equals e0.Id into engineers
        from e in engineers.DefaultIfEmpty()
        orderby j.SlaDueAt == null, j.SlaDueAt, j.Id
        select new WorkOrderRow(j.Id, j.JobRef, j.RegionId, c.Name, s.Name, s.Postcode,
            a.AssetType, a.Make, a.Model, jt.Name, sk.Name, j.Priority, j.Status,
            j.EngineerId, e.FullName, j.ScheduledStart, j.ScheduledEnd, j.SlaDueAt, j.CreatedAt,
            j.Description, j.Outcome);
}
