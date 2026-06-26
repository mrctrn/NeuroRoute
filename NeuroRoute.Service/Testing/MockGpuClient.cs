using System.Runtime.CompilerServices;
using NeuroRoute.Service.Gpu;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Testing;

public sealed class MockGpuClient : IGpuClient
{
    private readonly MockScenario _scenario;

    public MockGpuClient(MockScenario scenario)
    {
        _scenario = scenario;
    }

    public Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
    {
        if (_scenario.SimulatedLatencyMs > 0)
            Task.Delay(_scenario.SimulatedLatencyMs, ct).Wait(ct);

        return Task.FromResult(new ChatResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage { Role = "assistant", Content = _scenario.GpuResponseText },
                    FinishReason = "stop"
                }
            ]
        });
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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

        var words = _scenario.GpuResponseText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();

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
                        Delta = new ChatMessage { Content = word + " " }
                    }
                ]
            };

            await Task.Delay(_scenario.StreamDelayMs, ct);
        }

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
                    Delta = new ChatMessage(),
                    FinishReason = "stop"
                }
            ]
        };
    }

    public Task<bool> PingAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_scenario.GpuAvailable);
    }

    public Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(
            _scenario.GpuAvailable
                ? new List<string> { _scenario.GpuModel }
                : new List<string>());
    }
}
