using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IPartService
{
    Task<IReadOnlyList<PartDto>> ListAsync(string? search);
    Task<IReadOnlyList<PartDto>> GetLowStockAsync();
    Task<PartDto> GetAsync(int id);
    Task<PartDto> CreateAsync(PartRequest request);
    Task<PartDto> UpdateAsync(int id, PartRequest request);
}

public class PartService : IPartService
{
    private readonly IPartRepository _parts;
    public PartService(IPartRepository parts) => _parts = parts;

    public async Task<IReadOnlyList<PartDto>> ListAsync(string? search) => (await _parts.ListAsync(search)).Select(p => p.ToDto()).ToList();
    public async Task<IReadOnlyList<PartDto>> GetLowStockAsync() => (await _parts.GetLowStockAsync()).Select(p => p.ToDto()).ToList();

    public async Task<PartDto> GetAsync(int id) =>
        (await _parts.GetAsync(id) ?? throw new NotFoundException("Part", id)).ToDto();

    public async Task<PartDto> CreateAsync(PartRequest request)
    {
        var part = new Part();
        part.Apply(request);
        return (await _parts.AddAsync(part)).ToDto();
    }

    public async Task<PartDto> UpdateAsync(int id, PartRequest request)
    {
        var part = await _parts.GetAsync(id) ?? throw new NotFoundException("Part", id);
        part.Apply(request);
        await _parts.UpdateAsync(part);
        return part.ToDto();
    }
}
