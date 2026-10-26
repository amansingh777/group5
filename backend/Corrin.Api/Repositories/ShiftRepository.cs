using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface IShiftRepository
{
    Task<List<Shift>> QueryAsync(int? engineerId, DateOnly? from, DateOnly? to);
    Task<List<Shift>> GetBetweenAsync(DateOnly from, DateOnly to, IEnumerable<int> engineerIds);
    Task<Shift?> GetAsync(int id);
    Task<List<Shift>> GetForDayAsync(int engineerId, DateOnly day);
    Task<Shift> AddAsync(Shift shift);
    Task UpdateAsync(Shift shift);
    Task RemoveAsync(Shift shift);
}

public class ShiftRepository : IShiftRepository
{
    private readonly CorrinDbContext _db;
    public ShiftRepository(CorrinDbContext db) => _db = db;

    public Task<List<Shift>> QueryAsync(int? engineerId, DateOnly? from, DateOnly? to)
    {
        var q = _db.Shifts.AsNoTracking().AsQueryable();
        if (engineerId.HasValue) q = q.Where(s => s.EngineerId == engineerId);
        if (from.HasValue) q = q.Where(s => s.ShiftDate >= from);
        if (to.HasValue) q = q.Where(s => s.ShiftDate <= to);
        return q.OrderBy(s => s.ShiftDate).ThenBy(s => s.EngineerId).Take(1000).ToListAsync();
    }

    public Task<List<Shift>> GetBetweenAsync(DateOnly from, DateOnly to, IEnumerable<int> engineerIds)
    {
        var ids = engineerIds.ToList();
        return _db.Shifts.AsNoTracking().Where(s => s.ShiftDate >= from && s.ShiftDate <= to && ids.Contains(s.EngineerId)).ToListAsync();
    }

    public Task<Shift?> GetAsync(int id) => _db.Shifts.FirstOrDefaultAsync(s => s.Id == id);

    public Task<List<Shift>> GetForDayAsync(int engineerId, DateOnly day) =>
        _db.Shifts.AsNoTracking().Where(s => s.EngineerId == engineerId && s.ShiftDate == day).ToListAsync();

    public async Task<Shift> AddAsync(Shift shift)
    {
        _db.Shifts.Add(shift);
        await _db.SaveChangesAsync();
        return shift;
    }

    public Task UpdateAsync(Shift shift) => _db.SaveChangesAsync();

    public async Task RemoveAsync(Shift shift)
    {
        _db.Shifts.Remove(shift);
        await _db.SaveChangesAsync();
    }
}
