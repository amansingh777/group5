using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Corrin.Api.Dtos;

namespace Corrin.Tests;

// Read-only: the report runs against the seeded database. Nothing here writes data.
[Collection("api")]
public class FirstTimeFixApiTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public FirstTimeFixApiTests(TestApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task FirstTimeFix_ValidRange_Returns200WithReportShapeAndRatesInRange()
    {
        // This also proves the grouped EF query translates against MySQL. A failure here is a production bug, not a test bug.
        var response = await _client.GetAsync("/api/reports/first-time-fix?from=2026-01&to=2026-12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("2026-01", root.GetProperty("from").GetString());
        Assert.Equal("2026-12", root.GetProperty("to").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("overall").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("byRegion").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("byJobType").ValueKind);

        var report = JsonSerializer.Deserialize<FirstTimeFixReportDto>(body, Json);
        Assert.NotNull(report);
        Assert.Equal("Overall", report!.Overall.Name);
        Assert.InRange(report.Overall.RatePercent, 0.0, 100.0);
        Assert.True(report.Overall.FixedFirstTime <= report.Overall.JobsAttended);
        Assert.All(report.ByRegion, r => Assert.InRange(r.RatePercent, 0.0, 100.0));
        Assert.All(report.ByJobType, t => Assert.InRange(t.RatePercent, 0.0, 100.0));
    }

    [Theory]
    [InlineData("from=2026-1&to=2026-12")]
    [InlineData("from=2026-01&to=2026-13")]
    [InlineData("from=26-01&to=2026-12")]
    [InlineData("from=2026-06&to=2026-05")]
    [InlineData("from=2026-01")]
    public async Task FirstTimeFix_InvalidQuery_Returns400(string query)
    {
        var response = await _client.GetAsync($"/api/reports/first-time-fix?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Migration 007 backfills one visit 1 per seeded Completed job with an outcome (207 jobs, all with actual_end in
    // September 2026). Legacy jobs have no later visits, so every backfilled Fixed visit counts as fixed first time.
    [Fact]
    public async Task FirstTimeFix_WideRangeWithBackfilledVisits_OverallIs207Attended152Fixed73Point4Percent()
    {
        var response = await _client.GetAsync("/api/reports/first-time-fix?from=2020-01&to=2030-12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<FirstTimeFixReportDto>(Json);
        Assert.NotNull(report);
        Assert.Equal(207, report!.Overall.JobsAttended);
        Assert.Equal(152, report.Overall.FixedFirstTime);
        Assert.Equal(73.4, report.Overall.RatePercent);
    }

    [Fact]
    public async Task FirstTimeFix_WideRangeWithBackfilledVisits_RegionAndJobTypeRowsSumToOverall()
    {
        var report = await _client.GetFromJsonAsync<FirstTimeFixReportDto>("/api/reports/first-time-fix?from=2020-01&to=2030-12", Json);

        Assert.NotNull(report);
        Assert.NotEmpty(report!.ByRegion);
        Assert.NotEmpty(report.ByJobType);
        Assert.Equal(report.Overall.JobsAttended, report.ByRegion.Sum(r => r.JobsAttended));
        Assert.Equal(report.Overall.FixedFirstTime, report.ByRegion.Sum(r => r.FixedFirstTime));
        Assert.Equal(report.Overall.JobsAttended, report.ByJobType.Sum(t => t.JobsAttended));
        Assert.Equal(report.Overall.FixedFirstTime, report.ByJobType.Sum(t => t.FixedFirstTime));
    }

    [Fact]
    public async Task WorkOrderVisits_BackfilledCompletedJob1_ReturnsOneFixedVisitNo1()
    {
        // Job 1 is Completed with outcome Fixed, engineer 7, actual end 2026-09-01 11:13. Its visit row mirrors those values.
        var visits = await _client.GetFromJsonAsync<List<VisitDto>>("/api/work-orders/1/visits", Json);

        var visit = Assert.Single(visits!);
        Assert.Equal(1, visit.VisitNo);
        Assert.Equal("Fixed", visit.Outcome);
        Assert.Equal(7, visit.EngineerId);
        Assert.Equal(new DateTime(2026, 9, 1, 11, 13, 0), visit.ActualEnd);
    }
}
