using Corrin.Api.Domain;

namespace Corrin.Api.Services;

// The one SLA rule used by work-order list, detail and dispatch board (spec R25, Q8).
// Window = SlaDueAt - CreatedAt. Amber when less than a quarter of the window remains; breached once SlaDueAt has passed.
public static class SlaState
{
    public const string Breached = "breached";
    public const string Amber = "amber";
    public const string Ok = "ok";
    public const string None = "none";

    public const double AmberShareOfWindow = 0.25;

    public static string Compute(string status, DateTime createdAt, DateTime? slaDueAt, DateTime now)
    {
        if (slaDueAt is null || WorkOrderStatus.IsFinal(status)) return None;
        var due = slaDueAt.Value;
        if (now > due) return Breached;
        var window = due - createdAt;
        var remaining = due - now;
        return remaining.Ticks < window.Ticks * AmberShareOfWindow ? Amber : Ok;
    }
}
