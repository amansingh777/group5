using Corrin.Api.Dtos;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IReferenceService
{
    Task<IReadOnlyList<RegionDto>> GetRegionsAsync();
    Task<IReadOnlyList<SkillDto>> GetSkillsAsync();
    Task<IReadOnlyList<JobTypeDto>> GetJobTypesAsync();
}

public class ReferenceService : IReferenceService
{
    private readonly IReferenceRepository _repo;
    public ReferenceService(IReferenceRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<RegionDto>> GetRegionsAsync() => (await _repo.GetRegionsAsync()).Select(r => r.ToDto()).ToList();
    public async Task<IReadOnlyList<SkillDto>> GetSkillsAsync() => (await _repo.GetSkillsAsync()).Select(s => s.ToDto()).ToList();
    public async Task<IReadOnlyList<JobTypeDto>> GetJobTypesAsync() => (await _repo.GetJobTypesAsync()).Select(t => t.ToDto()).ToList();
}
