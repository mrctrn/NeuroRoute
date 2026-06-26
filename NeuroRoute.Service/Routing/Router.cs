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
    private readonly GpuClient _gpuClient;
    private readonly MetricsService _metrics;
    private readonly ILogger<Router> _logger;

    public Router(
        NpuPlanner planner,
        PromptBuilder promptBuilder,
        NpuModel npuModel,
        GpuClient gpuClient,
        MetricsService metrics,
        ILogger<Router> logger)
    {
        _planner = planner;
        _promptBuilder = promptBuilder;
        _npuModel = npuModel;
        _gpuClient = gpuClient;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ChatResponse> RouteAsync(ChatRequest request, CancellationToken ct = default)
    {
        using var activity = RoutingTelemetry.ActivitySource.StartActivity("Router.RouteAsync");
        var sw = Stopwatch.StartNew();

        var plan = await _planner.CreatePlanAsync(
            request.Messages,
            prompt => _npuModel.ClassifyAsync(prompt));

        sw.Stop();
        var backend = GetBackendName();

        activity?.SetTag("neuroroute.task_type", plan.TaskType);
        activity?.SetTag("neuroroute.needs_gpu", plan.NeedsGpu);
        activity?.SetTag("neuroroute.estimated_tokens", plan.EstimatedTokens);
        activity?.SetTag("neuroroute.backend", backend);

        _logger.LogInformation(
            "Routing decision: task_type={TaskType}, needs_gpu={NeedsGpu}, tokens={Tokens}, backend={Backend}",
            plan.TaskType, plan.NeedsGpu, plan.EstimatedTokens, backend);

        RoutingTelemetry.RecordRoutingDecision(plan.TaskType, plan.NeedsGpu, plan.EstimatedTokens, backend);
        _metrics.RecordRoutingDecision(plan.TaskType, plan.RoutingCase, plan.NeedsGpu, false, sw.Elapsed.TotalMilliseconds);
        RoutingTelemetry.RequestDuration.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("task_type", plan.TaskType),
            new KeyValuePair<string, object?>("needs_gpu", plan.NeedsGpu));

        if (plan.NeedsGpu)
        {
            var gpuRequest = BuildGpuRequest(request, plan);
            var gpuResponse = await _gpuClient.SendAsync(gpuRequest, ct);
            return gpuResponse;
        }
        else
        {
            var prompt = string.IsNullOrWhiteSpace(plan.CompressedPrompt)
                ? _promptBuilder.BuildChatPrompt(request.Messages)
                : plan.CompressedPrompt;

            var npuResponse = await _npuModel.GenerateAsync(prompt, request);

            npuResponse.Id = Guid.NewGuid().ToString("N");
            npuResponse.Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            npuResponse.Model = request.Model;

            return npuResponse;
        }
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = RoutingTelemetry.ActivitySource.StartActivity("Router.StreamAsync");
        var sw = Stopwatch.StartNew();

        var plan = await _planner.CreatePlanAsync(
            request.Messages,
            prompt => _npuModel.ClassifyAsync(prompt));

        var backend = GetBackendName();

        activity?.SetTag("neuroroute.task_type", plan.TaskType);
        activity?.SetTag("neuroroute.needs_gpu", plan.NeedsGpu);
        activity?.SetTag("neuroroute.estimated_tokens", plan.EstimatedTokens);
        activity?.SetTag("neuroroute.backend", backend);

        _logger.LogInformation(
            "Streaming routing: task_type={TaskType}, needs_gpu={NeedsGpu}, tokens={Tokens}, backend={Backend}",
            plan.TaskType, plan.NeedsGpu, plan.EstimatedTokens, backend);

        RoutingTelemetry.RecordRoutingDecision(plan.TaskType, plan.NeedsGpu, plan.EstimatedTokens, backend);
        _metrics.RecordRoutingDecision(plan.TaskType, plan.RoutingCase, plan.NeedsGpu, true, sw.Elapsed.TotalMilliseconds);

        if (plan.NeedsGpu)
        {
            var gpuRequest = BuildGpuRequest(request, plan);
            await foreach (var chunk in _gpuClient.StreamAsync(gpuRequest, ct))
            {
                yield return chunk;
            }
        }
        else
        {
            var prompt = string.IsNullOrWhiteSpace(plan.CompressedPrompt)
                ? _promptBuilder.BuildChatPrompt(request.Messages)
                : plan.CompressedPrompt;

            await foreach (var chunk in _npuModel.StreamAsync(prompt, request, ct))
            {
                yield return chunk;
            }
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
