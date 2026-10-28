using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IReorderService
{
    // Called inside the stock-decrement transaction (JobService.AddPartAsync). Creates one Open request when stock <= reorder level.
    Task EnsureReorderForPartAsync(int partId);
    Task<PagedResult<ReorderRequestDto>> ListAsync(ReorderRequestListQuery query);
    Task<ReorderRequestDto> MarkOrderedAsync(int id, ReorderOrderedRequest request);
    Task<ReorderRequestDto> MarkReceivedAsync(int id, ReorderReceivedRequest request);
}

public class ReorderService : IReorderService
{
    private readonly IReorderRequestRepository _reorders;
    private readonly IPartRepository _parts;

    public ReorderService(IReorderRequestRepository reorders, IPartRepository parts)
    {
        _reorders = reorders;
        _parts = parts;
    }

    public async Task EnsureReorderForPartAsync(int partId)
    {
        // The stock decrement already holds the part's row lock inside the caller's transaction, so this read and the
        // open-request check run after any concurrent decrement has committed. No duplicate can be created.
        var level = await _parts.GetStockLevelAsync(partId);
        if (level is null || level.Value.StockQty > level.Value.ReorderLevel) return;
        if (await _reorders.HasOpenOrOrderedAsync(partId)) return;

        var now = DateTime.UtcNow;
        await _reorders.AddAsync(new ReorderRequest { PartId = partId, Status = "Open", CreatedAt = now, UpdatedAt = now });
    }

    public async Task<PagedResult<ReorderRequestDto>> ListAsync(ReorderRequestListQuery query)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var (rows, total) = await _reorders.ListAsync(new ReorderRequestFilter(query.Status, query.PartId), page, pageSize);
        return new PagedResult<ReorderRequestDto>(rows.Select(r => r.ToDto()).ToList(), total, page, pageSize);
    }

    public async Task<ReorderRequestDto> MarkOrderedAsync(int id, ReorderOrderedRequest request)
    {
        var existing = await _reorders.GetAsync(id) ?? throw new NotFoundException("Reorder request", id);
        if (existing.Status != "Open")
            throw new ConflictException($"Reorder request {id} is {existing.Status}, not Open.");

        // Conditional update: only an Open request moves, so two callers cannot both succeed.
        if (await _reorders.TryMarkOrderedAsync(id, request.ExpectedOn, request.UserName, DateTime.UtcNow) == 0)
            throw new ConflictException($"Reorder request {id} is no longer Open.");
        return (await _reorders.GetRowAsync(id))!.ToDto();
    }

    public async Task<ReorderRequestDto> MarkReceivedAsync(int id, ReorderReceivedRequest request)
    {
        var existing = await _reorders.GetAsync(id) ?? throw new NotFoundException("Reorder request", id);
        if (existing.Status != "Ordered")
            throw new ConflictException($"Reorder request {id} is {existing.Status}, not Ordered.");

        // Status change and stock increment share one transaction; a lost race on the conditional update adds nothing.
        await _reorders.InTransactionAsync(async () =>
        {
            if (await _reorders.TryMarkReceivedAsync(id, request.UserName, DateTime.UtcNow) == 0)
                throw new ConflictException($"Reorder request {id} is no longer Ordered.");
            await _parts.IncrementStockAsync(existing.PartId, request.Quantity);
            return 0;
        });
        return (await _reorders.GetRowAsync(id))!.ToDto();
    }
}
