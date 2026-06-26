using System.Runtime.CompilerServices;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Npu;

public sealed class FlmBackend : INpuBackend
{
    private readonly FlmClient _flmClient;
    private readonly FlmProcessManager _processManager;
    private readonly ILogger<FlmBackend> _logger;

    public FlmBackend(
        FlmClient flmClient,
        FlmProcessManager processManager,
        ILogger<FlmBackend> logger)
    {
        _flmClient = flmClient;
        _processManager = processManager;
        _logger = logger;
    }

    public async Task<NpuPlan> ClassifyAsync(string prompt, CancellationToken ct = default)
    {
        if (_processManager.Status != "healthy")
        {
            _logger.LogWarning("FLM backend not ready (status: {Status}), falling back",
                _processManager.Status);
            return new NpuPlan
            {
                TaskType = "deep_reasoning",
                NeedsGpu = true,
                Confidence = 0.5f
            };
        }

        return await _flmClient.ClassifyAsync(prompt, ct);
    }

    public async Task<ChatResponse> GenerateAsync(string prompt, ChatRequest request, CancellationToken ct = default)
    {
        if (_processManager.Status != "healthy")
        {
            var content = $"[FLM Unavailable] Response to: {prompt[..Math.Min(prompt.Length, 100)]}";
            return new ChatResponse
            {
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage { Role = "assistant", Content = content },
                        FinishReason = "stop"
                    }
                ]
            };
        }

        return await _flmClient.GenerateAsync(request, ct);
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        string prompt,
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_processManager.Status != "healthy")
        {
            var id = Guid.NewGuid().ToString("N");
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var content = $"[FLM Unavailable] Response to: {prompt[..Math.Min(prompt.Length, 100)]}";

            yield return new ChatCompletionChunk
            {
                Id = id,
                Created = created,
                Model = request.Model,
                Choices =
                [
                    new ChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatMessage { Role = "assistant" }
                    }
                ]
            };
            yield return new ChatCompletionChunk
            {
                Id = id,
                Created = created,
                Model = request.Model,
                Choices =
                [
                    new ChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatMessage { Content = content },
                        FinishReason = "stop"
                    }
                ]
            };
            yield break;
        }

        await foreach (var chunk in _flmClient.StreamAsync(request, ct))
        {
            yield return chunk;
        }
    }
}
