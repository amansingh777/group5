using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Corrin.Api.Dtos;

namespace Corrin.Tests;

// Legacy PUT /api/jobs/{id} and POST /api/jobs against the seeded database. Every case here is refused before any write
// (a 409 from the visit-status guard, or a 400 from the validator), so the seed is not changed. Job 235 (J-100705) is
// Requested, has no engineer and no booking. Customer 103 and site 142 are the same pair LegacyJobAssignmentApiTests uses.
[Collection("api")]
public class LegacyJobGuardApiTests
{
    private const int RequestedJobId = 235;
    private const int CustomerId = 103;
    private const int SiteId = 142;
    private const int BoilerServiceJobTypeId = 1;

    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public LegacyJobGuardApiTests(TestApiFactory factory) => _client = factory.CreateClient();

    private async Task<int> JobCountAsync()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<JobDto>>("/api/jobs?page=1&pageSize=1", Json);
        return page!.Total;
    }

    [Fact]
    public async Task UpdateJob_MoveToCompletedThroughPut_Returns409AndJobIsUnchanged()
    {
        var body = new { priority = "P3", status = "Completed" };

        var response = await _client.PutAsJsonAsync($"/api/jobs/{RequestedJobId}", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        Assert.Contains($"A work order cannot be moved to Completed through PUT /api/jobs/{RequestedJobId}.",
            await response.Content.ReadAsStringAsync());
        var job = await _client.GetFromJsonAsync<JobDto>($"/api/jobs/{RequestedJobId}", Json);
        Assert.Equal("Requested", job!.Status);
    }

    [Fact]
    public async Task UpdateJob_OnlyScheduledStart_Returns400AndJobIsUnchanged()
    {
        var body = new { priority = "P3", status = "Requested", scheduledStart = new DateTime(2030, 1, 7, 10, 0, 0) };

        var response = await _client.PutAsJsonAsync($"/api/jobs/{RequestedJobId}", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Give both a scheduled start and end, or neither.", await response.Content.ReadAsStringAsync());
        var job = await _client.GetFromJsonAsync<JobDto>($"/api/jobs/{RequestedJobId}", Json);
        Assert.Equal("Requested", job!.Status);
        Assert.Null(job.ScheduledStart);
        Assert.Null(job.ScheduledEnd);
    }

    [Fact]
    public async Task CreateJob_OnlyScheduledStart_Returns400AndJobCountIsUnchanged()
    {
        var before = await JobCountAsync();
        var body = new
        {
            customerId = CustomerId,
            siteId = SiteId,
            jobTypeId = BoilerServiceJobTypeId,
            priority = "P3",
            scheduledStart = new DateTime(2030, 1, 7, 10, 0, 0)
        };

        var response = await _client.PostAsJsonAsync("/api/jobs", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Give both a scheduled start and end, or neither.", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await JobCountAsync());
    }
}
