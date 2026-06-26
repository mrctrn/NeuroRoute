using System.Reflection;
using NeuroRoute.Service.Gpu;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using Microsoft.Extensions.Options;

namespace NeuroRoute.Service.Services;

public sealed class HealthService
{
    private readonly OnnxSessionFactory _onnxSessionFactory;
    private readonly GpuClient _gpuClient;
    private readonly FlmProcessManager? _flmProcessManager;
    private readonly IOptions<NeuroRouteOptions> _options;
    private readonly ILogger<HealthService> _logger;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private ComponentHealth _lastGpuHealth = new();
    private DateTime _lastGpuCheck = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public HealthService(
        OnnxSessionFactory onnxSessionFactory,
        GpuClient gpuClient,
        IOptions<NeuroRouteOptions> options,
        ILogger<HealthService> logger,
        FlmProcessManager? flmProcessManager = null)
    {
        _onnxSessionFactory = onnxSessionFactory;
        _gpuClient = gpuClient;
        _flmProcessManager = flmProcessManager;
        _options = options;
        _logger = logger;
    }

    public async Task<HealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        var npuHealth = GetNpuHealth();
        var gpuHealth = await GetGpuHealthAsync(ct);

        var overall = (npuHealth.Status, gpuHealth.Status) switch
        {
            ("healthy", "healthy") => "healthy",
            ("unhealthy", "unhealthy") => "unhealthy",
            _ => "degraded"
        };

        var uptime = DateTime.UtcNow - _startTime;

        return new HealthStatus
        {
            Status = overall,
            Version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0.0",
            Uptime = $"{(int)uptime.TotalDays}.{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}",
            Components = new Dictionary<string, ComponentHealth>
            {
                ["npu"] = npuHealth,
                ["gpu"] = gpuHealth
            }
        };
    }

    private ComponentHealth GetNpuHealth()
    {
        var backend = _options.Value.NpuBackend;

        if (backend.Equals("flm", StringComparison.OrdinalIgnoreCase))
        {
            var modelName = _options.Value.NpuFlmModelTag;
            if (_flmProcessManager is null)
            {
                return new ComponentHealth
                {
                    Status = "unhealthy",
                    Message = "FLM process manager not available",
                    Backend = "flm",
                    Model = modelName,
                    ModelLoaded = false
                };
            }
            return new ComponentHealth
            {
                Status = _flmProcessManager.Status == "healthy" ? "healthy" : "unhealthy",
                Message = _flmProcessManager.StatusMessage ?? "FLM backend unavailable",
                Backend = "flm",
                Model = modelName,
                ModelLoaded = _flmProcessManager.Status == "healthy"
            };
        }
        else
        {
            var modelName = _options.Value.NpuModelPath;
            try
            {
                _onnxSessionFactory.GetOrCreateSession();
                return new ComponentHealth
                {
                    Status = "healthy",
                    Message = "ONNX session created, model loaded",
                    Backend = "onnx",
                    Model = modelName,
                    ModelLoaded = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ONNX health check failed");
                return new ComponentHealth
                {
                    Status = "unhealthy",
                    Message = ex.Message,
                    Backend = "onnx",
                    Model = modelName,
                    ModelLoaded = false
                };
            }
        }
    }

    private async Task<ComponentHealth> GetGpuHealthAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastGpuCheck < CacheDuration)
            return _lastGpuHealth;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var reachable = await _gpuClient.PingAsync(cts.Token);
            var models = reachable ? await _gpuClient.GetAvailableModelsAsync(cts.Token) : [];

            _lastGpuHealth = new ComponentHealth
            {
                Status = reachable
                    ? (models.Count > 0 ? "healthy" : "degraded")
                    : "unhealthy",
                Message = reachable
                    ? (models.Count > 0 ? $"GPU responding, model: {models[0]}" : "GPU reachable, no model loaded")
                    : "GPU endpoint unreachable",
                Endpoint = _options.Value.GpuEndpoint,
                Model = models.Count > 0 ? models[0] : "",
                ModelLoaded = models.Count > 0
            };
        }
        catch (OperationCanceledException)
        {
            _lastGpuHealth = new ComponentHealth
            {
                Status = "unhealthy",
                Message = "GPU health check timed out",
                ModelLoaded = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU health check failed");
            _lastGpuHealth = new ComponentHealth
            {
                Status = "unhealthy",
                Message = ex.Message,
                ModelLoaded = false
            };
        }

        _lastGpuCheck = DateTime.UtcNow;
        return _lastGpuHealth;
    }
}
