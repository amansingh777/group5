using Corrin.Api.Dtos;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/assets")]
public class AssetsController : ControllerBase
{
    private readonly IAssetService _service;
    public AssetsController(IAssetService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? siteId, [FromQuery] string? assetType) => Ok(await _service.ListAsync(siteId, assetType));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    public async Task<IActionResult> Create(AssetRequest request)
    {
        var created = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, AssetRequest request) => Ok(await _service.UpdateAsync(id, request));

    [HttpGet("{id:int}/jobs")]
    public async Task<IActionResult> Jobs(int id) => Ok(await _service.GetJobHistoryAsync(id));
}
