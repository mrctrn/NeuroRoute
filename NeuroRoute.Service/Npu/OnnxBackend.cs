using System.Runtime.CompilerServices;
using System.Text.Json;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Prompts;

namespace NeuroRoute.Service.Npu;

public sealed class OnnxBackend : INpuBackend
{
    private readonly OnnxSessionFactory _sessionFactory;
    private readonly ILogger<OnnxBackend> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OnnxBackend(OnnxSessionFactory sessionFactory, ILogger<OnnxBackend> logger)
    {
        _sessionFactory = sessionFactory;
        _logger = logger;
    }

    public async Task<NpuPlan> ClassifyAsync(string prompt, CancellationToken ct = default)
    {
        if (!TryGetSession(out var message))
        {
            return new NpuPlan
            {
                TaskType = "simple_chat",
                NeedsGpu = false,
                EstimatedTokens = prompt.Length / 4,
                Confidence = 0.8f
            };
        }

        try
        {
            var classifyPrompt = $"{ClassificationSystemPrompt.Prompt}\n\nUSER INPUT:\n{prompt}";
            var json = await RunInferenceAsync(classifyPrompt, "classify", ct);
            var plan = JsonSerializer.Deserialize<NpuPlan>(json, JsonOptions)
                       ?? new NpuPlan { TaskType = "simple_chat" };

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ONNX classification inference failed, falling back to GPU");
            return new NpuPlan
            {
                TaskType = "deep_reasoning",
                NeedsGpu = true,
                Confidence = 0.5f
            };
        }
    }

    public async Task<ChatResponse> GenerateAsync(string prompt, ChatRequest request, CancellationToken ct = default)
    {
        if (!TryGetSession(out _))
        {
            return StubGenerate(prompt);
        }

        try
        {
            var output = await RunInferenceAsync(prompt, "generate", ct);
            return new ChatResponse
            {
                Choices =
                [
                    new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessage { Role = "assistant", Content = output },
                        FinishReason = "stop"
                    }
                ]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ONNX generation failed");
            throw;
        }
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
                    Delta = new ChatMessage { Role = "assistant", Content = "" }
                }
            ]
        };

        var response = await GenerateAsync(prompt, request, ct);
        var content = response.Choices[0].Message?.Content ?? "";

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
    }

    private bool TryGetSession(out string? message)
    {
        try
        {
            _sessionFactory.GetOrCreateSession();
            message = null;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ONNX model not available, using fallback");
            message = ex.Message;
            return false;
        }
    }

    private static async Task<string> RunInferenceAsync(string prompt, string mode, CancellationToken ct)
    {
        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();
        return mode == "classify"
            ? """{"task_type":"simple_chat","needs_gpu":false,"compressed_prompt":"","notes_for_gpu":""}"""
            : $"[NPU] Generated: {prompt[..Math.Min(prompt.Length, 100)]}";
    }

    private static ChatResponse StubGenerate(string prompt)
    {
        var content = $"[NPU] Response to: {prompt[..Math.Min(prompt.Length, 100)]}";
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
}
