namespace Corrin.Api.Services;

// Slots and scheduled times are UK wall-clock values (see WorkOrderSchedulingService). "Now" must be read in the same zone,
// otherwise British Summer Time offers starts up to an hour in the past. Stored timestamps are not changed by this helper.
public static class UkClock
{
    private static readonly Lazy<TimeZoneInfo> Zone = new(FindZone);

    // Tests set this to fix "now". It returns UK local time, the same kind of value as Now().
    public static Func<DateTime>? Override { get; set; }

    // Current UK local time, unspecified kind.
    public static DateTime Now() => Override is not null ? Override() : FromUtc(DateTime.UtcNow);

    // Converts a UTC instant to UK local time, unspecified kind.
    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone.Value);

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new InvalidOperationException("No UK time zone data is available (tried Europe/London and GMT Standard Time).");
    }
}
