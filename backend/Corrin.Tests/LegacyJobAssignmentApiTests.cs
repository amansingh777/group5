using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Corrin.Api.Dtos;

namespace Corrin.Tests;

// Legacy POST /api/jobs and PUT /api/jobs/{id} against the seeded database. Every case here is refused before any write,
// so the seed is not changed. Job 235 (J-100705) is Requested, has no engineer, and its job type (1, BOILER_SERVICE)
// requires GAS (skill 1). Engineer 2 (Zainab Iqbal) is active and does not hold GAS. Site 142 belongs to customer 103.
[Collection("api")]
public class LegacyJobAssignmentApiTests
{
    private const int GasRequestedJobId = 235;
    private const int EngineerWithoutGasId = 2;
    private const int CustomerId = 103;
    private const int SiteId = 142;
    private const int BoilerServiceJobTypeId = 1;

    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public LegacyJobAssignmentApiTests(TestApiFactory factory) => _client = factory.CreateClient();

    private async Task<int> JobCountAsync()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<JobDto>>("/api/jobs?page=1&pageSize=1", Json);
        return page!.Total;
    }

    [Fact]
    public async Task CreateJob_EngineerWithoutRequiredSkill_Returns422AndNothingIsPersisted()
    {
        var before = await JobCountAsync();
        var body = new { customerId = CustomerId, siteId = SiteId, jobTypeId = BoilerServiceJobTypeId, engineerId = EngineerWithoutGasId, priority = "P3" };

        var response = await _client.PostAsJsonAsync("/api/jobs", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("The engineer does not hold the skill required for this job type.", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await JobCountAsync());
    }

    [Fact]
    public async Task UpdateJob_EngineerWithoutRequiredSkill_Returns422AndJobIsUnchanged()
    {
        var body = new { engineerId = EngineerWithoutGasId, priority = "P3", status = "Requested" };

        var response = await _client.PutAsJsonAsync($"/api/jobs/{GasRequestedJobId}", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var job = await _client.GetFromJsonAsync<JobDto>($"/api/jobs/{GasRequestedJobId}", Json);
        Assert.Equal("Requested", job!.Status);
        Assert.Null(job.EngineerId);
        Assert.Null(job.ScheduledStart);
    }

    [Fact]
    public async Task UpdateJob_InProgressStatus_Returns400AndJobIsUnchanged()
    {
        // "InProgress" is not a work-order status any more (Domain/WorkOrderStatus.All), so the validator refuses it.
        var body = new { priority = "P3", status = "InProgress" };

        var response = await _client.PutAsJsonAsync($"/api/jobs/{GasRequestedJobId}", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var job = await _client.GetFromJsonAsync<JobDto>($"/api/jobs/{GasRequestedJobId}", Json);
        Assert.Equal("Requested", job!.Status);
    }
}
