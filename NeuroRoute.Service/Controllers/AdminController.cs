using Microsoft.AspNetCore.Mvc;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using Microsoft.Extensions.Options;
using System.Diagnostics.Eventing.Reader;

namespace NeuroRoute.Service.Controllers;

[ApiController]
[Route("v1/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly FlmProcessManager? _flmProcessManager;
    private readonly OnnxSessionFactory _onnxSessionFactory;
    private readonly IOptions<NeuroRouteOptions> _options;
    private readonly IConfigurationRoot _configuration;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IHostApplicationLifetime lifetime,
        OnnxSessionFactory onnxSessionFactory,
        IOptions<NeuroRouteOptions> options,
        IConfigurationRoot configuration,
        ILogger<AdminController> logger,
        FlmProcessManager? flmProcessManager = null)
    {
        _lifetime = lifetime;
        _flmProcessManager = flmProcessManager;
        _onnxSessionFactory = onnxSessionFactory;
        _options = options;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        _logger.LogInformation("Admin: shutting down service");
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            _lifetime.StopApplication();
        });
        return Ok(new { message = "Shutting down" });
    }

    [HttpPost("restart-backend")]
    public async Task<IActionResult> RestartBackend()
    {
        _logger.LogInformation("Admin: restarting NPU backend");
        try
        {
            var backend = _options.Value.NpuBackend;
            if (backend.Equals("flm", StringComparison.OrdinalIgnoreCase))
            {
                if (_flmProcessManager is null)
                    return StatusCode(500, new { message = "FLM not configured" });
                await _flmProcessManager.StopAsync();
                await _flmProcessManager.StartAsync(CancellationToken.None);
            }
            else
            {
                _onnxSessionFactory.ResetSession();
            }
            return Ok(new { message = "Backend restarted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backend restart failed");
            return StatusCode(500, new { message = $"Backend restart failed: {ex.Message}" });
        }
    }

    [HttpPost("reload-config")]
    public IActionResult ReloadConfig()
    {
        _logger.LogInformation("Admin: reloading configuration");
        _configuration.Reload();
        return Ok(new { message = "Configuration reloaded" });
    }

    [HttpGet("logs")]
    public IActionResult GetLogs([FromQuery] int count = 50)
    {
        var entries = new List<AdminLogEntry>();
        try
        {
            using var log = new EventLogReader("NeuroRoute", PathType.LogName);
            for (int i = 0; i < count; i++)
            {
                using var record = log.ReadEvent();
                if (record == null) break;
                entries.Add(new AdminLogEntry
                {
                    Timestamp = record.TimeCreated ?? DateTime.UtcNow,
                    Level = record.LevelDisplayName ?? "Information",
                    Message = record.FormatDescription() ?? ""
                });
            }
        }
        catch
        {
            // Event log not available when running in dev console mode
        }
        return Ok(entries);
    }
}
