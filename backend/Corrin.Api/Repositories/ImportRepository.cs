using Corrin.Api.Data;
using Corrin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Repositories;

public interface IImportRepository
{
    Task<List<ImportBatch>> GetBatchesAsync();
    Task<ImportBatch?> GetBatchAsync(int id);
    Task<List<JobRequest>> GetRequestsAsync(int batchId);
}

public class ImportRepository : IImportRepository
{
    private readonly CorrinDbContext _db;
    public ImportRepository(CorrinDbContext db) => _db = db;

    public Task<List<ImportBatch>> GetBatchesAsync() => _db.ImportBatches.AsNoTracking().OrderByDescending(b => b.ImportedAt).Take(100).ToListAsync();
    public Task<ImportBatch?> GetBatchAsync(int id) => _db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
    public Task<List<JobRequest>> GetRequestsAsync(int batchId) =>
        _db.JobRequests.AsNoTracking().Where(r => r.BatchId == batchId).OrderBy(r => r.RowNo).ToListAsync();
}
