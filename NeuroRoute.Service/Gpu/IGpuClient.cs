using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Gpu;

public interface IGpuClient
{
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatRequest request, CancellationToken ct = default);
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default);
}
