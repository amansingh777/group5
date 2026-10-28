namespace Corrin.Api.Dtos;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record RegionDto(int Id, string Code, string Name);
public record SkillDto(int Id, string Code, string Name);
public record JobTypeDto(int Id, string Code, string Name, int DefaultMinutes, int RequiredSkillId);

public record UserDto(int Id, string Username, string DisplayName, string Email, string PasswordHash, string Role, int? RegionId, bool Active, DateTime CreatedAt);

public record CustomerDto(int Id, string AccountRef, string Name, string CustomerType, string? Email, string? Phone,
    string AddressLine, string City, string Postcode, int RegionId, DateTime CreatedAt);
public record CustomerRequest(string Name, string CustomerType, string? Email, string? Phone, string AddressLine, string City, string Postcode, int RegionId);
public record CustomerNoteDto(int Id, int CustomerId, int AuthorUserId, string Note, DateTime CreatedAt);
public record NoteRequest(int AuthorUserId, string Note);

public record SiteDto(int Id, int CustomerId, string Name, string AddressLine, string City, string Postcode, int RegionId, string? AccessNotes);
public record SiteRequest(int CustomerId, string Name, string AddressLine, string City, string Postcode, int RegionId, string? AccessNotes);

public record AssetDto(int Id, int SiteId, string AssetType, string Make, string Model, string SerialNumber, DateOnly InstallDate, DateOnly? LastServiceDate, string Status);
public record AssetRequest(int SiteId, string AssetType, string Make, string Model, string SerialNumber, DateOnly InstallDate, DateOnly? LastServiceDate, string Status);

public record EngineerDto(int Id, string EmployeeNo, string FullName, string Email, string? Phone, int RegionId, string Grade, bool Active, DateOnly HiredOn, IReadOnlyList<int> SkillIds);
public record EngineerRequest(string FullName, string Email, string? Phone, int RegionId, string Grade, bool Active);
public record SkillAssignmentRequest(IReadOnlyList<int> SkillIds);
public record OnShiftDto(int EngineerId, bool OnShift);

public record ShiftDto(int Id, int EngineerId, DateOnly ShiftDate, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, string ShiftType, double RosteredHours);
public record ShiftRequest(int EngineerId, DateOnly ShiftDate, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, string ShiftType);

public record PartDto(int Id, string Sku, string Name, decimal UnitCost, int StockQty, int ReorderLevel);
public record PartRequest(string Sku, string Name, decimal UnitCost, int StockQty, int ReorderLevel);

public record JobDto(int Id, string JobRef, int CustomerId, string CustomerName, int SiteId, string SiteName, int? AssetId, int JobTypeId,
    int? EngineerId, string? EngineerName, int RegionId, string Priority, string Status, DateTime? ScheduledStart, DateTime? ScheduledEnd,
    DateTime? ActualStart, DateTime? ActualEnd, DateTime? SlaDueAt, string? Outcome, bool? FirstTimeFix, string? Description);
public record JobCreateRequest(int CustomerId, int SiteId, int? AssetId, int JobTypeId, int? EngineerId, string Priority,
    DateTime? ScheduledStart, DateTime? ScheduledEnd, string? Description);
public record JobUpdateRequest(int? EngineerId, string Priority, string Status, DateTime? ScheduledStart, DateTime? ScheduledEnd,
    DateTime? ActualStart, DateTime? ActualEnd, string? Outcome, string? Description);
public record JobCancelRequest(string Reason);
public record JobNoteDto(int Id, int JobId, string Author, string Note, DateTime CreatedAt);
public record JobNoteRequest(string Author, string Note);
public record JobPartDto(int Id, int JobId, int PartId, int Quantity);
public record JobPartRequest(int PartId, int Quantity);

public record UtilisationRowDto(int EngineerId, string EngineerName, int RegionId, int ShiftsRostered, double HoursWorked, double HoursAvailable, double UtilisationPct);

public record JobStatusCountDto(string Status, int Count);

public record ImportBatchDto(int Id, string FileName, DateTime ImportedAt, int TotalRows, int CreatedRows, int RejectedRows);
public record JobRequestDto(int Id, int BatchId, int RowNo, string? CustomerRef, string? SitePostcode, string? JobTypeCode, string? Priority,
    DateOnly? RequestedDate, string? Description, string Result, string? RejectionReason, int? JobId);
