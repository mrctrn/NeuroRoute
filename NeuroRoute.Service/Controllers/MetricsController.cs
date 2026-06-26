using Microsoft.AspNetCore.Mvc;
using NeuroRoute.Service.Services;

namespace NeuroRoute.Service.Controllers;

[ApiController]
[Route("v1/metrics")]
public sealed class MetricsController : ControllerBase
{
    private readonly MetricsService _metricsService;

    public MetricsController(MetricsService metricsService)
    {
        _metricsService = metricsService;
    }

    [HttpGet]
    public IActionResult GetMetrics()
    {
        var snapshot = _metricsService.GetSnapshot();
        return Ok(snapshot);
    }
}
