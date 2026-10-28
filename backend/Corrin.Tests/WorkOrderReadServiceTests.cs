using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Corrin.Api.Validators;
using Moq;

namespace Corrin.Tests;

public class WorkOrderReadServiceTests
{
    private static readonly DateTime Created = new(2030, 1, 1, 9, 0, 0);

    private readonly Mock<IWorkOrderReadRepository> _reads = new();

    public WorkOrderReadServiceTests()
    {
        _reads.Setup(r => r.ListAsync(It.IsAny<WorkOrderFilter>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((new List<WorkOrderRow>(), 0));
        _reads.Setup(r => r.NotesForJobsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<JobNote>());
        _reads.Setup(r => r.PartsForJobsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<JobPart>());
    }

    private WorkOrderService Build() => new(_reads.Object, new Mock<IJobRepository>().Object);

    private static WorkOrderRow Row(int id, string status = "Requested", string priority = "P3", DateTime? slaDueAt = null,
        DateTime? createdAt = null, string? assetType = null) => new(id, $"J-{id}", 1, "Customer", "Site", "LS6 1AA",
        assetType, assetType is null ? null : "Worcester", assetType is null ? null : "Greenstar 30i",
        "Boiler service", "Gas Safe", priority, status, null, null, null, null, slaDueAt, createdAt ?? Created, null, null);

    [Theory]
    [InlineData(500, 100)]
    [InlineData(101, 100)]
    [InlineData(100, 100)]
    [InlineData(25, 25)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public async Task ListAsync_PageSize_IsClampedToOneThroughOneHundred(int requested, int expected)
    {
        var result = await Build().ListAsync(new WorkOrderListQuery(PageSize: requested));

        Assert.Equal(expected, result.PageSize);
        _reads.Verify(r => r.ListAsync(It.IsAny<WorkOrderFilter>(), 1, expected), Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    public async Task ListAsync_Page_IsAtLeastOne(int requested, int expected)
    {
        var result = await Build().ListAsync(new WorkOrderListQuery(Page: requested));

        Assert.Equal(expected, result.Page);
        _reads.Verify(r => r.ListAsync(It.IsAny<WorkOrderFilter>(), expected, 25), Times.Once);
    }

    [Fact]
    public async Task ListAsync_Filters_ArePassedToTheRepositoryUnchanged()
    {
        await Build().ListAsync(new WorkOrderListQuery(Page: 2, PageSize: 10, Status: "Scheduled", RegionId: 2, EngineerId: 9, Priority: "P1"));

        _reads.Verify(r => r.ListAsync(It.Is<WorkOrderFilter>(f =>
            f.Status == "Scheduled" && f.RegionId == 2 && f.EngineerId == 9 && f.Priority == "P1"), 2, 10), Times.Once);
    }

    [Fact]
    public async Task ListAsync_NoRows_ReturnsEmptyPageWithZeroTotal()
    {
        var result = await Build().ListAsync(new WorkOrderListQuery());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(25, result.PageSize);
        _reads.Verify(r => r.NotesForJobsAsync(It.Is<IEnumerable<int>>(ids => !ids.Any())), Times.Once);
    }

    [Fact]
    public async Task ListAsync_TotalComesFromRepositoryNotFromPageLength()
    {
        _reads.Setup(r => r.ListAsync(It.IsAny<WorkOrderFilter>(), 1, 25))
            .ReturnsAsync((new List<WorkOrderRow> { Row(1) }, 57));

        var result = await Build().ListAsync(new WorkOrderListQuery());

        Assert.Single(result.Items);
        Assert.Equal(57, result.Total);
    }

    [Fact]
    public async Task ListAsync_MapsNotesAndPartsOnlyToTheirOwnJob_AndComputesSlaPerRow()
    {
        var now = DateTime.UtcNow;
        _reads.Setup(r => r.ListAsync(It.IsAny<WorkOrderFilter>(), 1, 25))
            .ReturnsAsync((new List<WorkOrderRow>
            {
                Row(1, status: "Completed", slaDueAt: now.AddDays(-1)),
                Row(2, status: "Scheduled", slaDueAt: now.AddDays(-1), createdAt: now.AddDays(-3)),
            }, 2));
        _reads.Setup(r => r.NotesForJobsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<JobNote>
        {
            new() { Id = 1, JobId = 1, Author = "a", Note = "first", CreatedAt = Created },
            new() { Id = 2, JobId = 1, Author = "b", Note = "second", CreatedAt = Created },
        });
        _reads.Setup(r => r.PartsForJobsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<JobPart>
        {
            new() { Id = 7, JobId = 2, PartId = 3, Quantity = 4 },
        });

        var result = await Build().ListAsync(new WorkOrderListQuery());

        Assert.Equal(new[] { 1, 2 }, result.Items.Select(i => i.Id));
        Assert.Equal(new[] { "first", "second" }, result.Items[0].Notes.Select(n => n.Note));
        Assert.Empty(result.Items[1].Notes);
        Assert.Empty(result.Items[0].Parts);
        Assert.Equal(new[] { 3 }, result.Items[1].Parts.Select(p => p.PartId));
        Assert.Equal(4, result.Items[1].Parts[0].Quantity);
        Assert.Equal(SlaState.None, result.Items[0].SlaState);
        Assert.Equal(SlaState.Breached, result.Items[1].SlaState);
    }

    [Fact]
    public async Task ListAsync_RowWithNoAsset_HasNullAssetSummary()
    {
        _reads.Setup(r => r.ListAsync(It.IsAny<WorkOrderFilter>(), 1, 25))
            .ReturnsAsync((new List<WorkOrderRow> { Row(1), Row(2, assetType: "Boiler") }, 2));

        var result = await Build().ListAsync(new WorkOrderListQuery());

        Assert.Null(result.Items[0].AssetSummary);
        Assert.Equal("Boiler Worcester Greenstar 30i", result.Items[1].AssetSummary);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ThrowsNotFound()
    {
        _reads.Setup(r => r.GetAsync(999)).ReturnsAsync((WorkOrderRow?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Build().GetAsync(999));

        Assert.Equal("Work order 999 was not found.", ex.Message);
    }

    [Fact]
    public async Task GetAsync_KnownId_ReturnsRowWithNotesPartsAndAmberSla()
    {
        // Created two days ago, due in one hour: about 1 hour of a 2 day window is left, so amber.
        var now = DateTime.UtcNow;
        _reads.Setup(r => r.GetAsync(5)).ReturnsAsync(Row(5, status: "Scheduled", slaDueAt: now.AddHours(1),
            createdAt: now.AddDays(-2), assetType: "Boiler"));
        _reads.Setup(r => r.NotesForJobsAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 5 })))).ReturnsAsync(new List<JobNote>
        {
            new() { Id = 11, JobId = 5, Author = "dispatcher1", Note = "Called customer", CreatedAt = Created },
        });
        _reads.Setup(r => r.PartsForJobsAsync(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 5 })))).ReturnsAsync(new List<JobPart>
        {
            new() { Id = 12, JobId = 5, PartId = 2, Quantity = 1 },
        });

        var dto = await Build().GetAsync(5);

        Assert.Equal(5, dto.Id);
        Assert.Equal("Scheduled", dto.Status);
        Assert.Equal("Customer", dto.CustomerName);
        Assert.Equal("Site", dto.SiteName);
        Assert.Equal("Boiler Worcester Greenstar 30i", dto.AssetSummary);
        Assert.Equal(SlaState.Amber, dto.SlaState);
        Assert.Single(dto.Notes);
        Assert.Equal("Called customer", dto.Notes[0].Note);
        Assert.Single(dto.Parts);
        Assert.Equal(2, dto.Parts[0].PartId);
    }

