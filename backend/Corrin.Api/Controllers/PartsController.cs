using Corrin.Api.Dtos;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/parts")]
public class PartsController : ControllerBase
{
    private readonly IPartService _service;
    public PartsController(IPartService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search) => Ok(await _service.ListAsync(search));

    [HttpGet("low-stock")]
    public async Task<IActionResult> LowStock() => Ok(await _service.GetLowStockAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(PartRequest request)
    {
        var created = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PartRequest request) => Ok(await _service.UpdateAsync(id, request));
}
