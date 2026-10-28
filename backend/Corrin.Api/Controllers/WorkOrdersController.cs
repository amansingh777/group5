using Corrin.Api.Dtos;
using Corrin.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Corrin.Api.Controllers;

[ApiController]
[Route("api/work-orders")]
public class WorkOrdersController : ControllerBase
{
    private readonly IWorkOrderAllocationService _allocation;
    private readonly IWorkOrderSchedulingService _scheduling;
    private readonly IWorkOrderVisitService _visits;
    private readonly IWorkOrderService _workOrders;
    public WorkOrdersController(IWorkOrderAllocationService allocation, IWorkOrderSchedulingService scheduling, IWorkOrderVisitService visits, IWorkOrderService workOrders)
    {
        _allocation = allocation;
        _scheduling = scheduling;
        _visits = visits;
        _workOrders = workOrders;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] WorkOrderListQuery query) => Ok(await _workOrders.ListAsync(query));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => Ok(await _workOrders.GetAsync(id));

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, WorkOrderStatusRequest request) => Ok(await _workOrders.ChangeStatusAsync(id, request));

    [HttpGet("{id:int}/candidates")]
    public async Task<IActionResult> Candidates(int id, [FromQuery] DateOnly? date) =>
        Ok(await _allocation.GetCandidatesAsync(id, date));

    [HttpPost("{id:int}/allocate")]
    public async Task<IActionResult> Allocate(int id, WorkOrderAllocateRequest request) =>
        Ok(await _allocation.AllocateAsync(id, request));

    [HttpPost("{id:int}/schedule")]
    public async Task<IActionResult> Schedule(int id, WorkOrderScheduleRequest request) =>
        Ok(await _scheduling.ScheduleAsync(id, request));

    [HttpGet("{id:int}/next-slot")]
    public async Task<IActionResult> NextSlot(int id, [FromQuery] DateOnly? from) =>
        Ok(await _scheduling.NextSlotAsync(id, from));

    [HttpPost("{id:int}/visits/start")]
    public async Task<IActionResult> StartVisit(int id) => Ok(await _visits.StartAsync(id));

    [HttpPost("{id:int}/visits/complete")]
    public async Task<IActionResult> CompleteVisit(int id, VisitCompleteRequest request) =>
        Ok(await _visits.CompleteAsync(id, request));

    [HttpGet("{id:int}/visits")]
    public async Task<IActionResult> Visits(int id) => Ok(await _visits.ListAsync(id));
}