    [Fact]
    public async Task GetAsync_NoNotesOrParts_ReturnsEmptyArrays()
    {
        _reads.Setup(r => r.GetAsync(6)).ReturnsAsync(Row(6, status: "Completed"));

        var dto = await Build().GetAsync(6);

        Assert.Empty(dto.Notes);
        Assert.Empty(dto.Parts);
        Assert.Equal(SlaState.None, dto.SlaState);
    }
}

public class WorkOrderListQueryValidatorTests
{
    private readonly WorkOrderListQueryValidator _validator = new();

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Validate_EveryKnownStatusFilter_IsAccepted(string status)
    {
        Assert.True(_validator.Validate(new WorkOrderListQuery(Status: status)).IsValid);
    }

    public static IEnumerable<object[]> AllStatuses() => WorkOrderStatus.All.Select(s => new object[] { s });

    [Theory]
    [InlineData("Raised")]
    [InlineData("Bogus")]
    [InlineData("scheduled")]
    public void Validate_UnknownStatusFilter_IsRejected(string status)
    {
        var result = _validator.Validate(new WorkOrderListQuery(Status: status));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WorkOrderListQuery.Status));
    }

    [Theory]
    [InlineData("P1")]
    [InlineData("P2")]
    [InlineData("P3")]
    public void Validate_KnownPriorityFilter_IsAccepted(string priority)
    {
        Assert.True(_validator.Validate(new WorkOrderListQuery(Priority: priority)).IsValid);
    }

    [Theory]
    [InlineData("P4")]
    [InlineData("p1")]
    [InlineData("High")]
    public void Validate_UnknownPriorityFilter_IsRejected(string priority)
    {
        var result = _validator.Validate(new WorkOrderListQuery(Priority: priority));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(WorkOrderListQuery.Priority));
    }

    [Fact]
    public void Validate_NoFilters_IsAccepted()
    {
        Assert.True(_validator.Validate(new WorkOrderListQuery()).IsValid);
    }

    [Fact]
    public void Validate_EmptyStatusAndPriority_MeansNoFilter_AndIsAccepted()
    {
        Assert.True(_validator.Validate(new WorkOrderListQuery(Status: "", Priority: "")).IsValid);
    }

    [Fact]
    public void Validate_ZeroRegionId_IsRejected()
    {
        Assert.False(_validator.Validate(new WorkOrderListQuery(RegionId: 0)).IsValid);
    }
}
