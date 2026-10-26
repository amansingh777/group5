using Corrin.Api.Dtos;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/sites")]
public class SitesController : ControllerBase
{
    private readonly ISiteService _service;
    public SitesController(ISiteService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? regionId, [FromQuery] int? customerId) => Ok(await _service.ListAsync(regionId, customerId));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(SiteRequest request)
    {
        var created = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SiteRequest request) => Ok(await _service.UpdateAsync(id, request));

    [HttpGet("{id:int}/assets")]
    public async Task<IActionResult> Assets(int id) => Ok(await _service.GetAssetsAsync(id));
}
