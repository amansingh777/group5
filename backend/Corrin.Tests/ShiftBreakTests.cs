using Corrin.Api.Domain;
using Corrin.Api.Services;

namespace Corrin.Tests;

public class ShiftBreakTests
{
    private static readonly DateOnly Day = WorkOrderTestData.Day;
    private static readonly DateTime DayBase = Day.ToDateTime(TimeOnly.MinValue);

    private static DateTime T(string time) => Day.ToDateTime(TimeOnly.Parse(time));

    // Shift 08:00 to 17:00 has its midpoint at 12:30, so an hour's break runs 12:00 to 13:00.
    private static readonly Shift EightToFiveWithHourBreak = WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0), breakMinutes: 60);

    [Theory]
    [InlineData("12:15", "12:45")] // Inside the break.
    [InlineData("11:30", "12:30")] // Straddles the break start.
    [InlineData("12:30", "13:30")] // Straddles the break end.
    [InlineData("11:00", "14:00")] // Covers the whole break.
    [InlineData("12:00", "13:00")] // Equals the break.
    public void Overlaps_WindowInsideOrCrossingBreak_ReturnsTrue(string start, string end) =>
        Assert.True(ShiftBreak.Overlaps(EightToFiveWithHourBreak, DayBase, T(start), T(end)));

    [Theory]
    [InlineData("11:00", "12:00")] // Ends exactly as the break starts.
    [InlineData("13:00", "14:00")] // Starts exactly as the break ends.
    [InlineData("09:00", "10:00")] // Well clear, before.
    [InlineData("15:00", "16:00")] // Well clear, after.
    public void Overlaps_WindowClearOrTouchingBreakEdge_ReturnsFalse(string start, string end) =>
        Assert.False(ShiftBreak.Overlaps(EightToFiveWithHourBreak, DayBase, T(start), T(end)));

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Overlaps_NoBreakMinutes_ReturnsFalseForAnyWindow(int breakMinutes)
    {
        var shift = WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(17, 0), breakMinutes);

        Assert.False(ShiftBreak.Overlaps(shift, DayBase, T("12:00"), T("13:00")));
        Assert.False(ShiftBreak.Overlaps(shift, DayBase, T("08:00"), T("17:00")));
    }

    // Shift 08:00 to 12:00 has its midpoint at 10:00. 31 minutes gives a half of 15.5 minutes,
    // so the break runs 09:44:30 to 10:15:30.
    [Theory]
    [InlineData("09:30", "09:44", false)]    // Ends 30 seconds before the break starts.
    [InlineData("09:30", "09:44:30", false)] // Touches the break start exactly.
    [InlineData("09:30", "09:45", true)]     // Runs 30 seconds into the break.
    [InlineData("10:15", "10:30", true)]     // Starts 30 seconds before the break ends.
    [InlineData("10:15:30", "10:30", false)] // Touches the break end exactly.
    public void Overlaps_OddBreakMinutes_HalfMinuteEdgesAreExact(string start, string end, bool expected)
    {
        var shift = WorkOrderTestData.Shift(2, new TimeOnly(8, 0), new TimeOnly(12, 0), breakMinutes: 31);

        Assert.Equal(expected, ShiftBreak.Overlaps(shift, DayBase, T(start), T(end)));
    }

    [Fact]
    public void Overlaps_WindowOnDifferentDayFromShift_UsesDayBase()
    {
        var nextDay = DayBase.AddDays(1);

        // The shift is on nextDay, so the same clock times on the earlier day are before it and not in the break.
        Assert.False(ShiftBreak.Overlaps(EightToFiveWithHourBreak, nextDay, DayBase.AddHours(12).AddMinutes(15), DayBase.AddHours(12).AddMinutes(45)));
        // The same clock times on the shift's own day are.
        Assert.True(ShiftBreak.Overlaps(EightToFiveWithHourBreak, nextDay, nextDay.AddHours(12).AddMinutes(15), nextDay.AddHours(12).AddMinutes(45)));
    }
}
