using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Corrin.Api.Dtos;

namespace Corrin.Tests;

[Collection("api")]
public class ApiTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ApiTests(TestApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Regions_ReturnsSixRegions()
    {
        var regions = await _client.GetFromJsonAsync<List<RegionDto>>("/api/regions", Json);
        Assert.Equal(6, regions!.Count);
    }

    [Fact]
    public async Task Customers_ReturnsFirstPageOfTwentyFive()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/customers", Json);
        Assert.Equal(25, page!.Items.Count);
        Assert.Equal(120, page.Total);
    }

    [Fact]
    public async Task Customer_Unknown_ReturnsProblemDetails404()
    {
        var response = await _client.GetAsync("/api/customers/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Engineers_FilteredByRegion_OnlyReturnsThatRegion()
    {
        var engineers = await _client.GetFromJsonAsync<List<EngineerDto>>("/api/engineers?regionId=1", Json);
        Assert.NotEmpty(engineers!);
        Assert.All(engineers!, e => Assert.Equal(1, e.RegionId));
    }

    [Fact]
    public async Task Jobs_DefaultPage_ContainsTwentyFiveJobs()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<JobDto>>("/api/jobs", Json);
        Assert.Equal(25, page!.Items.Count);
    }

    [Fact]
    public async Task CreateJob_RejectsEndBeforeStart()
    {
        var body = new
        {
            siteId = 1,
            jobTypeId = 1,
            priority = "P2",
            scheduledStart = "2026-11-02T10:00:00",
            scheduledEnd = "2026-11-02T09:00:00"
        };
        var response = await _client.PostAsJsonAsync("/api/jobs", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateShift_EndBeforeStart_Returns400()
    {
        var body = new { engineerId = 1, shiftDate = "2026-11-02", startTime = "16:00:00", endTime = "08:00:00", breakMinutes = 30, shiftType = "Standard" };
        var response = await _client.PostAsJsonAsync("/api/shifts", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Utilisation_ReturnsOneRowPerActiveEngineer()
    {
        var rows = await _client.GetFromJsonAsync<List<UtilisationRowDto>>("/api/reports/engineer-utilisation?from=2026-09-01&to=2026-09-30", Json);
        Assert.Equal(10, rows!.Count);
    }

    [Fact]
    public async Task Users_ReturnsList()
    {
        var response = await _client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LowStockParts_IsNotEmpty()
    {
        var parts = await _client.GetFromJsonAsync<List<PartDto>>("/api/parts/low-stock", Json);
        Assert.NotEmpty(parts!);
    }

    [Fact]
    public async Task Import_UnknownCustomer_RejectsTheRow()
    {
        var csv = "customer_ref,site_postcode,job_type,priority,requested_date,description\nCUS-00000,M1 1AA,BOILER_SERVICE,P3,2026-11-02,test\n";
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "rows.csv");

        var response = await _client.PostAsync("/api/imports/job-requests", form);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>(Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result!.RejectedRows);
        Assert.Equal(0, result.CreatedRows);
    }

    [Fact]
    public async Task Swagger_Loads()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
