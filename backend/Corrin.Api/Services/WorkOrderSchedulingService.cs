using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IWorkOrderSchedulingService
{
    Task<WorkOrderScheduleDto> ScheduleAsync(int jobId, WorkOrderScheduleRequest request);
    Task<NextSlotDto> NextSlotAsync(int jobId, DateOnly? from);
}

// Times are wall-clock values stored without offset, matching the rest of the schema.
public class WorkOrderSchedulingService : IWorkOrderSchedulingService
{
    private static readonly string[] SchedulableStatuses = { "Allocated", "Scheduled" };
    private const int SearchDays = 14;
    private const int GridMinutes = 15;
    private const int P1GraceMinutes = 15;

    private readonly IJobRepository _jobs;
    private readonly IEngineerRepository _engineers;
    private readonly IShiftRepository _shifts;
    private readonly IReferenceRepository _reference;
    private readonly IEngineerBookingRules _rules;

    public WorkOrderSchedulingService(IJobRepository jobs, IEngineerRepository engineers, IShiftRepository shifts, IReferenceRepository reference,
        IEngineerBookingRules rules)
    {
        _jobs = jobs;
        _engineers = engineers;
        _shifts = shifts;
        _reference = reference;
        _rules = rules;
    }

    public async Task<WorkOrderScheduleDto> ScheduleAsync(int jobId, WorkOrderScheduleRequest request)
    {
        // Unlocked preview: only used to pick the engineer to lock. Status and engineer are re-checked after the locks.
        var preview = await _jobs.GetReadOnlyAsync(jobId) ?? throw new NotFoundException("Job", jobId);
        var engineerId = request.EngineerId ?? preview.EngineerId
            ?? throw new BusinessRuleException("The work order has no engineer. Supply engineerId or allocate first.");
        var engineer = await _engineers.GetReadOnlyAsync(engineerId) ?? throw new NotFoundException("Engineer", engineerId);
        var jobType = await RequiredJobTypeAsync(preview);
        _rules.EnsureEngineerSuitable(jobType.RequiredSkillId, engineer, "scheduled");

        var start = request.Start;
        var duration = TimeSpan.FromMinutes(jobType.DefaultMinutes);
        var end = start + duration;
        var day = DateOnly.FromDateTime(start);
        if (DateOnly.FromDateTime(end) != day)
            throw new BusinessRuleException("A booking cannot run past midnight.");

        // Starts are UK wall-clock times, so "now" is UK local time. A P1 may start up to 15 minutes ago.
        var ukNow = UkClock.Now();
        if (preview.Priority == "P1")
        {
            if (start < ukNow.AddMinutes(-P1GraceMinutes))
                throw new BusinessRuleException($"The start {start:yyyy-MM-dd HH:mm} is more than {P1GraceMinutes} minutes in the past. UK time is {ukNow:yyyy-MM-dd HH:mm}.");
        }
        else if (start < ukNow)
        {
            throw new BusinessRuleException($"The start {start:yyyy-MM-dd HH:mm} is in the past. UK time is {ukNow:yyyy-MM-dd HH:mm}; choose a later start.");
        }

        // Stored and displayed as UTC (notes and timestamps), unchanged by the UK clock.
        var now = DateTime.UtcNow;
        return await _jobs.InTransactionAsync(async () =>
        {
            // Lock the engineer row first so concurrent schedules for this engineer serialise, then the job row
            // so a concurrent allocate, visit start or second schedule of this job cannot interleave.
            // Both locks come before any plain read, so the overlap reads see the committed state.
            await _engineers.LockForUpdateAsync(engineer.Id);
            await _jobs.LockForUpdateAsync(jobId);
            var job = await _jobs.GetAsync(jobId) ?? throw new NotFoundException("Job", jobId);
            if (!SchedulableStatuses.Contains(job.Status))
                throw new ConflictException($"A work order with status {job.Status} cannot be scheduled.");
            if ((request.EngineerId ?? job.EngineerId) != engineer.Id)
                throw new ConflictException("The work order's engineer changed while it was being booked. Retry.");

            var dayStart = day.ToDateTime(TimeOnly.MinValue);
            var dayShifts = await _shifts.GetForDayAsync(engineer.Id, day);
            if (!dayShifts.Any(s => start.TimeOfDay >= s.StartTime.ToTimeSpan() && end.TimeOfDay <= s.EndTime.ToTimeSpan()
                    && !ShiftBreak.Overlaps(s, dayStart, start, end)))
                throw new BusinessRuleException($"The window is not inside one of the engineer's shifts on {day:yyyy-MM-dd}, outside its break.");

            var overlaps = await _rules.FindOverlapsAsync(engineer.Id, job.Id, start, end);
            var isP1 = job.Priority == "P1";
            if (isP1)
            {
                // A P1 may take a booked slot, but never over another P1 or a job already on site.
                var blocking = overlaps.Where(j => j.Priority == "P1" || j.ActualStart != null || j.Status == "OnSite").ToList();
                if (blocking.Count > 0)
                    throw new ConflictException($"A P1 cannot displace {string.Join(", ", blocking.Select(j => j.JobRef))}: it is another P1 or is already on site.");
            }

            // A P1 displaces the jobs it overlaps, so their minutes do not count against the day.
            var dayBookings = (await _jobs.GetActiveBookingsAsync(new[] { engineer.Id }, dayStart, dayStart.AddDays(1)))
                .Where(j => j.Id != job.Id && j.ScheduledStart!.Value.Date == dayStart)
                .Where(j => !isP1 || !overlaps.Any(o => o.Id == j.Id))
                .ToList();
            if (BookedMinutes(dayBookings) + duration.TotalMinutes > RosteredMinutes(dayShifts))
                throw new BusinessRuleException("The booking would exceed the engineer's rostered hours for that day.");

            var displaced = new List<DisplacedJobDto>();

            if (isP1)
            {
                // Lock every displaced row in ascending Id order (after the engineer and P1 locks), then re-read and re-check.
                // Throw rather than skip: a skipped job would stay booked under the P1 (double booking). The transaction rolls back.
                var ordered = overlaps.OrderBy(j => j.Id).ToList();
                foreach (var other in ordered)
                    await _jobs.LockForUpdateAsync(other.Id);

                foreach (var other in ordered)
                {
                    var tracked = await _jobs.GetAsync(other.Id)
                        ?? throw new ConflictException($"{other.JobRef} was removed while the P1 was being booked. Retry.");
                    var problem = DisplacementProblem(tracked, engineer.Id, start, end);
                    if (problem is not null)
                        throw new ConflictException($"{other.JobRef} cannot be displaced by this P1: {problem} Retry.");

                    tracked.ScheduledStart = null;
                    tracked.ScheduledEnd = null;
                    tracked.Status = "Allocated";
                    tracked.UpdatedAt = now;
                    await _jobs.AddNoteAsync(new JobNote
                    {
                        JobId = tracked.Id,
                        Author = request.UserName,
                        Note = $"{WorkOrderNotes.DisplacedByP1NotePrefix}{job.JobRef} on {now:yyyy-MM-dd HH:mm} UTC by {request.UserName}",
                        CreatedAt = now
                    });
                    displaced.Add(tracked.ToDisplacedDto());
                }
            }
            else
            {
                _rules.ThrowIfOverlapping(overlaps);
            }

            job.EngineerId = engineer.Id;
            job.ScheduledStart = start;
            job.ScheduledEnd = end;
            job.Status = "Scheduled";
            job.UpdatedAt = now;
            await _jobs.UpdateAsync(job);

            return new WorkOrderScheduleDto(job.Id, job.Status, engineer.Id, start, end, displaced);
        });
    }

