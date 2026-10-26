using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface ISiteService
{
    Task<IReadOnlyList<SiteDto>> ListAsync(int? regionId, int? customerId);
    Task<SiteDto> GetAsync(int id);
    Task<SiteDto> CreateAsync(SiteRequest request);
    Task<SiteDto> UpdateAsync(int id, SiteRequest request);
    Task<IReadOnlyList<AssetDto>> GetAssetsAsync(int id);
}

public class SiteService : ISiteService
{
    private readonly ISiteRepository _sites;
    private readonly ICustomerRepository _customers;

    public SiteService(ISiteRepository sites, ICustomerRepository customers)
    {
        _sites = sites;
        _customers = customers;
    }

    public async Task<IReadOnlyList<SiteDto>> ListAsync(int? regionId, int? customerId) =>
        (await _sites.ListAsync(regionId, customerId)).Select(s => s.ToDto()).ToList();

    public async Task<SiteDto> GetAsync(int id) =>
        (await _sites.GetReadOnlyAsync(id) ?? throw new NotFoundException("Site", id)).ToDto();

    public async Task<SiteDto> CreateAsync(SiteRequest request)
    {
        if (await _customers.GetAsync(request.CustomerId) is null) throw new BusinessRuleException("Customer does not exist.");
        var site = new Site { CreatedAt = DateTime.UtcNow };
        site.Apply(request);
        return (await _sites.AddAsync(site)).ToDto();
    }

    public async Task<SiteDto> UpdateAsync(int id, SiteRequest request)
    {
        var site = await _sites.GetAsync(id) ?? throw new NotFoundException("Site", id);
        site.Apply(request);
        await _sites.UpdateAsync(site);
        return site.ToDto();
    }

    public async Task<IReadOnlyList<AssetDto>> GetAssetsAsync(int id)
    {
        if (await _sites.GetReadOnlyAsync(id) is null) throw new NotFoundException("Site", id);
        return (await _sites.GetAssetsAsync(id)).Select(a => a.ToDto()).ToList();
    }
}
