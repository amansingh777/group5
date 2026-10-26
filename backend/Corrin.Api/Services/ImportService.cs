using System.Globalization;
using System.Text;
using Corrin.Api.Data;
using Corrin.Api.Domain;
using Corrin.Api.Dtos;
using Corrin.Api.Infrastructure;
using Corrin.Api.Mappers;
using Corrin.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Corrin.Api.Services;

public interface IJobRequestImportService
{
    Task<ImportResultDto> ImportAsync(Stream csv, string fileName);
    Task<IReadOnlyList<ImportBatchDto>> GetBatchesAsync();
    Task<IReadOnlyList<JobRequestDto>> GetRequestsAsync(int batchId);
}

public class JobRequestImportService : IJobRequestImportService
{
    private readonly CorrinDbContext _db;
    private readonly IImportRepository _imports;

    public JobRequestImportService(CorrinDbContext db, IImportRepository imports)
    {
        _db = db;
        _imports = imports;
    }

    public async Task<IReadOnlyList<ImportBatchDto>> GetBatchesAsync() => (await _imports.GetBatchesAsync()).Select(b => b.ToDto()).ToList();

    public async Task<IReadOnlyList<JobRequestDto>> GetRequestsAsync(int batchId)
    {
        if (await _imports.GetBatchAsync(batchId) is null) throw new NotFoundException("Import batch", batchId);
        return (await _imports.GetRequestsAsync(batchId)).Select(r => r.ToDto()).ToList();
    }

