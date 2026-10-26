using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync();
    Task<UserDto> GetAsync(int id);
}

public class UserService : IUserService
{
    private readonly IUserRepository _repo;
    public UserService(IUserRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<UserDto>> ListAsync() => (await _repo.ListAsync()).Select(u => u.ToDto()).ToList();

    public async Task<UserDto> GetAsync(int id) =>
        (await _repo.GetAsync(id) ?? throw new NotFoundException("User", id)).ToDto();
}
