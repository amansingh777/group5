using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IDispatchBoardService
{
    Task<DispatchBoardDto> GetBoardAsync(DateOnly? from, int? days, int? regionId);
}

// One batched query per source (engineers, shifts, booked jobs, unscheduled jobs, displacement notes); cells are built in memory.
public class DispatchBoardService : IDispatchBoardService
{
    private const int DefaultDays = 7;
    private const int MaxDays = 14;
    private const int UnscheduledCap = 200;

    private readonly IEngineerRepository _engineers;
    private readonly IShiftRepository _shifts;
    private readonly IWorkOrderReadRepository _workOrders;

    public DispatchBoardService(IEngineerRepository engineers, IShiftRepository shifts, IWorkOrderReadRepository workOrders)
    {
        _engineers = engineers;
        _shifts = shifts;
        _workOrders = workOrders;
    }

    public async Task<DispatchBoardDto> GetBoardAsync(DateOnly? from, int? days, int? regionId)
    {
        var now = DateTime.UtcNow;
        var first = from ?? DateOnly.FromDateTime(now);
        var count = Math.Clamp(days ?? DefaultDays, 1, MaxDays);
        var last = first.AddDays(count - 1);
        var windowStart = first.ToDateTime(TimeOnly.MinValue);
        var windowEnd = last.ToDateTime(TimeOnly.MinValue).AddDays(1);

        var engineers = await _engineers.ListAsync(regionId, null, true);
        var engineerIds = engineers.Select(e => e.Id).ToList();
        var shifts = engineerIds.Count == 0
            ? new List<Shift>()
            : await _shifts.GetBetweenAsync(first, last, engineerIds);
        var booked = engineerIds.Count == 0
            ? new List<BoardJobRow>()
            : await _workOrders.ScheduledForEngineersAsync(engineerIds, windowStart, windowEnd);
        var unscheduled = await _workOrders.UnscheduledAsync(regionId);
        var displaced = (await _workOrders.DisplacedJobIdsAsync(
            unscheduled.Where(r => r.Status == WorkOrderStatus.Allocated && r.ScheduledStart == null).Select(r => r.Id)))
            .ToHashSet();

        var shiftsByDay = shifts.GroupBy(s => (s.EngineerId, s.ShiftDate)).ToDictionary(g => g.Key, g => g.ToList());
        var jobsByDay = booked.Where(b => b.EngineerId.HasValue)
            .GroupBy(b => (EngineerId: b.EngineerId!.Value, Day: DateOnly.FromDateTime(b.ScheduledStart!.Value)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<DispatchEngineerRowDto>(engineers.Count);
        foreach (var engineer in engineers)
        {
            var cells = new List<DispatchCellDto>(count);
            for (var i = 0; i < count; i++)
            {
                var day = first.AddDays(i);
                shiftsByDay.TryGetValue((engineer.Id, day), out var dayShifts);
                jobsByDay.TryGetValue((engineer.Id, day), out var dayJobs);
                dayShifts ??= new List<Shift>();
                cells.Add(new DispatchCellDto(
                    day,
                    dayShifts.Count == 0 ? null : dayShifts.Min(s => s.StartTime),
                    dayShifts.Count == 0 ? null : dayShifts.Max(s => s.EndTime),
                    dayShifts.Sum(s => s.BreakMinutes),
                    (dayJobs ?? new List<BoardJobRow>()).Select(j => j.ToBoardJobDto(SlaState.Compute(j.Status, j.CreatedAt, j.SlaDueAt, now), IsFlagged(j, displaced))).ToList()));
            }
            rows.Add(new DispatchEngineerRowDto(engineer.Id, engineer.FullName, engineer.RegionId,
                engineer.Skills.Select(s => s.SkillId).OrderBy(x => x).ToList(), cells));
        }

        // Capped list: rebooks first, then priority, then SLA due (nulls last), then id. The total is the uncapped count.
        var unscheduledDtos = unscheduled
            .Select(j => (Row: j, NeedsRebook: IsFlagged(j, displaced)))
            .OrderByDescending(x => x.NeedsRebook)
            .ThenBy(x => x.Row.Priority, StringComparer.Ordinal)
            .ThenBy(x => x.Row.SlaDueAt is null ? 1 : 0)
            .ThenBy(x => x.Row.SlaDueAt)
            .ThenBy(x => x.Row.Id)
            .Take(UnscheduledCap)
            .Select(x => x.Row.ToBoardJobDto(SlaState.Compute(x.Row.Status, x.Row.CreatedAt, x.Row.SlaDueAt, now), x.NeedsRebook))
            .ToList();
        return new DispatchBoardDto(first, count, rows, unscheduledDtos, unscheduled.Count);
    }

    // R13: an Allocated job with no time that a P1 displaced needs a rebook.
    private static bool IsFlagged(BoardJobRow job, HashSet<int> displaced) =>
        job.Status == WorkOrderStatus.Allocated && job.ScheduledStart == null && displaced.Contains(job.Id);
}
