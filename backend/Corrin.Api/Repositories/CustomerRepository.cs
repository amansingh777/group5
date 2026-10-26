using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface ICustomerRepository
{
    Task<(List<Customer> Items, int Total)> ListAsync(int? regionId, string? search, int page, int pageSize);
    Task<Customer?> GetAsync(int id);
    Task<Customer?> GetByRefAsync(string accountRef);
    Task<Customer> AddAsync(Customer customer);
    Task UpdateAsync(Customer customer);
    Task<List<Site>> GetSitesAsync(int customerId);
    Task<List<CustomerNote>> GetNotesAsync(int customerId);
    Task<CustomerNote> AddNoteAsync(CustomerNote note);
    Task<int> NextAccountNumberAsync();
}

public class CustomerRepository : ICustomerRepository
{
    private readonly CorrinDbContext _db;
    public CustomerRepository(CorrinDbContext db) => _db = db;

    public async Task<(List<Customer> Items, int Total)> ListAsync(int? regionId, string? search, int page, int pageSize)
    {
        var q = _db.Customers.AsNoTracking().AsQueryable();
        if (regionId.HasValue) q = q.Where(c => c.RegionId == regionId);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(c => c.Name.Contains(search) || c.AccountRef.Contains(search) || c.Postcode.Contains(search));
        var total = await q.CountAsync();
        var items = await q.OrderBy(c => c.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<Customer?> GetAsync(int id) => _db.Customers.FirstOrDefaultAsync(c => c.Id == id);
    public Task<Customer?> GetByRefAsync(string accountRef) => _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.AccountRef == accountRef);

    public async Task<Customer> AddAsync(Customer customer)
    {
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }

    public Task UpdateAsync(Customer customer) => _db.SaveChangesAsync();

    public Task<List<Site>> GetSitesAsync(int customerId) =>
        _db.Sites.AsNoTracking().Where(s => s.CustomerId == customerId).OrderBy(s => s.Name).ToListAsync();

    public Task<List<CustomerNote>> GetNotesAsync(int customerId) =>
        _db.CustomerNotes.AsNoTracking().Where(n => n.CustomerId == customerId).OrderByDescending(n => n.CreatedAt).ToListAsync();

    public async Task<CustomerNote> AddNoteAsync(CustomerNote note)
    {
        _db.CustomerNotes.Add(note);
        await _db.SaveChangesAsync();
        return note;
    }

    public async Task<int> NextAccountNumberAsync() => (await _db.Customers.MaxAsync(c => (int?)c.Id) ?? 0) + 1;
}
