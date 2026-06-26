using NeuroRoute.Service.Npu;

namespace NeuroRoute.Service;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly FlmProcessManager? _flmProcessManager;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        FlmProcessManager? flmProcessManager = null)
    {
        _logger = logger;
        _configuration = configuration;
        _flmProcessManager = flmProcessManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NeuroRoute Routing Gateway started");

        var backend = _configuration.GetSection("NeuroRoute")["NpuBackend"] ?? "onnx";

        if (backend.Equals("flm", StringComparison.OrdinalIgnoreCase))
        {
            if (_flmProcessManager is not null)
            {
                _logger.LogInformation("Starting FLM backend...");
                await _flmProcessManager.StartAsync(stoppingToken);

                if (_flmProcessManager.Status != "healthy")
                {
                    _logger.LogWarning(
                        "FLM backend failed to start: {Message}. Service will run in degraded mode.",
                        _flmProcessManager.StatusMessage);
                }
            }
            else
            {
                _logger.LogWarning(
                    "NpuBackend is 'flm' but FlmProcessManager is not registered. "
                    + "Check FastFlowLM installation.");
            }
        }
        else
        {
            _logger.LogInformation("Using ONNX backend (NpuModelPath: {Path})",
                _configuration.GetSection("NeuroRoute")["NpuModelPath"] ?? "default");
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_flmProcessManager is not null)
        {
            _logger.LogInformation("Shutting down FLM backend...");
            await _flmProcessManager.StopAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
