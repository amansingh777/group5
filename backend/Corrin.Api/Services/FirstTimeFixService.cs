using System.Globalization;
using Corrin.Api.Dtos;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IFirstTimeFixService
{
    Task<FirstTimeFixReportDto> GetAsync(FirstTimeFixQuery query);
}

// Separate from the utilisation report (spec R23). Rates come from visit history, never Job.FirstTimeFix.
public class FirstTimeFixService : IFirstTimeFixService
{
    private readonly IFirstTimeFixRepository _repo;
    public FirstTimeFixService(IFirstTimeFixRepository repo) => _repo = repo;

    public async Task<FirstTimeFixReportDto> GetAsync(FirstTimeFixQuery query)
    {
        var start = DateTime.ParseExact(query.From + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateTime.ParseExact(query.To + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(1);

        var cohort = await _repo.GetCohortCountsAsync(start, end);

        var byRegion = cohort
            .GroupBy(r => (r.RegionId, r.RegionName))
            .OrderBy(g => g.Key.RegionName)
            .Select(g => Row(g.Key.RegionName, g.Sum(x => x.JobsAttended), g.Sum(x => x.FixedFirstTime)))
            .ToList();

        var byJobType = cohort
            .GroupBy(r => (r.JobTypeId, r.JobTypeName))
            .OrderBy(g => g.Key.JobTypeName)
            .Select(g => Row(g.Key.JobTypeName, g.Sum(x => x.JobsAttended), g.Sum(x => x.FixedFirstTime)))
            .ToList();

        var overall = Row("Overall", cohort.Sum(x => x.JobsAttended), cohort.Sum(x => x.FixedFirstTime));

        return new FirstTimeFixReportDto(query.From, query.To, overall, byRegion, byJobType);
    }

    private static FirstTimeFixRowDto Row(string name, int attended, int fixedFirstTime)
    {
        var rate = attended == 0 ? 0 : Math.Round(fixedFirstTime * 100.0 / attended, 1, MidpointRounding.AwayFromZero);
        return new FirstTimeFixRowDto(name, attended, fixedFirstTime, rate);
    }
}
