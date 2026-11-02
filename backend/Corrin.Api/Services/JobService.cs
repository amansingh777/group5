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
    private readonly IReorderService _reorder;
    private readonly IReferenceRepository _reference;
    private readonly IEngineerBookingRules _rules;

    public JobService(IJobRepository jobs, ICustomerRepository customers, ISiteRepository sites, IEngineerRepository engineers, IPartRepository parts,
        IReorderService reorder, IReferenceRepository reference, IEngineerBookingRules rules)
    {
        _jobs = jobs;
        _customers = customers;
        _sites = sites;
        _engineers = engineers;
        _parts = parts;
        _reorder = reorder;
        _reference = reference;
        _rules = rules;
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
        Engineer? engineer = null;
        if (request.EngineerId.HasValue)
        {
            engineer = await _engineers.GetReadOnlyAsync(request.EngineerId.Value) ?? throw new BusinessRuleException("Engineer does not exist.");
            var jobType = await _reference.GetJobTypeAsync(request.JobTypeId) ?? throw new BusinessRuleException("The job type does not exist.");
            _rules.EnsureEngineerSuitable(jobType.RequiredSkillId, engineer, IsBooked(request.EngineerId, request.ScheduledStart, request.ScheduledEnd) ? "scheduled" : "allocated");
        }
        var booked = engineer is not null && request.ScheduledStart.HasValue && request.ScheduledEnd.HasValue;
        var status = booked ? WorkOrderStatus.Scheduled : engineer is not null ? WorkOrderStatus.Allocated : WorkOrderStatus.Requested;

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
            Status = status,
            ScheduledStart = request.ScheduledStart,
            ScheduledEnd = request.ScheduledEnd,
            SlaDueAt = now.AddHours(request.Priority switch { "P1" => 4, "P2" => 24, _ => 72 }),
            Description = request.Description,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _jobs.InTransactionAsync(async () =>
        {
            // The overlap check and the insert share one transaction under the engineer row lock.
            if (booked)
            {
                await _engineers.LockForUpdateAsync(engineer!.Id);
                await EnsureBookingAllowedAsync(engineer.Id, 0, request.Priority, request.ScheduledStart!.Value, request.ScheduledEnd!.Value,
                    "Create the job without a booking, then use POST /api/work-orders/{id}/schedule.");
            }
            await _jobs.AddAsync(job);
            return job;
        });
        return await ToDtoAsync(job);
    }

    public async Task<JobDto> UpdateAsync(int id, JobUpdateRequest request)
    {
        var updated = await _jobs.InTransactionAsync(async () =>
        {
            // Engineer row first, then job row (the order scheduling uses), so the two paths cannot deadlock.
            if (request.EngineerId.HasValue) await _engineers.LockForUpdateAsync(request.EngineerId.Value);
            // The job row is locked before its status or visits are read.
            await _jobs.LockForUpdateAsync(id);
            var job = await _jobs.GetAsync(id) ?? throw new NotFoundException("Job", id);

            // OnSite, Completed and NotCompleted are reached only through the visit endpoints, which write the visit row
            // that the first-time-fix report reads. Leaving the status unchanged is still allowed.
            var statusChanged = request.Status != job.Status;
            if (statusChanged && request.Status is WorkOrderStatus.OnSite or WorkOrderStatus.Completed or WorkOrderStatus.NotCompleted)
                throw new ConflictException($"A work order cannot be moved to {request.Status} through PUT /api/jobs/{job.Id}. " +
                    $"Use POST /api/work-orders/{job.Id}/visits/start and POST /api/work-orders/{job.Id}/visits/complete.");

            // Once a job has visit rows, the visit service owns its status, engineer and times.
            var hasVisits = await _jobs.HasVisitsAsync(id);
            var engineerChanged = request.EngineerId != job.EngineerId;
            var timesChanged = request.ScheduledStart != job.ScheduledStart || request.ScheduledEnd != job.ScheduledEnd;
            var changesBooking = statusChanged || engineerChanged || timesChanged;
            if (hasVisits && changesBooking)
                throw new ConflictException("This job has visits. Change its status, engineer or times through the work-order endpoints (allocate, schedule, visits).");

            // Legacy rows may be inconsistent. Their status and booking rules only apply when this edit changes them,
            // so an unrelated edit (description, priority) is never blocked by old data.
            if (statusChanged || engineerChanged || timesChanged)
            {
                if (RequiresEngineer(request.Status) && !request.EngineerId.HasValue)
                    throw new BusinessRuleException($"A work order with status {request.Status} needs an engineer.");
                if (RequiresBothTimes(request.Status) && (!request.ScheduledStart.HasValue || !request.ScheduledEnd.HasValue))
                    throw new BusinessRuleException($"A work order with status {request.Status} needs both a scheduled start and end.");
            }

            // Skill, active and overlap checks run only when the engineer or the times actually change.
            if (request.EngineerId.HasValue && (engineerChanged || timesChanged))
            {
                var engineer = await _engineers.GetReadOnlyAsync(request.EngineerId.Value) ?? throw new BusinessRuleException("Engineer does not exist.");
                var jobType = await _reference.GetJobTypeAsync(job.JobTypeId) ?? throw new BusinessRuleException("The job type does not exist.");
                var booked = IsBooked(request.EngineerId, request.ScheduledStart, request.ScheduledEnd);
                _rules.EnsureEngineerSuitable(jobType.RequiredSkillId, engineer, booked ? "scheduled" : "allocated");
                if (booked)
                    await EnsureBookingAllowedAsync(engineer.Id, job.Id, request.Priority, request.ScheduledStart!.Value, request.ScheduledEnd!.Value,
                        $"Use POST /api/work-orders/{job.Id}/schedule, which can displace a non-P1 booking.");
            }

            job.EngineerId = request.EngineerId;
            job.Priority = request.Priority;
            job.Status = request.Status;
            job.ScheduledStart = request.ScheduledStart;
            job.ScheduledEnd = request.ScheduledEnd;
            job.ActualStart = request.ActualStart;
            job.ActualEnd = request.ActualEnd;
            job.Outcome = request.Outcome;
            // Jobs with visit rows get FirstTimeFix from the visit service (return visits count against it).
            // Legacy jobs with no visits keep the outcome-based flag.
            if (!hasVisits)
                job.FirstTimeFix = request.Outcome is null ? null : request.Outcome == "Fixed";
            job.Description = request.Description;
            job.UpdatedAt = DateTime.UtcNow;
            await _jobs.UpdateAsync(job);
            return job;
        });
        return await ToDtoAsync(updated);
    }

    private static bool IsBooked(int? engineerId, DateTime? start, DateTime? end) =>
        engineerId.HasValue && start.HasValue && end.HasValue;

    private static bool RequiresEngineer(string status) =>
        status is WorkOrderStatus.Allocated or WorkOrderStatus.Scheduled or WorkOrderStatus.OnSite;

    private static bool RequiresBothTimes(string status) =>
        status is WorkOrderStatus.Scheduled or WorkOrderStatus.OnSite;

    // Non-P1 bookings are refused on overlap (R10). A P1 is never allowed to displace another booking through the
    // legacy endpoints; it must go through the schedule endpoint, which applies R11 and R12.
    private async Task EnsureBookingAllowedAsync(int engineerId, int jobId, string priority, DateTime start, DateTime end, string hint)
    {
        var overlaps = await _rules.FindOverlapsAsync(engineerId, jobId, start, end);
        if (overlaps.Count == 0) return;
        if (priority == "P1")
            throw new ConflictException($"A P1 cannot be booked over {string.Join(", ", overlaps.Select(j => j.JobRef))} through this endpoint. {hint}");
        _rules.ThrowIfOverlapping(overlaps);
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

        // The job row lock, the status check, the conditional decrement and the JobPart insert share one transaction.
        // Locking first means a visit completing on this job cannot change its status between the check and the insert.
        var jobPart = await _jobs.InTransactionAsync(async () =>
        {
            await _jobs.LockForUpdateAsync(id);
            var job = await _jobs.GetAsync(id) ?? throw new NotFoundException("Job", id);
            if (job.Status is "Cancelled") throw new BusinessRuleException("Parts cannot be added to a cancelled job.");

            // A failed decrement rolls back.
            if (await _parts.TryDecrementStockAsync(part.Id, request.Quantity) == 0)
            {
                // Report the stock as it is now (inside this transaction), not the figure read before it.
                var current = await _parts.GetStockLevelAsync(part.Id);
                throw new ConflictException($"Only {current?.StockQty ?? 0} of {part.Sku} in stock.");
            }
            // Same transaction: a request is raised if this decrement took stock to or below the reorder level.
            await _reorder.EnsureReorderForPartAsync(part.Id);
            return await _jobs.AddPartAsync(new JobPart { JobId = id, PartId = part.Id, Quantity = request.Quantity });
        });
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
