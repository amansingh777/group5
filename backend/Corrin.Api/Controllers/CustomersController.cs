using Corrin.Api.Dtos;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _service;
    public CustomersController(ICustomerService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? regionId, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(await _service.ListAsync(regionId, search, page, pageSize));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(CustomerRequest request)
    {
        var created = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CustomerRequest request) => Ok(await _service.UpdateAsync(id, request));

    [HttpGet("{id:int}/sites")]
    public async Task<IActionResult> Sites(int id) => Ok(await _service.GetSitesAsync(id));

    [HttpGet("{id:int}/notes")]
    public async Task<IActionResult> Notes(int id) => Ok(await _service.GetNotesAsync(id));

    [HttpPost("{id:int}/notes")]
    public async Task<IActionResult> AddNote(int id, NoteRequest request) =>
        StatusCode(StatusCodes.Status201Created, await _service.AddNoteAsync(id, request));
}
