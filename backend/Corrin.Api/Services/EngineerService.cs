using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IEngineerService
{
    Task<IReadOnlyList<EngineerDto>> ListAsync(int? regionId, int? skillId, bool? active);
    Task<EngineerDto> GetAsync(int id);
    Task<EngineerDto> CreateAsync(EngineerRequest request);
    Task<EngineerDto> UpdateAsync(int id, EngineerRequest request);
    Task<EngineerDto> SetSkillsAsync(int id, SkillAssignmentRequest request);
    Task<IReadOnlyList<ShiftDto>> GetShiftsAsync(int id, DateOnly? from, DateOnly? to);
    Task<IReadOnlyList<JobHistoryDto>> GetJobsAsync(int id, DateTime from, DateTime to);
    Task<OnShiftDto> GetOnShiftAsync(int id);
}

public class EngineerService : IEngineerService
{
    private readonly IEngineerRepository _engineers;
    private readonly IShiftRepository _shifts;
    private readonly IJobRepository _jobs;
    private readonly IReferenceRepository _reference;
    private readonly IShiftService _shiftService;

    public EngineerService(IEngineerRepository engineers, IShiftRepository shifts, IJobRepository jobs, IReferenceRepository reference, IShiftService shiftService)
    {
        _engineers = engineers;
        _shifts = shifts;
        _jobs = jobs;
        _reference = reference;
        _shiftService = shiftService;
    }

    public async Task<IReadOnlyList<EngineerDto>> ListAsync(int? regionId, int? skillId, bool? active) =>
        (await _engineers.ListAsync(regionId, skillId, active)).Select(e => e.ToDto()).ToList();

    public async Task<EngineerDto> GetAsync(int id) =>
        (await _engineers.GetReadOnlyAsync(id) ?? throw new NotFoundException("Engineer", id)).ToDto();

    public async Task<EngineerDto> CreateAsync(EngineerRequest request)
    {
        if (!await _reference.RegionExistsAsync(request.RegionId)) throw new BusinessRuleException("Region does not exist.");
        var count = await _engineers.CountAsync();
        var engineer = new Engineer { EmployeeNo = $"ENG-{100 + count + 1:D4}", HiredOn = DateOnly.FromDateTime(DateTime.UtcNow) };
        engineer.Apply(request);
        return (await _engineers.AddAsync(engineer)).ToDto();
    }

    public async Task<EngineerDto> UpdateAsync(int id, EngineerRequest request)
    {
        var engineer = await _engineers.GetAsync(id) ?? throw new NotFoundException("Engineer", id);
        engineer.Apply(request);
        await _engineers.UpdateAsync(engineer);
        return engineer.ToDto();
    }

    public async Task<EngineerDto> SetSkillsAsync(int id, SkillAssignmentRequest request)
    {
        if (await _engineers.GetReadOnlyAsync(id) is null) throw new NotFoundException("Engineer", id);
        var known = (await _reference.GetSkillsAsync()).Select(s => s.Id).ToHashSet();
        if (request.SkillIds.Any(s => !known.Contains(s))) throw new BusinessRuleException("One or more skills do not exist.");
        await _engineers.SetSkillsAsync(id, request.SkillIds);
        return (await _engineers.GetReadOnlyAsync(id))!.ToDto();
    }

    public async Task<IReadOnlyList<ShiftDto>> GetShiftsAsync(int id, DateOnly? from, DateOnly? to)
    {
        if (await _engineers.GetReadOnlyAsync(id) is null) throw new NotFoundException("Engineer", id);
        return (await _shifts.QueryAsync(id, from, to)).Select(s => s.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<JobHistoryDto>> GetJobsAsync(int id, DateTime from, DateTime to)
    {
        if (await _engineers.GetReadOnlyAsync(id) is null) throw new NotFoundException("Engineer", id);
        return (await _jobs.GetForEngineerAsync(id, from, to))
            .Select(j => new JobHistoryDto(j.Id, j.JobRef, j.Status, j.Outcome, j.ScheduledStart, j.EngineerId)).ToList();
    }

    public async Task<OnShiftDto> GetOnShiftAsync(int id)
    {
        if (await _engineers.GetReadOnlyAsync(id) is null) throw new NotFoundException("Engineer", id);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var shifts = await _shifts.GetForDayAsync(id, today);
        return new OnShiftDto(id, shifts.Any(_shiftService.IsOnShift));
    }
}
