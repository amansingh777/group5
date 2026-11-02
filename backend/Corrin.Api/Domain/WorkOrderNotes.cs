namespace Corrin.Api.Domain;

// Note text shared by the writer (WorkOrderSchedulingService) and the readers (WorkOrderReadRepository).
public static class WorkOrderNotes
{
    // Written when a P1 displaces a booked job. The board treats a note starting with this as a rebook flag (R13).
    public const string DisplacedByP1NotePrefix = "Displaced by P1 ";
}
