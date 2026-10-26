using Corrin.Api.Dtos;
using FluentValidation;

namespace Corrin.Api.Validators;

public class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    private static readonly string[] Types = { "Residential", "Commercial", "HousingAssociation" };
    public CustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.CustomerType).Must(t => Types.Contains(t)).WithMessage("CustomerType must be Residential, Commercial or HousingAssociation.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(120).When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Postcode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.RegionId).GreaterThan(0);
    }
}

public class NoteRequestValidator : AbstractValidator<NoteRequest>
{
    public NoteRequestValidator()
    {
        RuleFor(x => x.AuthorUserId).GreaterThan(0);
        RuleFor(x => x.Note).NotEmpty().MaximumLength(1000);
    }
}

public class SiteRequestValidator : AbstractValidator<SiteRequest>
{
    public SiteRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Postcode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.RegionId).GreaterThan(0);
        RuleFor(x => x.AccessNotes).MaximumLength(300);
    }
}

public class AssetRequestValidator : AbstractValidator<AssetRequest>
{
    private static readonly string[] Types = { "Boiler", "Meter", "HeatPump", "Cylinder" };
    private static readonly string[] Statuses = { "Active", "Faulty", "Decommissioned" };
    public AssetRequestValidator()
    {
        RuleFor(x => x.SiteId).GreaterThan(0);
        RuleFor(x => x.AssetType).Must(t => Types.Contains(t)).WithMessage("AssetType must be Boiler, Meter, HeatPump or Cylinder.");
        RuleFor(x => x.Make).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(60);
        RuleFor(x => x.SerialNumber).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Status).Must(s => Statuses.Contains(s)).WithMessage("Status must be Active, Faulty or Decommissioned.");
    }
}

public class EngineerRequestValidator : AbstractValidator<EngineerRequest>
{
    private static readonly string[] Grades = { "Standard", "Senior", "Trainee" };
    public EngineerRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(120);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.RegionId).GreaterThan(0);
        RuleFor(x => x.Grade).Must(g => Grades.Contains(g)).WithMessage("Grade must be Trainee, Standard or Senior.");
    }
}

public class ShiftRequestValidator : AbstractValidator<ShiftRequest>
{
    public ShiftRequestValidator()
    {
        RuleFor(x => x.EngineerId).GreaterThan(0);
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime).WithMessage("EndTime must be after StartTime.");
        RuleFor(x => x.BreakMinutes).InclusiveBetween(0, 120);
        RuleFor(x => x.ShiftType).NotEmpty().MaximumLength(20);
    }
}

public class PartRequestValidator : AbstractValidator<PartRequest>
{
    public PartRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StockQty).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0);
    }
}

public class JobCreateRequestValidator : AbstractValidator<JobCreateRequest>
{
    private static readonly string[] Priorities = { "P1", "P2", "P3" };
    public JobCreateRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.SiteId).GreaterThan(0);
        RuleFor(x => x.JobTypeId).GreaterThan(0);
        RuleFor(x => x.Priority).Must(p => Priorities.Contains(p)).WithMessage("Priority must be P1, P2 or P3.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class JobUpdateRequestValidator : AbstractValidator<JobUpdateRequest>
{
    private static readonly string[] Priorities = { "P1", "P2", "P3" };
    private static readonly string[] Statuses = { "Requested", "Scheduled", "InProgress", "Completed", "Cancelled" };
    public JobUpdateRequestValidator()
    {
        RuleFor(x => x.Priority).Must(p => Priorities.Contains(p)).WithMessage("Priority must be P1, P2 or P3.");
        RuleFor(x => x.Status).Must(s => Statuses.Contains(s)).WithMessage("Status is not recognised.");
        RuleFor(x => x.ScheduledEnd).GreaterThan(x => x.ScheduledStart).When(x => x.ScheduledStart.HasValue && x.ScheduledEnd.HasValue);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class JobCancelRequestValidator : AbstractValidator<JobCancelRequest>
{
    public JobCancelRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
}

public class JobNoteRequestValidator : AbstractValidator<JobNoteRequest>
{
    public JobNoteRequestValidator()
    {
        RuleFor(x => x.Author).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Note).NotEmpty().MaximumLength(1000);
    }
}

public class JobPartRequestValidator : AbstractValidator<JobPartRequest>
{
    public JobPartRequestValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 50);
    }
}
