using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IReportService
{
    Task<IReadOnlyList<UtilisationRowDto>> GetEngineerUtilisationAsync(DateOnly from, DateOnly to, int? regionId);
    Task<IReadOnlyList<JobStatusCountDto>> GetJobsByStatusAsync(int? regionId);
}

public class ReportService : IReportService
{
    private readonly IEngineerRepository _engineers;
    private readonly IJobRepository _jobs;
    private readonly IShiftRepository _shifts;

    public ReportService(IEngineerRepository engineers, IJobRepository jobs, IShiftRepository shifts)
    {
        _engineers = engineers;
        _jobs = jobs;
        _shifts = shifts;
    }

    public async Task<IReadOnlyList<UtilisationRowDto>> GetEngineerUtilisationAsync(DateOnly from, DateOnly to, int? regionId)
    {
        if (to < from) throw new BusinessRuleException("'to' must not be before 'from'.");

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var engineers = await _engineers.ListAsync(regionId, null, true);
        var jobs = await _jobs.GetCompletedBetweenAsync(start, end, regionId);
        var shifts = await _shifts.GetBetweenAsync(from, to, engineers.Select(e => e.Id));

        var periodHours = (end - start).TotalHours;

        var rows = new List<UtilisationRowDto>();
        foreach (var engineer in engineers)
        {
            var worked = jobs
                .Where(j => j.EngineerId == engineer.Id && j.ActualStart.HasValue && j.ActualEnd.HasValue)
                .Sum(j => (j.ActualEnd!.Value - j.ActualStart!.Value).TotalHours);
            var rostered = shifts.Count(s => s.EngineerId == engineer.Id);
            var available = periodHours;
            var pct = available > 0 ? worked / available * 100 : 0;
            rows.Add(new UtilisationRowDto(engineer.Id, engineer.FullName, engineer.RegionId, rostered,
                Math.Round(worked, 2), Math.Round(available, 2), Math.Round(pct, 1)));
        }
        return rows.OrderByDescending(r => r.UtilisationPct).ToList();
    }

    public async Task<IReadOnlyList<JobStatusCountDto>> GetJobsByStatusAsync(int? regionId) =>
        (await _jobs.CountByStatusAsync(regionId)).Select(x => new JobStatusCountDto(x.Status, x.Count)).ToList();
}
