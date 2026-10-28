using Corrin.Api.Domain;

namespace Corrin.Api.Services;

// The schema stores only a break length (Shift.BreakMinutes), not a break start time.
// Version 1 models the break as a block of BreakMinutes centred on the shift midpoint.
// Both next-slot and schedule use this helper, so a suggested slot is always schedulable.
// A shift with no break (BreakMinutes <= 0) has no break window.
public static class ShiftBreak
{
    public static bool Overlaps(Shift shift, DateTime dayBase, DateTime start, DateTime end)
    {
        if (shift.BreakMinutes <= 0) return false;
        var shiftStart = dayBase + shift.StartTime.ToTimeSpan();
        var shiftEnd = dayBase + shift.EndTime.ToTimeSpan();
        var midpoint = shiftStart + (shiftEnd - shiftStart) / 2;
        var half = TimeSpan.FromMinutes(shift.BreakMinutes) / 2;
        return start < midpoint + half && end > midpoint - half;
    }
}
