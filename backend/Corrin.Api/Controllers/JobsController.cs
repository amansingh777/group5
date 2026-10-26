using Corrin.Api.Dtos;
using Corrin.Api.Repositories;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    private readonly IJobService _service;
    public JobsController(IJobService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? regionId, [FromQuery] int? engineerId, [FromQuery] string? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(await _service.ListAsync(new JobFilter(regionId, engineerId, status, from, to), page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(JobCreateRequest request)
    {
        var created = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, JobUpdateRequest request) => Ok(await _service.UpdateAsync(id, request));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, JobCancelRequest request) => Ok(await _service.CancelAsync(id, request));

    [HttpGet("{id:int}/notes")]
    public async Task<IActionResult> Notes(int id) => Ok(await _service.GetNotesAsync(id));

    [HttpPost("{id:int}/notes")]
    public async Task<IActionResult> AddNote(int id, JobNoteRequest request) =>
        StatusCode(StatusCodes.Status201Created, await _service.AddNoteAsync(id, request));

    [HttpGet("{id:int}/parts")]
    public async Task<IActionResult> Parts(int id) => Ok(await _service.GetPartsAsync(id));

    [HttpPost("{id:int}/parts")]
    public async Task<IActionResult> AddPart(int id, JobPartRequest request) =>
        StatusCode(StatusCodes.Status201Created, await _service.AddPartAsync(id, request));
}
