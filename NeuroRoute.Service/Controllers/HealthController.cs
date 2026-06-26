using Microsoft.AspNetCore.Mvc;
using NeuroRoute.Service.Services;

namespace NeuroRoute.Service.Controllers;

[ApiController]
[Route("v1/health")]
public sealed class HealthController : ControllerBase
{
    private readonly HealthService _healthService;

    public HealthController(HealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var health = await _healthService.GetHealthAsync(ct);
        var statusCode = health.Status switch
        {
            "healthy" => 200,
            "degraded" => 200,
            _ => 503
        };
        return StatusCode(statusCode, health);
    }
}
