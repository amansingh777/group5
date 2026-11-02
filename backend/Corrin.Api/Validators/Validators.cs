using Corrin.Api.Domain;
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
        RuleFor(x => x.ScheduledEnd).Must((x, end) => x.ScheduledStart.HasValue == end.HasValue)
            .WithMessage("Give both a scheduled start and end, or neither.");
        RuleFor(x => x.ScheduledEnd).GreaterThan(x => x.ScheduledStart).When(x => x.ScheduledStart.HasValue && x.ScheduledEnd.HasValue)
            .WithMessage("The scheduled end must be after the scheduled start.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class JobUpdateRequestValidator : AbstractValidator<JobUpdateRequest>
{
    private static readonly string[] Priorities = { "P1", "P2", "P3" };
    private static readonly string[] Statuses = WorkOrderStatus.All;
    public JobUpdateRequestValidator()
    {
        RuleFor(x => x.Priority).Must(p => Priorities.Contains(p)).WithMessage("Priority must be P1, P2 or P3.");
        RuleFor(x => x.Status).Must(s => Statuses.Contains(s)).WithMessage("Status is not recognised.");
        RuleFor(x => x.ScheduledEnd).Must((x, end) => x.ScheduledStart.HasValue == end.HasValue)
            .WithMessage("Give both a scheduled start and end, or neither.");
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

public class VisitCompleteRequestValidator : AbstractValidator<VisitCompleteRequest>
{
    private static readonly string[] Outcomes = { "Fixed", "PartsNeeded", "NoAccess", "FollowUpRequired" };
    public VisitCompleteRequestValidator()
    {
        RuleFor(x => x.Outcome).Must(o => Outcomes.Contains(o)).WithMessage("Outcome must be Fixed, PartsNeeded, NoAccess or FollowUpRequired.");
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleForEach(x => x.Parts).SetValidator(new JobPartRequestValidator()).When(x => x.Parts is not null);
    }
}

public class WorkOrderAllocateRequestValidator : AbstractValidator<WorkOrderAllocateRequest>
{
    public WorkOrderAllocateRequestValidator()
    {
        RuleFor(x => x.EngineerId).GreaterThan(0);
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(80);
    }
}

public class WorkOrderScheduleRequestValidator : AbstractValidator<WorkOrderScheduleRequest>
{
    public WorkOrderScheduleRequestValidator()
    {
        RuleFor(x => x.EngineerId).GreaterThan(0).When(x => x.EngineerId.HasValue);
        RuleFor(x => x.Start).NotEqual(default(DateTime)).WithMessage("Start is required.");
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(80);
    }
}

public class WorkOrderListQueryValidator : AbstractValidator<WorkOrderListQuery>
{
    private static readonly string[] Priorities = { "P1", "P2", "P3" };
    public WorkOrderListQueryValidator()
    {
        RuleFor(x => x.Status).Must(s => Corrin.Api.Domain.WorkOrderStatus.All.Contains(s))
            .When(x => !string.IsNullOrEmpty(x.Status)).WithMessage("Status is not a work order status.");
        RuleFor(x => x.Priority).Must(p => Priorities.Contains(p))
            .When(x => !string.IsNullOrEmpty(x.Priority)).WithMessage("Priority must be P1, P2 or P3.");
        RuleFor(x => x.RegionId).GreaterThan(0).When(x => x.RegionId.HasValue);
        RuleFor(x => x.EngineerId).GreaterThan(0).When(x => x.EngineerId.HasValue);
    }
}

public class WorkOrderStatusRequestValidator : AbstractValidator<WorkOrderStatusRequest>
{
    public WorkOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status).Must(s => Corrin.Api.Domain.WorkOrderStatus.All.Contains(s)).WithMessage("Status is not a work order status.");
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(80);
    }
}

public class FirstTimeFixQueryValidator : AbstractValidator<FirstTimeFixQuery>
{
    private static readonly System.Text.RegularExpressions.Regex Month = new(@"^\d{4}-(0[1-9]|1[0-2])$");
    public FirstTimeFixQueryValidator()
    {
        RuleFor(x => x.From).Must(m => Month.IsMatch(m ?? "")).WithMessage("From must be a month in yyyy-MM format.");
        RuleFor(x => x.To).Must(m => Month.IsMatch(m ?? "")).WithMessage("To must be a month in yyyy-MM format.");
        RuleFor(x => x.To).Must((q, to) => string.CompareOrdinal(to, q.From) >= 0)
            .When(q => Month.IsMatch(q.From ?? "") && Month.IsMatch(q.To ?? ""))
            .WithMessage("To must not be before From.");
    }
}

public class ReorderRequestListQueryValidator : AbstractValidator<ReorderRequestListQuery>
{
    private static readonly string[] Statuses = { "Open", "Ordered", "Received" };
    public ReorderRequestListQueryValidator()
    {
        RuleFor(x => x.Status).Must(s => Statuses.Contains(s))
            .When(x => !string.IsNullOrEmpty(x.Status)).WithMessage("Status must be Open, Ordered or Received.");
        RuleFor(x => x.PartId).GreaterThan(0).When(x => x.PartId.HasValue);
    }
}

public class ReorderOrderedRequestValidator : AbstractValidator<ReorderOrderedRequest>
{
    public ReorderOrderedRequestValidator()
    {
        // Compared against the UTC date, consistent with the rest of the API's timestamps.
        RuleFor(x => x.ExpectedOn).Must(d => d >= DateOnly.FromDateTime(DateTime.UtcNow)).WithMessage("ExpectedOn must be today or later.");
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(80);
    }
}

public class ReorderReceivedRequestValidator : AbstractValidator<ReorderReceivedRequest>
{
    public ReorderReceivedRequestValidator()
    {
        RuleFor(x => x.Quantity).InclusiveBetween(1, 1000);
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(80);
    }
}
