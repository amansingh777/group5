using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;

namespace Corrin.Api.Services;

public interface IWorkOrderService
{
    Task<PagedResult<WorkOrderDto>> ListAsync(WorkOrderListQuery query);
    Task<WorkOrderDto> GetAsync(int id);
    Task<WorkOrderDto> ChangeStatusAsync(int id, WorkOrderStatusRequest request);
}

public class WorkOrderService : IWorkOrderService
{
    private readonly IWorkOrderReadRepository _reads;
    private readonly IJobRepository _jobs;

    public WorkOrderService(IWorkOrderReadRepository reads, IJobRepository jobs)
    {
        _reads = reads;
        _jobs = jobs;
    }

    public async Task<PagedResult<WorkOrderDto>> ListAsync(WorkOrderListQuery query)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var filter = new WorkOrderFilter(query.RegionId, query.EngineerId, query.Status, query.Priority);
        var (rows, total) = await _reads.ListAsync(filter, page, pageSize);

        var ids = rows.Select(r => r.Id).ToList();
        var notes = (await _reads.NotesForJobsAsync(ids)).ToLookup(n => n.JobId);
        var parts = (await _reads.PartsForJobsAsync(ids)).ToLookup(p => p.JobId);
        var now = DateTime.UtcNow;

        var items = rows.Select(r => r.ToWorkOrderDto(
            SlaState.Compute(r.Status, r.CreatedAt, r.SlaDueAt, now),
            notes[r.Id].Select(n => n.ToDto()).ToList(),
            parts[r.Id].Select(p => p.ToDto()).ToList())).ToList();
        return new PagedResult<WorkOrderDto>(items, total, page, pageSize);
    }

    public async Task<WorkOrderDto> GetAsync(int id)
    {
        var row = await _reads.GetAsync(id) ?? throw new NotFoundException("Work order", id);
        var notes = await _reads.NotesForJobsAsync(new[] { id });
        var parts = await _reads.PartsForJobsAsync(new[] { id });
        return row.ToWorkOrderDto(
            SlaState.Compute(row.Status, row.CreatedAt, row.SlaDueAt, DateTime.UtcNow),
            notes.Select(n => n.ToDto()).ToList(),
            parts.Select(p => p.ToDto()).ToList());
    }

    public async Task<WorkOrderDto> ChangeStatusAsync(int id, WorkOrderStatusRequest request)
    {
        // Lock the job row first, then read it, so two status changes cannot interleave.
        await _jobs.InTransactionAsync(async () =>
        {
            await _jobs.LockForUpdateAsync(id);
            var job = await _jobs.GetAsync(id) ?? throw new NotFoundException("Work order", id);
            var from = job.Status;
            if (!WorkOrderStatus.CanMoveByStatusEndpoint(from, request.Status))
                throw new ConflictException($"A work order cannot move from {from} to {request.Status} with this action.");

            var now = DateTime.UtcNow;
            job.Status = request.Status;
            job.UpdatedAt = now;
            await _jobs.UpdateAsync(job);
            await _jobs.AddNoteAsync(new JobNote
            {
                JobId = id,
                Author = request.UserName,
                Note = $"Status changed from {from} to {request.Status} by {request.UserName}",
                CreatedAt = now
            });
            return true;
        });
        return await GetAsync(id);
    }
}
