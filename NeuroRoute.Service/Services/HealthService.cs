using System.Reflection;
using NeuroRoute.Service.Gpu;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;

namespace NeuroRoute.Service.Services;

public sealed class HealthService
{
    private readonly OnnxSessionFactory _onnxSessionFactory;
    private readonly GpuClient _gpuClient;
    private readonly FlmProcessManager? _flmProcessManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HealthService> _logger;
    private readonly DateTime _startTime = DateTime.UtcNow;

    public HealthService(
        OnnxSessionFactory onnxSessionFactory,
        GpuClient gpuClient,
        IConfiguration configuration,
        ILogger<HealthService> logger,
        FlmProcessManager? flmProcessManager = null)
    {
        _onnxSessionFactory = onnxSessionFactory;
        _gpuClient = gpuClient;
        _flmProcessManager = flmProcessManager;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        var components = new Dictionary<string, ComponentHealth>();

        var backend = _configuration.GetSection("NeuroRoute")["NpuBackend"] ?? "onnx";

        if (backend.Equals("flm", StringComparison.OrdinalIgnoreCase))
        {
            components["npu"] = CheckFlm();
        }
        else
        {
            components["npu"] = CheckOnnx();
        }

        var gpu = await CheckGpuAsync(ct);
        components["gpu"] = gpu;

        var npuStatus = components["npu"].Status;
        var overall = (npuStatus, gpu.Status) switch
        {
            ("healthy", "healthy") => "healthy",
            ("healthy", _) or (_, "healthy") => "degraded",
            _ => "unhealthy"
        };

        var uptime = DateTime.UtcNow - _startTime;

        return new HealthStatus
        {
            Status = overall,
            Version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? "0.0.0.0",
            Uptime = $"{(int)uptime.TotalDays}.{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}",
            Components = components
        };
    }

    private ComponentHealth CheckOnnx()
    {
        try
        {
            _onnxSessionFactory.GetOrCreateSession();
            return new ComponentHealth
            {
                Status = "healthy",
                Message = "ONNX session created, model loaded"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ONNX health check failed");
            return new ComponentHealth
            {
                Status = "unhealthy",
                Message = ex.Message
            };
        }
    }

    private ComponentHealth CheckFlm()
    {
        if (_flmProcessManager is null)
        {
            return new ComponentHealth
            {
                Status = "unhealthy",
                Message = "FLM process manager not available"
            };
        }

        return new ComponentHealth
        {
            Status = _flmProcessManager.Status == "healthy" ? "healthy" : "unhealthy",
            Message = _flmProcessManager.StatusMessage ?? "FLM backend unavailable"
        };
    }

    private async Task<ComponentHealth> CheckGpuAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await _gpuClient.PingAsync(cts.Token);
            return new ComponentHealth
            {
                Status = result ? "healthy" : "unhealthy",
                Message = result ? "GPU endpoint reachable" : "GPU endpoint unreachable"
            };
        }
        catch (OperationCanceledException)
        {
            return new ComponentHealth
            {
                Status = "unhealthy",
                Message = "GPU health check timed out"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU health check failed");
            return new ComponentHealth
            {
                Status = "unhealthy",
                Message = ex.Message
            };
        }
    }
}