public record ImportResultDto(int BatchId, int TotalRows, int CreatedRows, int RejectedRows, IReadOnlyList<string> Messages);

public record WorkOrderCandidateDto(int Rank, int EngineerId, string EngineerName, int RegionId, string RankReason, IReadOnlyList<string> Warnings);
public record WorkOrderAllocateRequest(int EngineerId, string UserName, DateOnly? Date);
public record WorkOrderAllocationDto(int JobId, string Status, int EngineerId, IReadOnlyList<string> Warnings);
public record WorkOrderScheduleRequest(int? EngineerId, DateTime Start, string UserName);
public record DisplacedJobDto(int JobId, string JobRef, string Status, int? EngineerId);
public record WorkOrderScheduleDto(int JobId, string Status, int EngineerId, DateTime Start, DateTime End, IReadOnlyList<DisplacedJobDto> Displaced);
public record NextSlotDto(int EngineerId, string EngineerName, DateTime Start, DateTime End);
public record VisitDto(int Id, int JobId, int? EngineerId, int VisitNo, DateTime? ScheduledStart, DateTime? ScheduledEnd, DateTime? ActualStart, DateTime? ActualEnd, string? Outcome, DateTime CreatedAt);
public record VisitCompleteRequest(string Outcome, string UserName, string? Notes, IReadOnlyList<JobPartRequest>? Parts);

public record FirstTimeFixQuery(string From, string To);
public record FirstTimeFixRowDto(string Name, int JobsAttended, int FixedFirstTime, double RatePercent);
public record FirstTimeFixReportDto(string From, string To, FirstTimeFixRowDto Overall, IReadOnlyList<FirstTimeFixRowDto> ByRegion, IReadOnlyList<FirstTimeFixRowDto> ByJobType);

public record WorkOrderListQuery(int Page = 1, int PageSize = 25, string? Status = null, int? RegionId = null, int? EngineerId = null, string? Priority = null);
public record WorkOrderStatusRequest(string Status, string UserName);
public record WorkOrderDto(int Id, string JobRef, int RegionId, string CustomerName, string SiteName, string SitePostcode, string? AssetSummary,
    string JobType, string RequiredSkill, string Priority, string Status, int? EngineerId, string? EngineerName,
    DateTime? ScheduledStart, DateTime? ScheduledEnd, DateTime? SlaDueAt, string SlaState, string? Description, string? Outcome,
    DateTime CreatedAt, IReadOnlyList<JobNoteDto> Notes, IReadOnlyList<JobPartDto> Parts);

// Unscheduled is capped at 200 rows; UnscheduledTotal is the uncapped count.
public record DispatchBoardDto(DateOnly From, int Days, IReadOnlyList<DispatchEngineerRowDto> Engineers, IReadOnlyList<DispatchBoardJobDto> Unscheduled, int UnscheduledTotal);
public record DispatchEngineerRowDto(int EngineerId, string FullName, int RegionId, IReadOnlyList<int> SkillIds, IReadOnlyList<DispatchCellDto> Cells);
public record DispatchCellDto(DateOnly Date, TimeOnly? ShiftStart, TimeOnly? ShiftEnd, int BreakMinutes, IReadOnlyList<DispatchBoardJobDto> Jobs);
public record DispatchBoardJobDto(int Id, string JobRef, string Priority, string Status, DateTime? Start, DateTime? End, string SlaState, bool NeedsRebook);

public record ReorderRequestListQuery(int Page = 1, int PageSize = 25, string? Status = null, int? PartId = null);
public record ReorderRequestDto(int Id, int PartId, string PartSku, string PartName, int StockQty, int ReorderLevel, bool BelowReorderLevel,
    string Status, DateOnly? ExpectedOn, string? OrderedBy, string? ReceivedBy, DateTime CreatedAt, DateTime UpdatedAt);
public record ReorderOrderedRequest(DateOnly ExpectedOn, string UserName);
public record ReorderReceivedRequest(int Quantity, string UserName);