    public async Task<NextSlotDto> NextSlotAsync(int jobId, DateOnly? from)
    {
        var job = await _jobs.GetReadOnlyAsync(jobId) ?? throw new NotFoundException("Job", jobId);
        var jobType = await RequiredJobTypeAsync(job);
        var duration = TimeSpan.FromMinutes(jobType.DefaultMinutes);

        // Slots are UK wall-clock times, so "now" is UK local time.
        var now = UkClock.Now();
        var firstDay = from ?? DateOnly.FromDateTime(now);
        var lastDay = firstDay.AddDays(SearchDays - 1);
        var horizonStart = firstDay.ToDateTime(TimeOnly.MinValue);
        var horizonEnd = lastDay.ToDateTime(TimeOnly.MinValue).AddDays(1);
        // Never offer a start in the past, rounded up to the 15-minute grid.
        var nowRounded = now.Date.AddMinutes(Math.Ceiling(now.TimeOfDay.TotalMinutes / GridMinutes) * GridMinutes);
        var earliest = horizonStart > nowRounded ? horizonStart : nowRounded;

        var pool = await _engineers.ListAsync(null, jobType.RequiredSkillId, true);
        var notFound = new NotFoundException($"Free slot in the next {SearchDays} days for job", jobId);
        if (pool.Count == 0) throw notFound;

        var ids = pool.Select(e => e.Id).ToList();
        var shifts = await _shifts.GetBetweenAsync(firstDay, lastDay, ids);
        var bookings = await _jobs.GetActiveBookingsAsync(ids, horizonStart, horizonEnd);

        var candidates = new List<(DateTime Start, Engineer Engineer)>();
        foreach (var engineer in pool)
        {
            var engineerShifts = shifts.Where(s => s.EngineerId == engineer.Id).OrderBy(s => s.ShiftDate).ThenBy(s => s.StartTime).ToList();
            var engineerBookings = bookings.Where(b => b.EngineerId == engineer.Id).ToList();
            DateTime? found = null;

            foreach (var dayGroup in engineerShifts.GroupBy(s => s.ShiftDate).OrderBy(g => g.Key))
            {
                var dayBase = dayGroup.Key.ToDateTime(TimeOnly.MinValue);
                var dayBookings = engineerBookings.Where(b => b.ScheduledStart!.Value.Date == dayBase).ToList();
                if (BookedMinutes(dayBookings) + duration.TotalMinutes > RosteredMinutes(dayGroup)) continue;

                foreach (var shift in dayGroup.OrderBy(s => s.StartTime))
                {
                    var shiftEnd = dayBase + shift.EndTime.ToTimeSpan();
                    for (var t = dayBase + shift.StartTime.ToTimeSpan(); t + duration <= shiftEnd; t = t.AddMinutes(GridMinutes))
                    {
                        if (t < earliest) continue;
                        var tEnd = t + duration;
                        if (engineerBookings.Any(b => b.ScheduledStart < tEnd && b.ScheduledEnd > t)) continue;
                        if (ShiftBreak.Overlaps(shift, dayBase, t, tEnd)) continue;
                        found = t;
                        break;
                    }
                    if (found is not null) break;
                }
                if (found is not null) break;
            }

            if (found is not null) candidates.Add((found.Value, engineer));
        }

        if (candidates.Count == 0) throw notFound;

        var pick = candidates
            .OrderBy(c => c.Start)
            .ThenBy(c => c.Engineer.RegionId == job.RegionId ? 0 : 1)
            .ThenBy(c => c.Engineer.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Engineer.Id)
            .First();

        return new NextSlotDto(pick.Engineer.Id, pick.Engineer.FullName, pick.Start, pick.Start + duration);
    }

