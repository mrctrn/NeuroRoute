using System.Runtime.CompilerServices;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;

namespace NeuroRoute.Service.Testing;

public sealed class MockNpuBackend : INpuBackend
{
    private readonly MockScenario _scenario;

    public MockNpuBackend(MockScenario scenario)
    {
        _scenario = scenario;
    }

    public Task<NpuPlan> ClassifyAsync(string prompt, CancellationToken ct = default)
    {
        return Task.FromResult(new NpuPlan
        {
            TaskType = _scenario.TaskType,
            NeedsGpu = _scenario.NeedsGpu,
            RoutingCase = _scenario.RoutingCase,
            EstimatedTokens = prompt.Length / 4,
            Confidence = 0.95f
        });
    }

    public async Task<ChatResponse> GenerateAsync(string prompt, ChatRequest request, CancellationToken ct = default)
    {
        if (_scenario.SimulatedLatencyMs > 0)
            await Task.Delay(_scenario.SimulatedLatencyMs, ct);

        return new ChatResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage { Role = "assistant", Content = _scenario.NpuResponseText },
                    FinishReason = "stop"
                }
            ],
            Usage = new UsageInfo
            {
                PromptTokens = prompt.Length / 4,
                CompletionTokens = 10,
                TotalTokens = prompt.Length / 4 + 10
            }
        };
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        string prompt,
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

        var words = _scenario.NpuResponseText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
}
