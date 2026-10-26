using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _service;
    public UsersController(IUserService service) => _service = service;

    [HttpGet] public async Task<IActionResult> List() => Ok(await _service.ListAsync());
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id) => Ok(await _service.GetAsync(id));
}
