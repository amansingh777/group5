using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Corrin.Api.Dtos;

namespace Corrin.Tests;

// Runs against the seeded database. No test here persists a successful transition: the successful ordered and received
// paths are covered by the rolled-back ReorderRepositoryTests. The POSTs below are rejected (400, 404 or 409) before any
// row changes, and the tests check that the row is unchanged afterwards.
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

    private static DateOnly FutureDate => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

    [Fact]
    public async Task List_Returns200WithTheTwoBackfilledOpenRequests()
    {
        var page = await ListAsync();

        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { 2, 17 }, page.Items.Select(i => i.PartId).OrderBy(id => id).ToArray());
        Assert.All(page.Items, i => Assert.Equal("Open", i.Status));
        Assert.All(page.Items, i => Assert.True(i.BelowReorderLevel));
        Assert.Contains(page.Items, i => i.PartId == 2 && i.PartSku == "DV-WB-01" && i.StockQty == 1 && i.ReorderLevel == 4);
        Assert.Contains(page.Items, i => i.PartId == 17 && i.PartSku == "HP-CTRL" && i.StockQty == 2 && i.ReorderLevel == 2);
    }

    [Fact]
    public async Task List_StatusOpen_ReturnsOnlyOpenRequests()
    {
        var page = await ListAsync("?status=Open");

        Assert.Equal(2, page.Total);
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
        var open = Assert.Single((await ListAsync("?status=Open&partId=2")).Items);

        var response = await _client.PostAsJsonAsync($"/api/reorder-requests/{open.Id}/ordered",
            new { expectedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), userName = "Test" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = Assert.Single((await ListAsync("?partId=2")).Items);
        Assert.Equal("Open", after.Status);
        Assert.Null(after.ExpectedOn);
        Assert.Null(after.OrderedBy);
    }

    [Fact]
    public async Task MarkOrdered_MissingUserName_Returns400AndLeavesRequestOpen()
    {
        var open = Assert.Single((await ListAsync("?status=Open&partId=17")).Items);

        var response = await _client.PostAsJsonAsync($"/api/reorder-requests/{open.Id}/ordered",
            new { expectedOn = FutureDate });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = Assert.Single((await ListAsync("?partId=17")).Items);
        Assert.Equal("Open", after.Status);
        Assert.Null(after.OrderedBy);
    }

    [Fact]
    public async Task MarkReceived_OpenRequest_Returns409AndLeavesRequestAndStockUnchanged()
    {
        var open = Assert.Single((await ListAsync("?status=Open&partId=2")).Items);
        var stockBefore = await _client.GetFromJsonAsync<PartDto>("/api/parts/2", Json);

        var response = await _client.PostAsJsonAsync($"/api/reorder-requests/{open.Id}/received",
            new { quantity = 5, userName = "Test" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = Assert.Single((await ListAsync("?partId=2")).Items);
        Assert.Equal("Open", after.Status);
        Assert.Null(after.ReceivedBy);
        var stockAfter = await _client.GetFromJsonAsync<PartDto>("/api/parts/2", Json);
        Assert.Equal(stockBefore!.StockQty, stockAfter!.StockQty);
        Assert.Equal(1, stockAfter.StockQty);
    }

    [Fact]
    public async Task MarkReceived_UnknownId_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/reorder-requests/999999/received",
            new { quantity = 1, userName = "Test" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
