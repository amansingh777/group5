using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Corrin.Api.Domain;
using Corrin.Api.Dtos;

namespace Corrin.Tests;

// Runs against the real seeded database (migrations 002 to 005). Facts used, all read from the seed:
//   243 jobs: 8 Requested, 14 Scheduled, 207 Completed, 14 Cancelled. 33 jobs in region 1; 21 are P1.
//   Job 235 (J-100705): Requested, P3, region 1, no engineer, no parts, one note ("Booked via contact centre.").
//   Job 1 (J-100003): Completed, outcome Fixed, one note, due 2026-09-02 04:30.
//   Engineers: 10 active, two in region 1 (ids 1 and 2). Shifts run 2026-09-01 to 2026-10-16.
//   Engineer 7 on 2026-09-01: shift 07:00 to 15:30, break 30 min; J-100003 booked 07:30 to 11:30 and J-100033 12:30 to 15:00.
// Only refused requests (400 and 409) are sent to the status endpoint, so no data is written. The successful move
// is covered by the mocked service tests.
[Collection("api")]
public class WorkOrderReadApiTests
{
    private const int RequestedJobId = 235;
    private const int CompletedJobId = 1;
    private const int SeededJobCount = 243;
    private const int RequestedCount = 8;
    private const int CancelledCount = 14;
    private const int CompletedCount = 207;
    private const int ScheduledCount = 14;
    private const int Region1Count = 33;
    private const int P1Count = 21;
    private const int EngineerCount = 10;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] SlaStates = { "none", "ok", "amber", "breached" };

    private readonly HttpClient _client;

    public WorkOrderReadApiTests(TestApiFactory factory) => _client = factory.CreateClient();

    private async Task<PagedResult<WorkOrderDto>> ListAsync(string query)
    {
        var response = await _client.GetAsync($"/api/work-orders?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<WorkOrderDto>>(Json))!;
    }

    private async Task<WorkOrderDto> GetAsync(int id)
    {
        var response = await _client.GetAsync($"/api/work-orders/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkOrderDto>(Json))!;
    }

    private async Task<DispatchBoardDto> BoardAsync(string query)
    {
        var response = await _client.GetAsync($"/api/dispatch/board?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DispatchBoardDto>(Json))!;
    }

    // ---- List ----

    [Fact]
    public async Task List_NoFilters_Returns200WithPagedShapeAndSlaStateOnEveryItem()
    {
        var response = await _client.GetAsync("/api/work-orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(25, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(SeededJobCount, root.GetProperty("total").GetInt32());
        Assert.Equal(25, root.GetProperty("items").GetArrayLength());
        Assert.All(root.GetProperty("items").EnumerateArray(), item =>
            Assert.Contains(item.GetProperty("slaState").GetString(), SlaStates));

        var paged = await response.Content.ReadFromJsonAsync<PagedResult<WorkOrderDto>>(Json);
        Assert.Equal(SeededJobCount, paged!.Total);
        // Final statuses never have an SLA state other than none.
        Assert.All(paged.Items.Where(i => WorkOrderStatus.IsFinal(i.Status)), i => Assert.Equal("none", i.SlaState));
    }

    [Theory]
    [InlineData("Requested", RequestedCount)]
    [InlineData("Scheduled", ScheduledCount)]
    [InlineData("Cancelled", CancelledCount)]
    [InlineData("Completed", CompletedCount)]
    public async Task List_StatusFilter_NarrowsResultsToThatStatus(string status, int expectedTotal)
    {
        var paged = await ListAsync($"status={status}&pageSize=100");

        Assert.Equal(expectedTotal, paged.Total);
        Assert.All(paged.Items, i => Assert.Equal(status, i.Status));
    }

    [Fact]
    public async Task List_PriorityFilter_NarrowsResultsToP1()
    {
        var paged = await ListAsync("priority=P1&pageSize=100");

        Assert.Equal(P1Count, paged.Total);
        Assert.All(paged.Items, i => Assert.Equal("P1", i.Priority));
    }

    [Fact]
    public async Task List_RegionFilter_NarrowsResultsToRegion1()
    {
        var paged = await ListAsync("regionId=1&pageSize=100");

        Assert.Equal(Region1Count, paged.Total);
        Assert.All(paged.Items, i => Assert.Equal(1, i.RegionId));
    }

    [Fact]
    public async Task List_CombinedStatusAndRegion_NarrowsToJob235Only()
    {
        // Region 1 has one Requested job, and that is job 235.
        var paged = await ListAsync("status=Requested&regionId=1");

        Assert.Equal(1, paged.Total);
        Assert.Equal(RequestedJobId, paged.Items.Single().Id);
    }

    [Fact]
    public async Task List_SecondPage_ReturnsADifferentSetOfRows()
    {
        var first = await ListAsync("page=1&pageSize=5");
        var second = await ListAsync("page=2&pageSize=5");

        Assert.Equal(5, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal(2, second.Page);
        Assert.Equal(SeededJobCount, second.Total);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task List_PageSizeAboveMax_IsClampedTo100()
    {
        var paged = await ListAsync("pageSize=500");

        Assert.Equal(100, paged.PageSize);
        Assert.Equal(100, paged.Items.Count);
    }

    [Fact]
    public async Task List_PageSizeZero_IsClampedToOne()
    {
        var paged = await ListAsync("pageSize=0");

        Assert.Equal(1, paged.PageSize);
        Assert.Single(paged.Items);
    }

    [Fact]
    public async Task List_PageZero_IsClampedToOne()
    {
        var paged = await ListAsync("page=0&pageSize=3");

        Assert.Equal(1, paged.Page);
        Assert.Equal(3, paged.Items.Count);
    }

    [Theory]
    [InlineData("status=Raised")]
    [InlineData("status=Bogus")]
    [InlineData("priority=P4")]
    [InlineData("priority=p1")]
    public async Task List_InvalidStatusOrPriority_Returns400(string query)
    {
        var response = await _client.GetAsync($"/api/work-orders?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Detail ----

    [Fact]
    public async Task Get_SeededJob235_Returns200WithNotesPartsAndNames()
    {
        var dto = await GetAsync(RequestedJobId);

        Assert.Equal(RequestedJobId, dto.Id);
        Assert.Equal("J-100705", dto.JobRef);
        Assert.Equal("Requested", dto.Status);
        Assert.Equal("P3", dto.Priority);
        Assert.Equal(1, dto.RegionId);
        Assert.Equal("Freddie Wilson", dto.CustomerName);
        Assert.Equal("Freddie Wilson", dto.SiteName);
        Assert.Equal("Annual boiler service", dto.JobType);
        Assert.Equal("Gas Safe - boilers and appliances", dto.RequiredSkill);
        Assert.Null(dto.EngineerId);
        Assert.Null(dto.AssetSummary);
        Assert.Single(dto.Notes);
        Assert.Equal("Booked via contact centre.", dto.Notes[0].Note);
        Assert.Equal("Marcus Teague", dto.Notes[0].Author);
        Assert.Empty(dto.Parts);
        // Due 2026-10-03, so the open job is past its SLA. This depends on the clock being after that date.
        Assert.Equal("breached", dto.SlaState);
    }

    [Fact]
    public async Task Get_SeededCompletedJob_HasSlaStateNoneAndOutcome()
    {
        var dto = await GetAsync(CompletedJobId);

        Assert.Equal("Completed", dto.Status);
        Assert.Equal("Fixed", dto.Outcome);
        Assert.Equal("none", dto.SlaState);
        Assert.Single(dto.Notes);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/api/work-orders/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }

    // ---- Status ----

    [Fact]
    public async Task ChangeStatus_UnknownStatus_Returns400AndJobUnchanged()
    {
        var before = await GetAsync(RequestedJobId);

        var response = await _client.PostAsJsonAsync($"/api/work-orders/{RequestedJobId}/status",
            new { status = "Bogus", userName = "api-test" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await GetAsync(RequestedJobId);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Notes.Count, after.Notes.Count);
    }

    [Fact]
    public async Task ChangeStatus_MissingUserName_Returns400()
    {
        var response = await _client.PostAsJsonAsync($"/api/work-orders/{RequestedJobId}/status",
            new { status = "Cancelled", userName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Triaged")]
    [InlineData("Allocated")]
    [InlineData("Scheduled")]
    [InlineData("OnSite")]
    [InlineData("Completed")]
    [InlineData("NotCompleted")]
    [InlineData("Cancelled")]
    public async Task ChangeStatus_CompletedJobToAnyStatus_Returns409AndJobUnchanged(string target)
    {
        // Completed is final (R3), so every target is refused. The refusal happens before any write.
        var before = await GetAsync(CompletedJobId);

        var response = await _client.PostAsJsonAsync($"/api/work-orders/{CompletedJobId}/status",
            new { status = target, userName = "api-test" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        var after = await GetAsync(CompletedJobId);
        Assert.Equal("Completed", after.Status);
        Assert.Equal(before.Notes.Count, after.Notes.Count);
    }

    [Theory]
    [InlineData("Allocated")]
    [InlineData("Scheduled")]
    [InlineData("OnSite")]
    [InlineData("Completed")]
    [InlineData("NotCompleted")]
    public async Task ChangeStatus_RequestedJobToStatusOnlyReachedByOtherEndpoints_Returns409AndJobUnchanged(string target)
    {
        // Allocated, Scheduled, OnSite and the outcomes are reached through allocate, schedule and visit endpoints (R2).
        var before = await GetAsync(RequestedJobId);

        var response = await _client.PostAsJsonAsync($"/api/work-orders/{RequestedJobId}/status",
            new { status = target, userName = "api-test" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = await GetAsync(RequestedJobId);
        Assert.Equal("Requested", after.Status);
        Assert.Equal(before.Notes.Count, after.Notes.Count);
    }

    // ---- Dispatch board ----

    [Fact]
    public async Task Board_January2026Window_Returns200WithSevenCellsPerEngineerAndUnscheduled()
    {
        var board = await BoardAsync("from=2026-01-05&days=7");

        Assert.Equal(new DateOnly(2026, 1, 5), board.From);
        Assert.Equal(7, board.Days);
        Assert.Equal(EngineerCount, board.Engineers.Count);
        var expectedDates = Enumerable.Range(0, 7).Select(i => new DateOnly(2026, 1, 5).AddDays(i)).ToList();
        Assert.All(board.Engineers, row =>
        {
            Assert.Equal(7, row.Cells.Count);
            Assert.Equal(expectedDates, row.Cells.Select(c => c.Date));
        });
        // No shifts or bookings in January 2026 in the seed.
        Assert.All(board.Engineers.SelectMany(e => e.Cells), c =>
        {
            Assert.Null(c.ShiftStart);
            Assert.Empty(c.Jobs);
        });
        Assert.Equal(RequestedCount, board.Unscheduled.Count);
        Assert.All(board.Unscheduled, j => Assert.Equal("Requested", j.Status));
        Assert.All(board.Unscheduled, j => Assert.False(j.NeedsRebook));
    }

    [Fact]
    public async Task Board_DaysZero_IsClampedToOneDay()
    {
        var board = await BoardAsync("from=2026-01-05&days=0");

        Assert.Equal(1, board.Days);
        Assert.All(board.Engineers, row => Assert.Single(row.Cells));
    }

    [Fact]
    public async Task Board_DaysNinetyNine_IsClampedToFourteenDays()
    {
        var board = await BoardAsync("from=2026-01-05&days=99");

        Assert.Equal(14, board.Days);
        Assert.All(board.Engineers, row => Assert.Equal(14, row.Cells.Count));
    }

    [Fact]
    public async Task Board_RegionFilter_NarrowsEngineersAndUnscheduled()
    {
        var board = await BoardAsync("from=2026-01-05&days=7&regionId=1");

        Assert.Equal(new[] { 1, 2 }, board.Engineers.Select(e => e.EngineerId).OrderBy(x => x));
        Assert.All(board.Engineers, e => Assert.Equal(1, e.RegionId));
        var unscheduled = Assert.Single(board.Unscheduled);
        Assert.Equal(RequestedJobId, unscheduled.Id);
    }

    [Fact]
    public async Task Board_SeptemberWindow_ShowsShiftsAndBookedJobInTheRightCell()
    {
        var board = await BoardAsync("from=2026-09-01&days=1");

        var engineer7 = board.Engineers.Single(e => e.EngineerId == 7);
        var cell = engineer7.Cells.Single();
        Assert.Equal(new DateOnly(2026, 9, 1), cell.Date);
        Assert.Equal(new TimeOnly(7, 0), cell.ShiftStart);
        Assert.Equal(new TimeOnly(15, 30), cell.ShiftEnd);
        Assert.Equal(30, cell.BreakMinutes);

        // Two bookings that day, in start order (job 1 at 07:30 then job 11 at 12:30).
        Assert.Equal(new[] { "J-100003", "J-100033" }, cell.Jobs.Select(j => j.JobRef));
        var first = cell.Jobs[0];
        Assert.Equal("Completed", first.Status);
        Assert.Equal(new DateTime(2026, 9, 1, 7, 30, 0), first.Start);
        Assert.Equal(new DateTime(2026, 9, 1, 11, 30, 0), first.End);
        Assert.Equal("none", first.SlaState);
        Assert.False(first.NeedsRebook);
        Assert.Equal(new DateTime(2026, 9, 1, 12, 30, 0), cell.Jobs[1].Start);

        var engineer1 = board.Engineers.Single(e => e.EngineerId == 1).Cells.Single();
        Assert.Equal(new TimeOnly(8, 0), engineer1.ShiftStart);
        Assert.Equal(new TimeOnly(16, 30), engineer1.ShiftEnd);
    }
}
