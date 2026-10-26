using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> ListAsync(int? regionId, string? search, int page, int pageSize);
    Task<CustomerDto> GetAsync(int id);
    Task<CustomerDto> CreateAsync(CustomerRequest request);
    Task<CustomerDto> UpdateAsync(int id, CustomerRequest request);
    Task<IReadOnlyList<SiteDto>> GetSitesAsync(int id);
    Task<IReadOnlyList<CustomerNoteDto>> GetNotesAsync(int id);
    Task<CustomerNoteDto> AddNoteAsync(int id, NoteRequest request);
}

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly IReferenceRepository _reference;

    public CustomerService(ICustomerRepository customers, IReferenceRepository reference)
    {
        _customers = customers;
        _reference = reference;
    }

    public async Task<PagedResult<CustomerDto>> ListAsync(int? regionId, string? search, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _customers.ListAsync(regionId, search, page, pageSize);
        return new PagedResult<CustomerDto>(items.Select(c => c.ToDto()).ToList(), total, page, pageSize);
    }

    public async Task<CustomerDto> GetAsync(int id) =>
        (await _customers.GetAsync(id) ?? throw new NotFoundException("Customer", id)).ToDto();

    public async Task<CustomerDto> CreateAsync(CustomerRequest request)
    {
        if (!await _reference.RegionExistsAsync(request.RegionId)) throw new BusinessRuleException("Region does not exist.");
        var customer = new Customer { CreatedAt = DateTime.UtcNow };
        customer.Apply(request);
        var next = await _customers.NextAccountNumberAsync();
        customer.AccountRef = $"CUS-{10000 + next * 7:D5}";
        return (await _customers.AddAsync(customer)).ToDto();
    }

    public async Task<CustomerDto> UpdateAsync(int id, CustomerRequest request)
    {
        var customer = await _customers.GetAsync(id) ?? throw new NotFoundException("Customer", id);
        if (!await _reference.RegionExistsAsync(request.RegionId)) throw new BusinessRuleException("Region does not exist.");
        customer.Apply(request);
        await _customers.UpdateAsync(customer);
        return customer.ToDto();
    }

    public async Task<IReadOnlyList<SiteDto>> GetSitesAsync(int id)
    {
        if (await _customers.GetAsync(id) is null) throw new NotFoundException("Customer", id);
        return (await _customers.GetSitesAsync(id)).Select(s => s.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<CustomerNoteDto>> GetNotesAsync(int id)
    {
        if (await _customers.GetAsync(id) is null) throw new NotFoundException("Customer", id);
        return (await _customers.GetNotesAsync(id)).Select(n => n.ToDto()).ToList();
    }

    public async Task<CustomerNoteDto> AddNoteAsync(int id, NoteRequest request)
    {
        if (await _customers.GetAsync(id) is null) throw new NotFoundException("Customer", id);
        var note = new CustomerNote { CustomerId = id, AuthorUserId = request.AuthorUserId, Note = request.Note, CreatedAt = DateTime.UtcNow };
        return (await _customers.AddNoteAsync(note)).ToDto();
    }
}
