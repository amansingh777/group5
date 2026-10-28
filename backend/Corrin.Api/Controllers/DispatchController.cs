using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/dispatch")]
public class DispatchController : ControllerBase
{
    private readonly IDispatchBoardService _board;
    public DispatchController(IDispatchBoardService board) => _board = board;

    [HttpGet("board")]
    public async Task<IActionResult> Board([FromQuery] DateOnly? from, [FromQuery] int? days, [FromQuery] int? regionId) =>
        Ok(await _board.GetBoardAsync(from, days, regionId));
}
