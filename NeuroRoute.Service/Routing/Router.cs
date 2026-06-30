using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NeuroRoute.Service.Diagnostics;
using NeuroRoute.Service.Gpu;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using NeuroRoute.Service.Services;

namespace NeuroRoute.Service.Routing;

public sealed class Router
{
    private readonly NpuPlanner _planner;
    private readonly PromptBuilder _promptBuilder;
    private readonly NpuModel _npuModel;
    private readonly IGpuClient _gpuClient;
    private readonly MetricsService _metrics;
    private readonly RuntimeSettings _runtimeSettings;
    private readonly ILogger<Router> _logger;

    public Router(
        NpuPlanner planner,
        PromptBuilder promptBuilder,
        NpuModel npuModel,
        IGpuClient gpuClient,
        MetricsService metrics,
        RuntimeSettings runtimeSettings,
        ILogger<Router> logger)
    {
        _planner = planner;
        _promptBuilder = promptBuilder;
        _npuModel = npuModel;
        _gpuClient = gpuClient;
        _metrics = metrics;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    public async Task<ChatResponse> RouteAsync(ChatRequest request, CancellationToken ct = default)
    {
        using var activity = RoutingTelemetry.ActivitySource.StartActivity("Router.RouteAsync");
        var sw = Stopwatch.StartNew();
        var backend = "npu";
        var fallback = false;
        var passthrough = _runtimeSettings.PassthroughMode;
        string? taskType = null;
        string? routingCase = null;

        if (passthrough)
        {
            var prompt = _promptBuilder.BuildChatPrompt(request.Messages);
            var response = await _npuModel.GenerateAsync(prompt, request, ct);
            response.Id = Guid.NewGuid().ToString("N");
            response.Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            response.Model = request.Model;
            sw.Stop();
            response.NeuroRouteMeta = new NeuroRouteMetadata
            {
                Backend = "npu",
                Passthrough = true,
                DurationMs = sw.Elapsed.TotalMilliseconds
            };
            _metrics.RecordRoutingDecision("passthrough", "P", false, false, sw.Elapsed.TotalMilliseconds);
            return response;
        }

        var plan = await _planner.CreatePlanAsync(
            request.Messages,
            p => _npuModel.ClassifyAsync(p));

        sw.Stop();
        var npuIsSelected = !plan.NeedsGpu;
        taskType = plan.TaskType;
        routingCase = plan.RoutingCase;
        var implBackend = GetBackendName();

        activity?.SetTag("neuroroute.task_type", plan.TaskType);
        activity?.SetTag("neuroroute.needs_gpu", plan.NeedsGpu);
        activity?.SetTag("neuroroute.estimated_tokens", plan.EstimatedTokens);
        activity?.SetTag("neuroroute.backend", implBackend);

        _logger.LogInformation(
            "Routing decision: task_type={TaskType}, needs_gpu={NeedsGpu}, tokens={Tokens}, backend={Backend}",
            plan.TaskType, plan.NeedsGpu, plan.EstimatedTokens, implBackend);

        RoutingTelemetry.RecordRoutingDecision(plan.TaskType, plan.NeedsGpu, plan.EstimatedTokens, implBackend);
        _metrics.RecordRoutingDecision(plan.TaskType, plan.RoutingCase, plan.NeedsGpu, false, sw.Elapsed.TotalMilliseconds);
        RoutingTelemetry.RequestDuration.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("task_type", plan.TaskType),
            new KeyValuePair<string, object?>("needs_gpu", plan.NeedsGpu));

        if (plan.NeedsGpu)
        {
            var gpuRequest = BuildGpuRequest(request, plan);
            try
            {
                var gpuResponse = await _gpuClient.SendAsync(gpuRequest, ct);
                gpuResponse.NeuroRouteMeta = new NeuroRouteMetadata
                {
                    Backend = "gpu",
                    Fallback = false,
                    DurationMs = sw.Elapsed.TotalMilliseconds,
                    TaskType = taskType,
                    RoutingCase = routingCase
                };
                return gpuResponse;
            }
            catch (Exception ex) when (_runtimeSettings.GpuFallbackToNpu)
            {
                _logger.LogWarning(ex, "GPU unreachable, falling back to NPU");
                fallback = true;
                backend = "npu";
                npuIsSelected = true;
            }
        }

        if (npuIsSelected) backend = "npu";

        var npuPrompt = string.IsNullOrWhiteSpace(plan.CompressedPrompt)
            ? _promptBuilder.BuildChatPrompt(request.Messages)
            : plan.CompressedPrompt;

        var npuResponse = await _npuModel.GenerateAsync(npuPrompt, request, ct);
        npuResponse.Id = Guid.NewGuid().ToString("N");
        npuResponse.Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        npuResponse.Model = request.Model;
        npuResponse.NeuroRouteMeta = new NeuroRouteMetadata
        {
            Backend = backend,
            Fallback = fallback,
            DurationMs = sw.Elapsed.TotalMilliseconds,
            TaskType = taskType,
            RoutingCase = routingCase
        };

        return npuResponse;
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = RoutingTelemetry.ActivitySource.StartActivity("Router.StreamAsync");
        var sw = Stopwatch.StartNew();

        NeuroRouteMetadata? meta = null;
        var passthrough = _runtimeSettings.PassthroughMode;

        if (passthrough)
        {
            var prompt = _promptBuilder.BuildChatPrompt(request.Messages);
            var streamed = _npuModel.StreamAsync(prompt, request, ct);

            await foreach (var chunk in streamed)
            {
                chunk.Id = Guid.NewGuid().ToString("N");
                chunk.Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                chunk.Model = request.Model;

                if (meta is null)
                {
                    meta = new NeuroRouteMetadata
                    {
                        Backend = "npu",
                        Passthrough = true,
                        DurationMs = sw.Elapsed.TotalMilliseconds
                    };
                    chunk.NeuroRouteMeta = meta;
                }

                yield return chunk;
            }

            _metrics.RecordRoutingDecision("passthrough", "P", false, true, sw.Elapsed.TotalMilliseconds);
            yield break;
        }

        var plan = await _planner.CreatePlanAsync(
            request.Messages,
            p => _npuModel.ClassifyAsync(p));

        var implBackend = GetBackendName();
        var npuIsSelected = !plan.NeedsGpu;

        activity?.SetTag("neuroroute.task_type", plan.TaskType);
        activity?.SetTag("neuroroute.needs_gpu", plan.NeedsGpu);
        activity?.SetTag("neuroroute.estimated_tokens", plan.EstimatedTokens);
        activity?.SetTag("neuroroute.backend", implBackend);

        _logger.LogInformation(
            "Streaming routing: task_type={TaskType}, needs_gpu={NeedsGpu}, tokens={Tokens}, backend={Backend}",
            plan.TaskType, plan.NeedsGpu, plan.EstimatedTokens, implBackend);

        RoutingTelemetry.RecordRoutingDecision(plan.TaskType, plan.NeedsGpu, plan.EstimatedTokens, implBackend);
        _metrics.RecordRoutingDecision(plan.TaskType, plan.RoutingCase, plan.NeedsGpu, true, sw.Elapsed.TotalMilliseconds);

        if (plan.NeedsGpu)
        {
            var gpuRequest = BuildGpuRequest(request, plan);
            var gpuEnumerator = _gpuClient.StreamAsync(gpuRequest, ct).GetAsyncEnumerator(ct);
            bool gpuUsed;
            try
            {
                gpuUsed = await gpuEnumerator.MoveNextAsync();
            }
            catch (Exception ex) when (_runtimeSettings.GpuFallbackToNpu)
            {
                _logger.LogWarning(ex, "GPU unreachable during stream, falling back to NPU");
                gpuUsed = false;
                npuIsSelected = true;
            }

            if (gpuUsed)
            {
                do
                {
                    var chunk = gpuEnumerator.Current;
                    if (meta is null)
                    {
                        meta = new NeuroRouteMetadata
                        {
                            Backend = "gpu",
                            Fallback = false,
                            DurationMs = sw.Elapsed.TotalMilliseconds,
                            TaskType = plan.TaskType,
                            RoutingCase = plan.RoutingCase
                        };
                        chunk.NeuroRouteMeta = meta;
                    }
                    yield return chunk;
                }
                while (await gpuEnumerator.MoveNextAsync());

                await gpuEnumerator.DisposeAsync();
                yield break;
            }

            await gpuEnumerator.DisposeAsync();
        }

        var npuPrompt = string.IsNullOrWhiteSpace(plan.CompressedPrompt)
            ? _promptBuilder.BuildChatPrompt(request.Messages)
            : plan.CompressedPrompt;

        await foreach (var chunk in _npuModel.StreamAsync(npuPrompt, request, ct))
        {
            chunk.Id = Guid.NewGuid().ToString("N");
            chunk.Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            chunk.Model = request.Model;

            if (meta is null)
            {
                meta = new NeuroRouteMetadata
                {
                    Backend = npuIsSelected ? "npu" : implBackend,
                    Fallback = plan.NeedsGpu && npuIsSelected,
                    DurationMs = sw.Elapsed.TotalMilliseconds,
                    TaskType = plan.TaskType,
                    RoutingCase = plan.RoutingCase
                };
                chunk.NeuroRouteMeta = meta;
            }

            yield return chunk;
        }

        sw.Stop();
        RoutingTelemetry.RequestDuration.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("task_type", plan.TaskType),
            new KeyValuePair<string, object?>("needs_gpu", plan.NeedsGpu));
    }

    private static string GetBackendName()
    {
        return NpuModel.GetBackendName();
    }

    private ChatRequest BuildGpuRequest(ChatRequest original, NpuPlan plan)
    {
        return new ChatRequest
        {
            Model = original.Model,
            Messages = original.Messages,
            MaxTokens = original.MaxTokens,
            Temperature = original.Temperature,
            Stream = original.Stream
        };
    }
}
