using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IWorkOrderAllocationService
{
    Task<IReadOnlyList<WorkOrderCandidateDto>> GetCandidatesAsync(int jobId, DateOnly? date);
    Task<WorkOrderAllocationDto> AllocateAsync(int jobId, WorkOrderAllocateRequest request);
}

public class WorkOrderAllocationService : IWorkOrderAllocationService
{
    private static readonly string[] AllocatableStatuses = { "Requested", "Triaged" };

    private readonly IJobRepository _jobs;
    private readonly IEngineerRepository _engineers;
    private readonly IShiftRepository _shifts;
    private readonly ISiteRepository _sites;
    private readonly IReferenceRepository _reference;
    private readonly IEngineerBookingRules _rules;

    public WorkOrderAllocationService(IJobRepository jobs, IEngineerRepository engineers, IShiftRepository shifts,
        ISiteRepository sites, IReferenceRepository reference, IEngineerBookingRules rules)
    {
        _jobs = jobs;
        _engineers = engineers;
        _shifts = shifts;
        _sites = sites;
        _reference = reference;
        _rules = rules;
    }

    public async Task<IReadOnlyList<WorkOrderCandidateDto>> GetCandidatesAsync(int jobId, DateOnly? date)
    {
        var job = await _jobs.GetReadOnlyAsync(jobId) ?? throw new NotFoundException("Job", jobId);
        var requiredSkillId = await RequiredSkillIdAsync(job);
        var day = ResolveDay(job, date);
        var site = await _sites.GetReadOnlyAsync(job.SiteId);
        var jobArea = OutwardCode(site?.Postcode ?? "");

        var pool = await _engineers.ListAsync(null, requiredSkillId, true);
        var ids = pool.Select(e => e.Id).ToList();
        if (ids.Count == 0) return Array.Empty<WorkOrderCandidateDto>();

        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var jobCounts = (await _jobs.CountScheduledByEngineerAsync(ids, dayStart, dayStart.AddDays(1)))
            .ToDictionary(r => r.EngineerId, r => r.Count);
        var areas = (await _jobs.GetEngineerSitePostcodesAsync(ids))
            .GroupBy(r => r.EngineerId)
            .ToDictionary(g => g.Key, g => g.Select(r => OutwardCode(r.Postcode)).ToHashSet());
        var withShift = (await _shifts.GetBetweenAsync(day, day, ids)).Select(s => s.EngineerId).ToHashSet();

        var scored = pool.Select(e =>
        {
            var sameRegion = e.RegionId == job.RegionId;
            var sameArea = jobArea.Length > 0 && areas.TryGetValue(e.Id, out var set) && set.Contains(jobArea);
            var jobsOnDay = jobCounts.GetValueOrDefault(e.Id);
            return (Engineer: e, SameRegion: sameRegion, SameArea: sameArea, JobsOnDay: jobsOnDay, HasShift: withShift.Contains(e.Id));
        })
        .OrderByDescending(x => x.SameRegion)
        .ThenByDescending(x => x.SameArea)
        .ThenBy(x => x.JobsOnDay)
        .ThenBy(x => x.Engineer.FullName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Engineer.Id)
        .ToList();

        return scored.Select((x, i) => new WorkOrderCandidateDto(
            Rank: i + 1,
            EngineerId: x.Engineer.Id,
            EngineerName: x.Engineer.FullName,
            RegionId: x.Engineer.RegionId,
            RankReason: RankReason(x.SameRegion, x.SameArea, jobArea, x.JobsOnDay),
            Warnings: Warnings(x.SameRegion, x.HasShift, day))).ToList();
    }

    public async Task<WorkOrderAllocationDto> AllocateAsync(int jobId, WorkOrderAllocateRequest request)
    {
        // The job row is locked first so two concurrent allocations of one job cannot both pass the status gate.
        // The status is read after the lock, so it reflects any allocation that committed first.
        return await _jobs.InTransactionAsync(async () =>
        {
            await _jobs.LockForUpdateAsync(jobId);
            var job = await _jobs.GetAsync(jobId) ?? throw new NotFoundException("Job", jobId);
            if (!AllocatableStatuses.Contains(job.Status))
                throw new ConflictException($"A work order with status {job.Status} cannot be allocated.");

            var engineer = await _engineers.GetReadOnlyAsync(request.EngineerId) ?? throw new NotFoundException("Engineer", request.EngineerId);

            // Hard blocks: these cannot be overridden.
            _rules.EnsureEngineerSuitable(await RequiredSkillIdAsync(job), engineer, "allocated");

            // Soft mismatches: returned as warnings only.
            var day = ResolveDay(job, request.Date);
            var hasShift = (await _shifts.GetForDayAsync(engineer.Id, day)).Count > 0;
            var warnings = Warnings(engineer.RegionId == job.RegionId, hasShift, day);

            var now = DateTime.UtcNow;
            job.EngineerId = engineer.Id;
            job.Status = "Allocated";
            job.UpdatedAt = now;
            await _jobs.UpdateAsync(job);
            await _jobs.AddNoteAsync(new JobNote
            {
                JobId = job.Id,
                Author = request.UserName,
                Note = $"Allocated to {engineer.FullName}.",
                CreatedAt = now
            });

            return new WorkOrderAllocationDto(job.Id, job.Status, engineer.Id, warnings);
        });
    }

    private async Task<int> RequiredSkillIdAsync(Job job)
    {
        var jobType = await _reference.GetJobTypeAsync(job.JobTypeId)
            ?? throw new BusinessRuleException("The job type does not exist.");
        return jobType.RequiredSkillId;
    }

    private static DateOnly ResolveDay(Job job, DateOnly? requested) =>
        requested ?? (job.ScheduledStart.HasValue ? DateOnly.FromDateTime(job.ScheduledStart.Value) : DateOnly.FromDateTime(DateTime.UtcNow));

    // Postcode "SW1A 1AA" -> "SW1A". Spaces are ignored, so "SW1A1AA" gives the same result.
    private static string OutwardCode(string postcode)
    {
        var compact = new string(postcode.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return compact.Length > 3 ? compact[..^3] : compact;
    }

    private static string RankReason(bool sameRegion, bool sameArea, string jobArea, int jobsOnDay)
    {
        var parts = new List<string> { sameRegion ? "same region" : "different region" };
        if (sameArea) parts.Add($"has worked in outward code {jobArea}");
        parts.Add($"{jobsOnDay} job(s) that day");
        return string.Join("; ", parts);
    }

    private static IReadOnlyList<string> Warnings(bool sameRegion, bool hasShift, DateOnly day)
    {
        var warnings = new List<string>();
        if (!sameRegion) warnings.Add("Engineer is in a different region from the work order.");
        if (!hasShift) warnings.Add($"Engineer has no shift on {day:yyyy-MM-dd}.");
        return warnings;
    }
}
