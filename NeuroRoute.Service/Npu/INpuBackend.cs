using System.Runtime.CompilerServices;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Npu;

public interface INpuBackend
{
    Task<NpuPlan> ClassifyAsync(string prompt, CancellationToken ct = default);
    Task<ChatResponse> GenerateAsync(string prompt, ChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ChatCompletionChunk> StreamAsync(string prompt, ChatRequest request, CancellationToken ct = default);
}
