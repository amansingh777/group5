using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IWorkOrderVisitService
{
    Task<VisitDto> StartAsync(int jobId);
    Task<VisitDto> CompleteAsync(int jobId, VisitCompleteRequest request);
    Task<IReadOnlyList<VisitDto>> ListAsync(int jobId);
}

// Visit state changes and the parts they use share one transaction; a stock conflict rolls the whole visit back.
public class WorkOrderVisitService : IWorkOrderVisitService
{
    private static readonly string[] Outcomes = { "Fixed", "PartsNeeded", "NoAccess", "FollowUpRequired" };

    private readonly IJobRepository _jobs;
    private readonly IVisitRepository _visits;
    private readonly IJobService _jobService;

    public WorkOrderVisitService(IJobRepository jobs, IVisitRepository visits, IJobService jobService)
    {
        _jobs = jobs;
        _visits = visits;
        _jobService = jobService;
    }

    public Task<VisitDto> StartAsync(int jobId) =>
        _jobs.InTransactionAsync(async () =>
        {
            // Job row lock first, so a concurrent start or complete on this job waits and then sees the new status.
            await _jobs.LockForUpdateAsync(jobId);
            var job = await _jobs.GetAsync(jobId) ?? throw new NotFoundException("Job", jobId);
            if (job.Status != "Scheduled")
                throw new BusinessRuleException($"A work order with status {job.Status} cannot be started.");

            var now = DateTime.UtcNow;
            var visit = (await _visits.GetForJobAsync(jobId)).LastOrDefault()
                ?? await _visits.AddAsync(new Visit { JobId = jobId, VisitNo = 1, CreatedAt = now });
            if (visit.ActualStart is not null || visit.ActualEnd is not null)
                throw new BusinessRuleException($"Visit {visit.VisitNo} has already started.");

            visit.EngineerId = job.EngineerId;
            visit.ScheduledStart = job.ScheduledStart;
            visit.ScheduledEnd = job.ScheduledEnd;
            visit.ActualStart = now;

            job.Status = "OnSite";
            job.ActualStart = now;
            job.UpdatedAt = now;
            await _jobs.UpdateAsync(job);
            return visit.ToDto();
        });

    public Task<VisitDto> CompleteAsync(int jobId, VisitCompleteRequest request)
    {
        if (!Outcomes.Contains(request.Outcome))
            throw new BusinessRuleException($"Outcome {request.Outcome} is not allowed.");

        return _jobs.InTransactionAsync(async () =>
        {
            // Job row lock first, so a concurrent complete or allocation on this job waits and then sees the new status.
            await _jobs.LockForUpdateAsync(jobId);
            var job = await _jobs.GetAsync(jobId) ?? throw new NotFoundException("Job", jobId);
            if (job.Status != "OnSite")
                throw new BusinessRuleException($"A work order with status {job.Status} cannot be completed; it must be on site.");

            var visit = (await _visits.GetForJobAsync(jobId)).LastOrDefault();
            if (visit is null || visit.ActualStart is null || visit.ActualEnd is not null)
                throw new BusinessRuleException("The work order has no visit in progress.");

            // Parts go first: AddPartAsync decrements stock or throws 409 before any visit or job state changes.
            foreach (var part in request.Parts ?? new List<JobPartRequest>())
                await _jobService.AddPartAsync(jobId, part);

            var now = DateTime.UtcNow;
            visit.ActualEnd = now;
            visit.Outcome = request.Outcome;

            // Spec G5: first-time-fix only when visit 1 is Fixed.
            job.Outcome = request.Outcome;
            job.FirstTimeFix = request.Outcome == "Fixed" && visit.VisitNo == 1;
            job.ActualEnd = now;
            job.UpdatedAt = now;

            var note = $"Visit {visit.VisitNo} outcome: {request.Outcome}.";
            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                var text = request.Notes.Trim();
                if (!text.EndsWith('.') && !text.EndsWith('!') && !text.EndsWith('?')) text += ".";
                note += $" {text}";
            }

            if (request.Outcome == "Fixed")
            {
                job.Status = "Completed";
            }
            else
            {
                // Spec R18: the work order stays open in Triaged and a return visit is created unscheduled.
                // The job's own actual times are cleared; the visit keeps its times.
                job.Status = "Triaged";
                job.ScheduledStart = null;
                job.ScheduledEnd = null;
                job.ActualStart = null;
                job.ActualEnd = null;
                var followUp = await _visits.AddAsync(new Visit { JobId = jobId, VisitNo = visit.VisitNo + 1, CreatedAt = now });
                note += $" Follow-up visit {followUp.VisitNo} needs booking.";
            }

            await _jobs.AddNoteAsync(new JobNote { JobId = jobId, Author = request.UserName, Note = note, CreatedAt = now });
            await _jobs.UpdateAsync(job);
            return visit.ToDto();
        });
    }

    public async Task<IReadOnlyList<VisitDto>> ListAsync(int jobId)
    {
        if (await _jobs.GetReadOnlyAsync(jobId) is null) throw new NotFoundException("Job", jobId);
        return (await _visits.ListForJobAsync(jobId)).Select(v => v.ToDto()).ToList();
    }
}
