using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Corrin.Api.Dtos;

namespace Corrin.Tests;

// Seeded data (migrations 002 to 005): job 235 (J-100705) is Requested, job type 1 requires skill 1 (GAS),
// and job site is in region 1. Engineer 2 (Zainab Iqbal) is active in region 1 and does not hold GAS.
// Nine active engineers hold GAS. The 422 path is rejected before any write, so no data is created or changed.
[Collection("api")]
public class WorkOrderApiTests
{
    private const int GasRequestedJobId = 235;
    private const int EngineerWithoutGasId = 2;
    private const int GasEngineerCount = 9;

    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public WorkOrderApiTests(TestApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Candidates_SeededGasJob_Returns200WithRankedGasEngineers()
    {
        var response = await _client.GetAsync($"/api/work-orders/{GasRequestedJobId}/candidates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var candidates = await response.Content.ReadFromJsonAsync<List<WorkOrderCandidateDto>>(Json);

        Assert.Equal(GasEngineerCount, candidates!.Count);
        Assert.Equal(Enumerable.Range(1, GasEngineerCount), candidates.Select(c => c.Rank));
        // The engineer without GAS must never be offered, and there is no override in the response.
        Assert.DoesNotContain(candidates, c => c.EngineerId == EngineerWithoutGasId);
        // Same region (job is in region 1) ranks ahead of any other region.
        var sameRegionFlags = candidates.Select(c => c.RegionId == 1).ToList();
        Assert.Equal(sameRegionFlags.OrderByDescending(f => f).ToList(), sameRegionFlags);
        Assert.All(candidates, c => Assert.False(string.IsNullOrWhiteSpace(c.RankReason)));
    }

    [Fact]
    public async Task Candidates_UnknownJob_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/api/work-orders/999999/candidates");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Allocate_BoilerGasJobToEngineerWithoutGasSkill_Returns422AndJobUnchanged()
    {
        var body = new { engineerId = EngineerWithoutGasId, userName = "api-test", date = "2026-11-02" };

        var response = await _client.PostAsJsonAsync($"/api/work-orders/{GasRequestedJobId}/allocate", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var job = await _client.GetFromJsonAsync<JobDto>($"/api/jobs/{GasRequestedJobId}", Json);
        Assert.Equal("Requested", job!.Status);
        Assert.Null(job.EngineerId);
    }

    [Fact]
    public async Task Allocate_UnknownJob_Returns404()
    {
        var body = new { engineerId = EngineerWithoutGasId, userName = "api-test", date = "2026-11-02" };

        var response = await _client.PostAsJsonAsync("/api/work-orders/999999/allocate", body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Visit endpoints below are error paths only. Each one is refused before any write, so the seeded data stays as it was.

    [Fact]
    public async Task Visits_UnknownJob_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/api/work-orders/999999/visits");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Visits_SeededJobWithNoVisitRows_ReturnsEmptyList()
    {
        // Job 235 is Requested. Migration 007 backfills visit 1 only for Completed jobs, so this job still has no visits.
        // The repository tests roll back everything they write.
        var visits = await _client.GetFromJsonAsync<List<VisitDto>>($"/api/work-orders/{GasRequestedJobId}/visits", Json);

        Assert.Empty(visits!);
    }

    [Fact]
    public async Task StartVisit_UnknownJob_Returns404()
    {
        var response = await _client.PostAsync("/api/work-orders/999999/visits/start", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartVisit_SeededRequestedJob_Returns422AndJobUnchanged()
    {
        // Job 235 is Requested, not Scheduled, so it cannot be started.
        var response = await _client.PostAsync($"/api/work-orders/{GasRequestedJobId}/visits/start", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var job = await _client.GetFromJsonAsync<JobDto>($"/api/jobs/{GasRequestedJobId}", Json);
        Assert.Equal("Requested", job!.Status);
    }

    [Fact]
    public async Task CompleteVisit_OutcomeNotInVocabulary_Returns400()
    {
        var body = new { outcome = "Done", userName = "api-test" };

        var response = await _client.PostAsJsonAsync($"/api/work-orders/{GasRequestedJobId}/visits/complete", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompleteVisit_PartQuantityZero_Returns400()
    {
        var body = new { outcome = "Fixed", userName = "api-test", parts = new[] { new { partId = 1, quantity = 0 } } };

        var response = await _client.PostAsJsonAsync($"/api/work-orders/{GasRequestedJobId}/visits/complete", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompleteVisit_SeededRequestedJobWithNoVisit_Returns422()
    {
        // Valid request, but the job is not OnSite and has no visit in progress.
        var body = new { outcome = "Fixed", userName = "api-test" };

        var response = await _client.PostAsJsonAsync($"/api/work-orders/{GasRequestedJobId}/visits/complete", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CompleteVisit_UnknownJob_Returns404()
    {
        var body = new { outcome = "Fixed", userName = "api-test" };

        var response = await _client.PostAsJsonAsync("/api/work-orders/999999/visits/complete", body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
