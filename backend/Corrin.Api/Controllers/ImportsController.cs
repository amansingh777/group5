using Corrin.Api.Infrastructure;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/imports")]
public class ImportsController : ControllerBase
{
    private readonly IJobRequestImportService _service;
    public ImportsController(IJobRequestImportService service) => _service = service;

    [HttpPost("job-requests")]
    public async Task<IActionResult> ImportJobRequests(IFormFile file)
    {
        if (file is null || file.Length == 0) throw new BusinessRuleException("Attach a CSV file in the 'file' field.");
        await using var stream = file.OpenReadStream();
        return Ok(await _service.ImportAsync(stream, file.FileName));
    }

    [HttpGet]
    public async Task<IActionResult> Batches() => Ok(await _service.GetBatchesAsync());

    [HttpGet("{id:int}/requests")]
    public async Task<IActionResult> Requests(int id) => Ok(await _service.GetRequestsAsync(id));
}