    public async Task<ImportResultDto> ImportAsync(Stream csv, string fileName)
    {
        string text;
        using (var reader = new StreamReader(csv, Encoding.UTF8, true))
        {
            text = await reader.ReadToEndAsync();
        }
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            throw new BusinessRuleException("The file is empty.");

        var header = lines[0].Split(',');
        int iCust = -1, iPost = -1, iType = -1, iPrio = -1, iDate = -1, iDesc = -1;
        for (int h = 0; h < header.Length; h++)
        {
            var name = header[h].Trim().Trim('"').ToLowerInvariant();
            if (name == "customer_ref" || name == "customer" || name == "account") iCust = h;
            else if (name == "site_postcode" || name == "postcode") iPost = h;
            else if (name == "job_type" || name == "type") iType = h;
            else if (name == "priority") iPrio = h;
            else if (name == "requested_date" || name == "date") iDate = h;
            else if (name == "description" || name == "desc") iDesc = h;
        }
        if (iCust < 0 || iPost < 0 || iType < 0)
            throw new BusinessRuleException("The file must have customer_ref, site_postcode and job_type columns.");

        var batch = new ImportBatch { FileName = fileName, ImportedAt = DateTime.UtcNow };
        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync();

        var messages = new List<string>();
        int total = 0, created = 0, rejected = 0;

        for (int n = 1; n < lines.Length; n++)
        {
            var line = lines[n];
            if (string.IsNullOrWhiteSpace(line)) continue;
            total++;

            // split the line, honouring double quotes
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int c = 0; c < line.Length; c++)
            {
                var ch = line[c];
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }
            fields.Add(sb.ToString());

            string? custRef = iCust < fields.Count ? fields[iCust].Trim() : null;
            string? postcode = iPost < fields.Count ? fields[iPost].Trim() : null;
            string? typeCode = iType < fields.Count ? fields[iType].Trim() : null;
            string? prio = iPrio >= 0 && iPrio < fields.Count ? fields[iPrio].Trim() : null;
            string? dateText = iDate >= 0 && iDate < fields.Count ? fields[iDate].Trim() : null;
            string? desc = iDesc >= 0 && iDesc < fields.Count ? fields[iDesc].Trim() : null;

            var request = new JobRequest
            {
                BatchId = batch.Id,
                RowNo = n,
                CustomerRef = custRef,
                SitePostcode = postcode,
                JobTypeCode = typeCode,
                Description = desc != null && desc.Length > 500 ? desc.Substring(0, 500) : desc
            };

            string? problem = null;

            // customer
            Customer? customer = null;
            if (string.IsNullOrEmpty(custRef))
            {
                problem = "customer_ref is missing";
            }
            else
            {
                customer = await _db.Customers.FirstOrDefaultAsync(c => c.AccountRef == custRef.ToUpper());
                if (customer == null) problem = "customer " + custRef + " not found";
            }

            // priority
            string priority = "P3";
            if (problem == null)
            {
                if (!string.IsNullOrEmpty(prio))
                {
                    var p = prio.ToUpper();
                    if (p == "P1" || p == "URGENT" || p == "EMERGENCY" || p == "1") priority = "P1";
                    else if (p == "P2" || p == "HIGH" || p == "2") priority = "P2";
                    else if (p == "P3" || p == "NORMAL" || p == "ROUTINE" || p == "3") priority = "P3";
                    else problem = "priority '" + prio + "' is not recognised";
                }
                request.Priority = priority;
            }

            // date
            DateOnly? requested = null;
            if (problem == null && !string.IsNullOrEmpty(dateText))
            {
                if (DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
                {
                    requested = DateOnly.FromDateTime(d1);
                }
                else if (DateTime.TryParseExact(dateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
                {
                    requested = DateOnly.FromDateTime(d2);
                }
                else
                {
                    problem = "requested_date '" + dateText + "' is not a date";
                }
                request.RequestedDate = requested;
            }

            // job type
            JobType? jobType = null;
            if (problem == null)
            {
                if (string.IsNullOrEmpty(typeCode))
                {
                    problem = "job_type is missing";
                }
                else
                {
                    jobType = await _db.JobTypes.FirstOrDefaultAsync(t => t.Code == typeCode.ToUpper());
                    if (jobType == null) problem = "job type " + typeCode + " not found";
                }
            }

            // site
            Site? site = null;
            if (problem == null)
            {
                if (string.IsNullOrEmpty(postcode))
                {
                    problem = "site_postcode is missing";
                }
                else
                {
                    var norm = postcode.Replace(" ", "").ToUpper();
                    var sites = await _db.Sites.Where(s => s.CustomerId == customer!.Id).ToListAsync();
                    foreach (var s in sites)
                    {
                        if (s.Postcode.Replace(" ", "").ToUpper() == norm)
                        {
                            site = s;
                            break;
                        }
                    }
                    if (site == null) problem = "customer " + custRef + " has no site at " + postcode;
                }
            }

            // duplicate open job
            if (problem == null)
            {
                var dup = await _db.Jobs.AnyAsync(j => j.SiteId == site!.Id && j.JobTypeId == jobType!.Id
                    && (j.Status == "Requested" || j.Status == "Scheduled"));
                if (dup) problem = "an open " + jobType!.Code + " job already exists for this site";
            }

            // pick the asset the job is for
            int? assetId = null;
            if (problem == null)
            {
                string? wanted = null;
                if (jobType!.Code.StartsWith("BOILER") || jobType.Code == "SAFETY_CHECK") wanted = "Boiler";
                else if (jobType.Code.StartsWith("METER") || jobType.Code.StartsWith("SMART")) wanted = "Meter";
                else if (jobType.Code == "HP_SURVEY") wanted = "HeatPump";
                else if (jobType.Code == "CYLINDER_FIT") wanted = "Cylinder";
                if (wanted != null)
                {
                    var assets = await _db.Assets.Where(a => a.SiteId == site!.Id && a.AssetType == wanted).ToListAsync();
                    foreach (var a in assets)
                    {
                        if (a.Status != "Decommissioned")
                        {
                            assetId = a.Id;
                            break;
                        }
                    }
                    if (assetId == null && jobType.Code != "BOILER_INSTALL" && jobType.Code != "METER_INSTALL"
                        && jobType.Code != "HP_SURVEY" && jobType.Code != "CYLINDER_FIT")
                    {
                        problem = "site has no " + wanted.ToLower() + " on record";
                    }
                }
            }

            if (problem != null)
            {
                request.Result = "Rejected";
                request.RejectionReason = problem;
                rejected++;
                messages.Add("Row " + n + ": " + problem);
                _db.JobRequests.Add(request);
                await _db.SaveChangesAsync();
                continue;
            }

            var now = DateTime.UtcNow;
            int slaHours = priority == "P1" ? 4 : priority == "P2" ? 24 : 72;
            DateTime slaStart = requested.HasValue ? requested.Value.ToDateTime(new TimeOnly(9, 0)) : now;
            var job = new Job
            {
                JobRef = "J-" + Random.Shared.Next(1000000, 9999999),
                CustomerId = customer!.Id,
                SiteId = site!.Id,
                AssetId = assetId,
                JobTypeId = jobType!.Id,
                RegionId = site.RegionId,
                Priority = priority,
                Status = "Requested",
                SlaDueAt = slaStart.AddHours(slaHours),
                Description = string.IsNullOrEmpty(desc) ? jobType.Name : (desc.Length > 500 ? desc.Substring(0, 500) : desc),
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Jobs.Add(job);
            await _db.SaveChangesAsync();

            request.Result = "Created";
            request.JobId = job.Id;
            _db.JobRequests.Add(request);
            await _db.SaveChangesAsync();
            created++;
        }

        batch.TotalRows = total;
        batch.CreatedRows = created;
        batch.RejectedRows = rejected;
        await _db.SaveChangesAsync();

        return new ImportResultDto(batch.Id, total, created, rejected, messages);
    }
}
