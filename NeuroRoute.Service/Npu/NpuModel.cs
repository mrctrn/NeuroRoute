using System.Runtime.CompilerServices;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Npu;

public sealed class NpuModel
{
    private readonly INpuBackend _backend;
    private readonly ILogger<NpuModel> _logger;

    public NpuModel(INpuBackend backend, ILogger<NpuModel> logger)
    {
        _backend = backend;
        _logger = logger;
    }

    public Task<NpuPlan> ClassifyAsync(string prompt, CancellationToken ct = default)
    {
        _logger.LogDebug("Classifying via {Backend}", _backend.GetType().Name);
        return _backend.ClassifyAsync(prompt, ct);
    }

    public Task<ChatResponse> GenerateAsync(string prompt, ChatRequest request, CancellationToken ct = default)
    {
        _logger.LogDebug("Generating via {Backend}", _backend.GetType().Name);
        return _backend.GenerateAsync(prompt, request, ct);
    }

    public static string GetBackendName()
    {
        return BackendName;
    }

    internal static string BackendName { get; set; } = "onnx";

    public IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        string prompt,
        ChatRequest request,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Streaming via {Backend}", _backend.GetType().Name);
        return _backend.StreamAsync(prompt, request, ct);
    }
}
