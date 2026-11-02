using Corrin.Api.Dtos;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/reorder-requests")]
public class ReorderRequestsController : ControllerBase
{
    private readonly IReorderService _service;
    public ReorderRequestsController(IReorderService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ReorderRequestListQuery query) => Ok(await _service.ListAsync(query));

    [HttpPost("{id:int}/ordered")]
    public async Task<IActionResult> MarkOrdered(int id, ReorderOrderedRequest request) => Ok(await _service.MarkOrderedAsync(id, request));

    [HttpPost("{id:int}/received")]
    public async Task<IActionResult> MarkReceived(int id, ReorderReceivedRequest request) => Ok(await _service.MarkReceivedAsync(id, request));
}
