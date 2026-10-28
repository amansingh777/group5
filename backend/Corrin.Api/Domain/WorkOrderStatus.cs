namespace Corrin.Api.Domain;

// Work orders are Job rows. "Requested" is the stored form of the spec's "Raised" (R1).
public static class WorkOrderStatus
{
    public const string Requested = "Requested";
    public const string Triaged = "Triaged";
    public const string Allocated = "Allocated";
    public const string Scheduled = "Scheduled";
    public const string OnSite = "OnSite";
    public const string Completed = "Completed";
    public const string NotCompleted = "NotCompleted";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All = { Requested, Triaged, Allocated, Scheduled, OnSite, Completed, NotCompleted, Cancelled };
    public static readonly string[] Open = { Requested, Triaged, Allocated, Scheduled, OnSite };

    public static bool IsFinal(string status) => status is Completed or NotCompleted or Cancelled;

    // Moves allowed through POST /api/work-orders/{id}/status (R2, R3). Allocated, Scheduled, OnSite, Completed and
    // NotCompleted are reached only through the allocate, schedule and visit endpoints, so they are never allowed here.
    public static bool CanMoveByStatusEndpoint(string from, string to) => to switch
    {
        Triaged => from == Requested,
        Cancelled => Open.Contains(from),
        _ => false
    };
}
