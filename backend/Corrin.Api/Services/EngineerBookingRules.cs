using Corrin.Api.Domain;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

// Rules shared by work-order allocation, work-order scheduling and the legacy job endpoints (JobService).
// Callers must hold the engineer row lock (IEngineerRepository.LockForUpdateAsync) inside a transaction before
// calling FindOverlapsAsync for a booking they are about to save.
public interface IEngineerBookingRules
{
    // R5, R6: 422 when the engineer is inactive or lacks the required skill. No override.
    // action is "allocated" or "scheduled" and only changes the wording of the inactive message.
    void EnsureEngineerSuitable(int requiredSkillId, Engineer engineer, string action);

    // Jobs booked to the engineer whose [start, end) overlaps, excluding excludeJobId (use 0 for a new job).
    Task<List<Job>> FindOverlapsAsync(int engineerId, int excludeJobId, DateTime start, DateTime end);

    // R10: 409 when any overlap exists. Used for non-P1 bookings only.
    void ThrowIfOverlapping(IReadOnlyList<Job> overlaps);
}

public class EngineerBookingRules : IEngineerBookingRules
{
    private readonly IJobRepository _jobs;

    public EngineerBookingRules(IJobRepository jobs) => _jobs = jobs;

    public void EnsureEngineerSuitable(int requiredSkillId, Engineer engineer, string action)
    {
        if (!engineer.Active)
            throw new BusinessRuleException($"An inactive engineer cannot be {action}.");
        if (!engineer.Skills.Any(s => s.SkillId == requiredSkillId))
            throw new BusinessRuleException("The engineer does not hold the skill required for this job type.");
    }

    public async Task<List<Job>> FindOverlapsAsync(int engineerId, int excludeJobId, DateTime start, DateTime end) =>
        (await _jobs.GetActiveBookingsAsync(new[] { engineerId }, start, end)).Where(j => j.Id != excludeJobId).ToList();

    public void ThrowIfOverlapping(IReadOnlyList<Job> overlaps)
    {
        if (overlaps.Count > 0)
            throw new ConflictException($"The engineer is already booked at that time by {string.Join(", ", overlaps.Select(j => j.JobRef))}.");
    }
}
