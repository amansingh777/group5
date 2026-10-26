using Corrin.Api.Domain;
using Corrin.Api.Dtos;

namespace Corrin.Api.Mappers;

public static class Mapper
{
    public static RegionDto ToDto(this Region r) => new(r.Id, r.Code, r.Name);
    public static SkillDto ToDto(this Skill s) => new(s.Id, s.Code, s.Name);
    public static JobTypeDto ToDto(this JobType t) => new(t.Id, t.Code, t.Name, t.DefaultMinutes, t.RequiredSkillId);

    public static UserDto ToDto(this User u) => new(u.Id, u.Username, u.DisplayName, u.Email, u.PasswordHash, u.Role, u.RegionId, u.Active, u.CreatedAt);

    public static CustomerDto ToDto(this Customer c) => new(c.Id, c.AccountRef, c.Name, c.CustomerType, c.Email, c.Phone, c.AddressLine, c.City, c.Postcode, c.RegionId, c.CreatedAt);
    public static void Apply(this Customer c, CustomerRequest r)
    {
        c.Name = r.Name; c.CustomerType = r.CustomerType; c.Email = r.Email; c.Phone = r.Phone;
        c.AddressLine = r.AddressLine; c.City = r.City; c.Postcode = r.Postcode; c.RegionId = r.RegionId;
    }
    public static CustomerNoteDto ToDto(this CustomerNote n) => new(n.Id, n.CustomerId, n.AuthorUserId, n.Note, n.CreatedAt);

    public static SiteDto ToDto(this Site s) => new(s.Id, s.CustomerId, s.Name, s.AddressLine, s.City, s.Postcode, s.RegionId, s.AccessNotes);
    public static void Apply(this Site s, SiteRequest r)
    {
        s.CustomerId = r.CustomerId; s.Name = r.Name; s.AddressLine = r.AddressLine; s.City = r.City;
        s.Postcode = r.Postcode; s.RegionId = r.RegionId; s.AccessNotes = r.AccessNotes;
    }

    public static AssetDto ToDto(this Asset a) => new(a.Id, a.SiteId, a.AssetType, a.Make, a.Model, a.SerialNumber, a.InstallDate, a.LastServiceDate, a.Status);
    public static void Apply(this Asset a, AssetRequest r)
    {
        a.SiteId = r.SiteId; a.AssetType = r.AssetType; a.Make = r.Make; a.Model = r.Model;
        a.SerialNumber = r.SerialNumber; a.InstallDate = r.InstallDate; a.LastServiceDate = r.LastServiceDate; a.Status = r.Status;
    }

    public static EngineerDto ToDto(this Engineer e) => new(e.Id, e.EmployeeNo, e.FullName, e.Email, e.Phone, e.RegionId, e.Grade, e.Active, e.HiredOn,
        e.Skills.Select(s => s.SkillId).OrderBy(x => x).ToList());
    public static void Apply(this Engineer e, EngineerRequest r)
    {
        e.FullName = r.FullName; e.Email = r.Email; e.Phone = r.Phone; e.RegionId = r.RegionId; e.Grade = r.Grade; e.Active = r.Active;
    }

    public static ShiftDto ToDto(this Shift s) => new(s.Id, s.EngineerId, s.ShiftDate, s.StartTime, s.EndTime, s.BreakMinutes, s.ShiftType, Math.Round(s.RosteredHours, 2));
    public static void Apply(this Shift s, ShiftRequest r)
    {
        s.EngineerId = r.EngineerId; s.ShiftDate = r.ShiftDate; s.StartTime = r.StartTime; s.EndTime = r.EndTime;
        s.BreakMinutes = r.BreakMinutes; s.ShiftType = r.ShiftType;
    }

    public static PartDto ToDto(this Part p) => new(p.Id, p.Sku, p.Name, p.UnitCost, p.StockQty, p.ReorderLevel);
    public static void Apply(this Part p, PartRequest r)
    {
        p.Sku = r.Sku; p.Name = r.Name; p.UnitCost = r.UnitCost; p.StockQty = r.StockQty; p.ReorderLevel = r.ReorderLevel;
    }

    public static JobDto ToDto(this Job j, string customerName, string siteName, string? engineerName) => new(j.Id, j.JobRef, j.CustomerId, customerName,
        j.SiteId, siteName, j.AssetId, j.JobTypeId, j.EngineerId, engineerName, j.RegionId, j.Priority, j.Status, j.ScheduledStart, j.ScheduledEnd,
        j.ActualStart, j.ActualEnd, j.SlaDueAt, j.Outcome, j.FirstTimeFix, j.Description);

    public static JobNoteDto ToDto(this JobNote n) => new(n.Id, n.JobId, n.Author, n.Note, n.CreatedAt);
    public static JobPartDto ToDto(this JobPart p) => new(p.Id, p.JobId, p.PartId, p.Quantity);

    public static ImportBatchDto ToDto(this ImportBatch b) => new(b.Id, b.FileName, b.ImportedAt, b.TotalRows, b.CreatedRows, b.RejectedRows);
    public static JobRequestDto ToDto(this JobRequest r) => new(r.Id, r.BatchId, r.RowNo, r.CustomerRef, r.SitePostcode, r.JobTypeCode, r.Priority,
        r.RequestedDate, r.Description, r.Result, r.RejectionReason, r.JobId);
}
