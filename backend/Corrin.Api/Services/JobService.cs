using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IJobService
{
    Task<PagedResult<JobDto>> ListAsync(JobFilter filter, int page, int pageSize);
    Task<JobDto> GetAsync(int id);
    Task<JobDto> CreateAsync(JobCreateRequest request);
    Task<JobDto> UpdateAsync(int id, JobUpdateRequest request);
    Task<JobDto> CancelAsync(int id, JobCancelRequest request);
    Task<IReadOnlyList<JobNoteDto>> GetNotesAsync(int id);
    Task<JobNoteDto> AddNoteAsync(int id, JobNoteRequest request);
    Task<IReadOnlyList<JobPartDto>> GetPartsAsync(int id);
    Task<JobPartDto> AddPartAsync(int id, JobPartRequest request);
}

public class JobService : IJobService
{
    private readonly IJobRepository _jobs;
    private readonly ICustomerRepository _customers;
    private readonly ISiteRepository _sites;
    private readonly IEngineerRepository _engineers;
    private readonly IPartRepository _parts;

    public JobService(IJobRepository jobs, ICustomerRepository customers, ISiteRepository sites, IEngineerRepository engineers, IPartRepository parts)
    {
        _jobs = jobs;
        _customers = customers;
        _sites = sites;
        _engineers = engineers;
        _parts = parts;
    }

    public async Task<PagedResult<JobDto>> ListAsync(JobFilter filter, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (jobs, total) = await _jobs.ListAsync(filter, page, pageSize);
        var items = new List<JobDto>();
        foreach (var job in jobs)
            items.Add(await ToDtoAsync(job));
        return new PagedResult<JobDto>(items, total, page, pageSize);
    }

    public async Task<JobDto> GetAsync(int id) =>
        await ToDtoAsync(await _jobs.GetReadOnlyAsync(id) ?? throw new NotFoundException("Job", id));

    public async Task<JobDto> CreateAsync(JobCreateRequest request)
    {
        var site = await _sites.GetReadOnlyAsync(request.SiteId) ?? throw new BusinessRuleException("Site does not exist.");
        if (site.CustomerId != request.CustomerId) throw new BusinessRuleException("The site does not belong to that customer.");
        if (request.EngineerId.HasValue && await _engineers.GetReadOnlyAsync(request.EngineerId.Value) is null)
            throw new BusinessRuleException("Engineer does not exist.");

        var now = DateTime.UtcNow;
        var job = new Job
        {
            JobRef = $"J-{Random.Shared.Next(1_000_000, 9_999_999)}",
            CustomerId = request.CustomerId,
            SiteId = request.SiteId,
            AssetId = request.AssetId,
            JobTypeId = request.JobTypeId,
            EngineerId = request.EngineerId,
            RegionId = site.RegionId,
            Priority = request.Priority,
            Status = request.EngineerId.HasValue && request.ScheduledStart.HasValue ? "Scheduled" : "Requested",
            ScheduledStart = request.ScheduledStart,
            ScheduledEnd = request.ScheduledEnd,
            SlaDueAt = now.AddHours(request.Priority switch { "P1" => 4, "P2" => 24, _ => 72 }),
            Description = request.Description,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _jobs.AddAsync(job);
        return await ToDtoAsync(job);
    }

    public async Task<JobDto> UpdateAsync(int id, JobUpdateRequest request)
    {
        var job = await _jobs.GetAsync(id) ?? throw new NotFoundException("Job", id);
        if (request.EngineerId.HasValue && await _engineers.GetReadOnlyAsync(request.EngineerId.Value) is null)
            throw new BusinessRuleException("Engineer does not exist.");
        job.EngineerId = request.EngineerId;
        job.Priority = request.Priority;
        job.Status = request.Status;
        job.ScheduledStart = request.ScheduledStart;
        job.ScheduledEnd = request.ScheduledEnd;
        job.ActualStart = request.ActualStart;
        job.ActualEnd = request.ActualEnd;
        job.Outcome = request.Outcome;
        job.FirstTimeFix = request.Outcome is null ? null : request.Outcome == "Fixed";
        job.Description = request.Description;
        job.UpdatedAt = DateTime.UtcNow;
        await _jobs.UpdateAsync(job);
        return await ToDtoAsync(job);
    }

    public async Task<JobDto> CancelAsync(int id, JobCancelRequest request)
    {
        var job = await _jobs.GetAsync(id) ?? throw new NotFoundException("Job", id);
        if (job.Status == "Completed") throw new ConflictException("A completed job cannot be cancelled.");
        job.Status = "Cancelled";
        job.UpdatedAt = DateTime.UtcNow;
        await _jobs.UpdateAsync(job);
        await _jobs.AddNoteAsync(new JobNote { JobId = id, Author = "system", Note = $"Cancelled: {request.Reason}", CreatedAt = DateTime.UtcNow });
        return await ToDtoAsync(job);
    }

    public async Task<IReadOnlyList<JobNoteDto>> GetNotesAsync(int id)
    {
        if (await _jobs.GetReadOnlyAsync(id) is null) throw new NotFoundException("Job", id);
        return (await _jobs.GetNotesAsync(id)).Select(n => n.ToDto()).ToList();
    }

    public async Task<JobNoteDto> AddNoteAsync(int id, JobNoteRequest request)
    {
        if (await _jobs.GetReadOnlyAsync(id) is null) throw new NotFoundException("Job", id);
        var note = new JobNote { JobId = id, Author = request.Author, Note = request.Note, CreatedAt = DateTime.UtcNow };
        return (await _jobs.AddNoteAsync(note)).ToDto();
    }

    public async Task<IReadOnlyList<JobPartDto>> GetPartsAsync(int id)
    {
        if (await _jobs.GetReadOnlyAsync(id) is null) throw new NotFoundException("Job", id);
        return (await _jobs.GetPartsAsync(id)).Select(p => p.ToDto()).ToList();
    }

    public async Task<JobPartDto> AddPartAsync(int id, JobPartRequest request)
    {
        var part = await _parts.GetAsync(request.PartId) ?? throw new NotFoundException("Part", request.PartId);
        var job = await _jobs.GetReadOnlyAsync(id) ?? throw new NotFoundException("Job", id);
        if (job.Status is "Cancelled") throw new BusinessRuleException("Parts cannot be added to a cancelled job.");
        if (part.StockQty < request.Quantity)
            throw new ConflictException($"Only {part.StockQty} of {part.Sku} in stock.");

        var jobPart = await _jobs.AddPartAsync(new JobPart { JobId = id, PartId = part.Id, Quantity = request.Quantity });
        part.StockQty -= request.Quantity;
        await _parts.UpdateAsync(part);
        return jobPart.ToDto();
    }

    private async Task<JobDto> ToDtoAsync(Job job)
    {
        var customer = await _customers.GetAsync(job.CustomerId);
        var site = await _sites.GetReadOnlyAsync(job.SiteId);
        var engineer = job.EngineerId.HasValue ? await _engineers.GetReadOnlyAsync(job.EngineerId.Value) : null;
        return job.ToDto(customer?.Name ?? "", site?.Name ?? "", engineer?.FullName);
    }
}
