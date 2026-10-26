using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface IUserRepository
{
    Task<List<User>> ListAsync();
    Task<User?> GetAsync(int id);
}

public class UserRepository : IUserRepository
{
    private readonly CorrinDbContext _db;
    public UserRepository(CorrinDbContext db) => _db = db;

    public Task<List<User>> ListAsync() => _db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();
    public Task<User?> GetAsync(int id) => _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
}
