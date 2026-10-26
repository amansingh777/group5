using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IShiftService
{
    Task<IReadOnlyList<ShiftDto>> QueryAsync(int? engineerId, DateOnly? from, DateOnly? to);
    Task<ShiftDto> CreateAsync(ShiftRequest request);
    Task<ShiftDto> UpdateAsync(int id, ShiftRequest request);
    Task DeleteAsync(int id);
    bool IsOnShift(Shift shift);
}

public class ShiftService : IShiftService
{
    private readonly IShiftRepository _shifts;
    private readonly IEngineerRepository _engineers;

    public ShiftService(IShiftRepository shifts, IEngineerRepository engineers)
    {
        _shifts = shifts;
        _engineers = engineers;
    }

    public async Task<IReadOnlyList<ShiftDto>> QueryAsync(int? engineerId, DateOnly? from, DateOnly? to) =>
        (await _shifts.QueryAsync(engineerId, from, to)).Select(s => s.ToDto()).ToList();

    public async Task<ShiftDto> CreateAsync(ShiftRequest request)
    {
        if (await _engineers.GetReadOnlyAsync(request.EngineerId) is null) throw new BusinessRuleException("Engineer does not exist.");
        await EnsureNoOverlapAsync(request, null);
        var shift = new Shift();
        shift.Apply(request);
        return (await _shifts.AddAsync(shift)).ToDto();
    }

    public async Task<ShiftDto> UpdateAsync(int id, ShiftRequest request)
    {
        var shift = await _shifts.GetAsync(id) ?? throw new NotFoundException("Shift", id);
        await EnsureNoOverlapAsync(request, id);
        shift.Apply(request);
        await _shifts.UpdateAsync(shift);
        return shift.ToDto();
    }

    public async Task DeleteAsync(int id)
    {
        var shift = await _shifts.GetAsync(id) ?? throw new NotFoundException("Shift", id);
        await _shifts.RemoveAsync(shift);
    }

    public bool IsOnShift(Shift shift)
    {
        var now = DateTime.Now;
        return shift.ShiftDate == DateOnly.FromDateTime(now)
            && TimeOnly.FromDateTime(now) >= shift.StartTime
            && TimeOnly.FromDateTime(now) < shift.EndTime;
    }

    private async Task EnsureNoOverlapAsync(ShiftRequest request, int? ignoreId)
    {
        var sameDay = await _shifts.GetForDayAsync(request.EngineerId, request.ShiftDate);
        if (sameDay.Any(s => s.Id != ignoreId && request.StartTime < s.EndTime && s.StartTime < request.EndTime))
            throw new ConflictException("The engineer already has a shift that overlaps this one.");
    }
}
