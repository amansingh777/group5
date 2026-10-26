using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api")]
public class ReferenceController : ControllerBase
{
    private readonly IReferenceService _service;
    public ReferenceController(IReferenceService service) => _service = service;

    [HttpGet("regions")] public async Task<IActionResult> Regions() => Ok(await _service.GetRegionsAsync());
    [HttpGet("skills")] public async Task<IActionResult> Skills() => Ok(await _service.GetSkillsAsync());
    [HttpGet("job-types")] public async Task<IActionResult> JobTypes() => Ok(await _service.GetJobTypesAsync());
}
