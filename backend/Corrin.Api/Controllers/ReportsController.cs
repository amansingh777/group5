using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _service;
    public ReportsController(IReportService service) => _service = service;

    [HttpGet("engineer-utilisation")]
    public async Task<IActionResult> EngineerUtilisation([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int? regionId) =>
        Ok(await _service.GetEngineerUtilisationAsync(from, to, regionId));

    [HttpGet("jobs-by-status")]
    public async Task<IActionResult> JobsByStatus([FromQuery] int? regionId) => Ok(await _service.GetJobsByStatusAsync(regionId));
}
