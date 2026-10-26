using Corrin.Api.Dtos;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/shifts")]
public class ShiftsController : ControllerBase
{
    private readonly IShiftService _service;
    public ShiftsController(IShiftService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? engineerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to) =>
        Ok(await _service.QueryAsync(engineerId, from, to));

    [HttpPost]
    public async Task<IActionResult> Create(ShiftRequest request) =>
        StatusCode(StatusCodes.Status201Created, await _service.CreateAsync(request));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ShiftRequest request) => Ok(await _service.UpdateAsync(id, request));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}
