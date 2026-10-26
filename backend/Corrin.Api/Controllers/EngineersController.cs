using Corrin.Api.Dtos;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/engineers")]
public class EngineersController : ControllerBase
{
    private readonly IEngineerService _service;
    public EngineersController(IEngineerService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? regionId, [FromQuery] int? skillId, [FromQuery] bool? active) =>
        Ok(await _service.ListAsync(regionId, skillId, active));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(EngineerRequest request)
    {
        var created = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, EngineerRequest request) => Ok(await _service.UpdateAsync(id, request));

    [HttpPut("{id:int}/skills")]
    public async Task<IActionResult> SetSkills(int id, SkillAssignmentRequest request) => Ok(await _service.SetSkillsAsync(id, request));

    [HttpGet("{id:int}/shifts")]
    public async Task<IActionResult> Shifts(int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to) =>
        Ok(await _service.GetShiftsAsync(id, from, to));

    [HttpGet("{id:int}/jobs")]
    public async Task<IActionResult> Jobs(int id, [FromQuery] DateTime from, [FromQuery] DateTime to) =>
        Ok(await _service.GetJobsAsync(id, from, to));

    [HttpGet("{id:int}/on-shift")]
    public async Task<IActionResult> OnShift(int id) => Ok(await _service.GetOnShiftAsync(id));
}