    // Re-checked on the locked row. Returns why a displaced job can no longer be moved, or null if it still can.
    private static string? DisplacementProblem(Job tracked, int engineerId, DateTime start, DateTime end)
    {
        if (tracked.ActualStart is not null || tracked.Status == "OnSite")
            return $"it has started or is on site (status {tracked.Status}).";
        if (tracked.Status is "Cancelled" or "Completed" or "NotCompleted")
            return $"it is now {tracked.Status}.";
        if (tracked.Priority == "P1")
            return "it has become a P1, and a P1 cannot displace another P1.";
        if (tracked.EngineerId != engineerId || tracked.ScheduledStart is null || tracked.ScheduledEnd is null
            || !(tracked.ScheduledStart < end && tracked.ScheduledEnd > start))
            return "it was moved while the P1 was being booked.";
        return null;
    }

    private async Task<JobType> RequiredJobTypeAsync(Job job) =>
        await _reference.GetJobTypeAsync(job.JobTypeId) ?? throw new BusinessRuleException("The job type does not exist.");

    private static double BookedMinutes(IEnumerable<Job> jobs) =>
        jobs.Sum(j => j.ScheduledEnd.HasValue && j.ScheduledStart.HasValue ? (j.ScheduledEnd.Value - j.ScheduledStart.Value).TotalMinutes : 0);

    private static double RosteredMinutes(IEnumerable<Shift> shifts) => shifts.Sum(s => s.RosteredHours * 60);
}
