using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Corrin.Api.Dtos;

namespace Corrin.Tests;

// Runs against the seeded database. No test here persists a successful transition: the successful ordered and received
// paths are covered by the rolled-back ReorderRepositoryTests. The POSTs below are rejected (400, 404 or 409) before any
// row changes, and the tests check that the row is unchanged afterwards.
//
// The database is live, so these tests do not assume which parts are Open: a user can move a request on (for example to
// Ordered) at any time. They take whichever Open request exists at the start of the test and fail with a clear message
// if none does.
[Collection("api")]
public class ReorderApiTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ReorderApiTests(TestApiFactory factory) => _client = factory.CreateClient();

    private async Task<PagedResult<ReorderRequestDto>> ListAsync(string query = "")
    {
        var response = await _client.GetAsync("/api/reorder-requests" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<ReorderRequestDto>>(Json))!;
    }

    private async Task<ReorderRequestDto> AnyOpenRequestAsync()
    {
        var page = await ListAsync("?status=Open&pageSize=1");
        Assert.NotEmpty(page.Items);
        return page.Items[0];
    }

    private async Task<ReorderRequestDto> RowAsync(ReorderRequestDto expected)
    {
        var page = await ListAsync($"?partId={expected.PartId}");
        return Assert.Single(page.Items, i => i.Id == expected.Id);
    }

    private static DateOnly FutureDate => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

    [Fact]
    public async Task List_Returns200WithItemsThatAgreeWithTheirStockLevels()
    {
        var page = await ListAsync();

        Assert.True(page.Total >= page.Items.Count);
        Assert.All(page.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.PartSku)));
        Assert.All(page.Items, i => Assert.Equal(i.StockQty <= i.ReorderLevel, i.BelowReorderLevel));
    }

    [Fact]
    public async Task List_StatusOpen_ReturnsOnlyOpenRequests()
    {
        var page = await ListAsync("?status=Open");

        Assert.True(page.Total >= page.Items.Count);
        Assert.All(page.Items, i => Assert.Equal("Open", i.Status));
    }

    [Fact]
    public async Task List_PartIdTwo_ReturnsOnlyPartTwo()
    {
        var page = await ListAsync("?partId=2");

        var item = Assert.Single(page.Items);
        Assert.Equal(2, item.PartId);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task List_UnknownStatus_Returns400()
    {
        var response = await _client.GetAsync("/api/reorder-requests?status=Bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MarkOrdered_UnknownId_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/reorder-requests/999999/ordered",
            new { expectedOn = FutureDate, userName = "Test" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MarkOrdered_PastExpectedOn_Returns400AndLeavesRequestOpen()
    {
        var open = await AnyOpenRequestAsync();

        var response = await _client.PostAsJsonAsync($"/api/reorder-requests/{open.Id}/ordered",
            new { expectedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), userName = "Test" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await RowAsync(open);
        Assert.Equal("Open", after.Status);
        Assert.Null(after.ExpectedOn);
        Assert.Null(after.OrderedBy);
    }

    [Fact]
    public async Task MarkOrdered_MissingUserName_Returns400AndLeavesRequestOpen()
    {
        var open = await AnyOpenRequestAsync();

        var response = await _client.PostAsJsonAsync($"/api/reorder-requests/{open.Id}/ordered",
            new { expectedOn = FutureDate });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await RowAsync(open);
        Assert.Equal("Open", after.Status);
        Assert.Null(after.OrderedBy);
    }

    [Fact]
    public async Task MarkReceived_OpenRequest_Returns409AndLeavesRequestAndStockUnchanged()
    {
        var open = await AnyOpenRequestAsync();
        var stockBefore = await _client.GetFromJsonAsync<PartDto>($"/api/parts/{open.PartId}", Json);

        var response = await _client.PostAsJsonAsync($"/api/reorder-requests/{open.Id}/received",
            new { quantity = 5, userName = "Test" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = await RowAsync(open);
        Assert.Equal("Open", after.Status);
        Assert.Null(after.ReceivedBy);
        var stockAfter = await _client.GetFromJsonAsync<PartDto>($"/api/parts/{open.PartId}", Json);
        Assert.Equal(stockBefore!.StockQty, stockAfter!.StockQty);
    }

    [Fact]
    public async Task MarkReceived_UnknownId_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/reorder-requests/999999/received",
            new { quantity = 1, userName = "Test" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
