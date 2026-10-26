namespace Corrin.Api.Domain;

public class Region { public int Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; }
public class Skill { public int Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; }

public class JobType
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int DefaultMinutes { get; set; }
    public int RequiredSkillId { get; set; }
    public Skill? RequiredSkill { get; set; }
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public int? RegionId { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string AccountRef { get; set; } = "";
    public string Name { get; set; } = "";
    public string CustomerType { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string AddressLine { get; set; } = "";
    public string City { get; set; } = "";
    public string Postcode { get; set; } = "";
    public int RegionId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CustomerNote
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int AuthorUserId { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class Site
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Name { get; set; } = "";
    public string AddressLine { get; set; } = "";
    public string City { get; set; } = "";
    public string Postcode { get; set; } = "";
    public int RegionId { get; set; }
    public string? AccessNotes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Asset
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public string AssetType { get; set; } = "";
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public DateOnly InstallDate { get; set; }
    public DateOnly? LastServiceDate { get; set; }
    public string Status { get; set; } = "";
}

public class Engineer
{
    public int Id { get; set; }
    public string EmployeeNo { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public int RegionId { get; set; }
    public string Grade { get; set; } = "";
    public bool Active { get; set; }
    public DateOnly HiredOn { get; set; }
    public List<EngineerSkill> Skills { get; set; } = new();
}

public class EngineerSkill
{
    public int EngineerId { get; set; }
    public int SkillId { get; set; }
}

public class Shift
{
    public int Id { get; set; }
    public int EngineerId { get; set; }
    public DateOnly ShiftDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public string ShiftType { get; set; } = "";

    public double RosteredHours => (EndTime - StartTime).TotalHours - BreakMinutes / 60.0;
}

public class Part
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal UnitCost { get; set; }
    public int StockQty { get; set; }
    public int ReorderLevel { get; set; }
}

public class Job
{
    public int Id { get; set; }
    public string JobRef { get; set; } = "";
    public int CustomerId { get; set; }
    public int SiteId { get; set; }
    public int? AssetId { get; set; }
    public int JobTypeId { get; set; }
    public int? EngineerId { get; set; }
    public int RegionId { get; set; }
    public string Priority { get; set; } = "P3";
    public string Status { get; set; } = "Requested";
    public DateTime? ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public DateTime? SlaDueAt { get; set; }
    public string? Outcome { get; set; }
    public bool? FirstTimeFix { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class JobNote
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public string Author { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class JobPart
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int PartId { get; set; }
    public int Quantity { get; set; }
}

public class ImportBatch
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public DateTime ImportedAt { get; set; }
    public int TotalRows { get; set; }
    public int CreatedRows { get; set; }
    public int RejectedRows { get; set; }
}

public class JobRequest
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public int RowNo { get; set; }
    public string? CustomerRef { get; set; }
    public string? SitePostcode { get; set; }
    public string? JobTypeCode { get; set; }
    public string? Priority { get; set; }
    public DateOnly? RequestedDate { get; set; }
    public string? Description { get; set; }
    public string Result { get; set; } = "";
    public string? RejectionReason { get; set; }
    public int? JobId { get; set; }
}
