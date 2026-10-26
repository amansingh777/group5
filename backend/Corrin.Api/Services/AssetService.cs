using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IAssetService
{
    Task<IReadOnlyList<AssetDto>> ListAsync(int? siteId, string? assetType);
    Task<AssetDto> GetAsync(int id);
    Task<AssetDto> CreateAsync(AssetRequest request);
    Task<AssetDto> UpdateAsync(int id, AssetRequest request);
    Task<IReadOnlyList<JobHistoryDto>> GetJobHistoryAsync(int id);
}

public record JobHistoryDto(int JobId, string JobRef, string Status, string? Outcome, DateTime? ScheduledStart, int? EngineerId);

public class AssetService : IAssetService
{
    private readonly IAssetRepository _assets;
    private readonly ISiteRepository _sites;

    public AssetService(IAssetRepository assets, ISiteRepository sites)
    {
        _assets = assets;
        _sites = sites;
    }

    public async Task<IReadOnlyList<AssetDto>> ListAsync(int? siteId, string? assetType) =>
        (await _assets.ListAsync(siteId, assetType)).Select(a => a.ToDto()).ToList();

    public async Task<AssetDto> GetAsync(int id) =>
        (await _assets.GetAsync(id) ?? throw new NotFoundException("Asset", id)).ToDto();

    public async Task<AssetDto> CreateAsync(AssetRequest request)
    {
        if (await _sites.GetReadOnlyAsync(request.SiteId) is null) throw new BusinessRuleException("Site does not exist.");
        var asset = new Asset();
        asset.Apply(request);
        return (await _assets.AddAsync(asset)).ToDto();
    }

    public async Task<AssetDto> UpdateAsync(int id, AssetRequest request)
    {
        var asset = await _assets.GetAsync(id) ?? throw new NotFoundException("Asset", id);
        asset.Apply(request);
        await _assets.UpdateAsync(asset);
        return asset.ToDto();
    }

    public async Task<IReadOnlyList<JobHistoryDto>> GetJobHistoryAsync(int id)
    {
        if (await _assets.GetAsync(id) is null) throw new NotFoundException("Asset", id);
        return (await _assets.GetJobHistoryAsync(id))
            .Select(j => new JobHistoryDto(j.Id, j.JobRef, j.Status, j.Outcome, j.ScheduledStart, j.EngineerId)).ToList();
    }
}
