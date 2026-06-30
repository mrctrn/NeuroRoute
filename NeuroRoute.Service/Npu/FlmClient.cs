using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Prompts;

namespace NeuroRoute.Service.Npu;

public sealed class FlmClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FlmClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FlmClient(HttpClient httpClient, ILogger<FlmClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<NpuPlan> ClassifyAsync(string prompt, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = ClassificationSystemPrompt.Prompt },
            new() { Role = "user", Content = prompt }
        };

        var request = new ChatRequest
        {
            Model = "neuro-route",
            Messages = messages,
            MaxTokens = 256,
            Temperature = 0.1f
        };

        try
        {
            var response = await _httpClient
                .PostAsJsonAsync("/v1/chat/completions", request, JsonOptions, ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);

            var content = result?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return new NpuPlan { TaskType = "simple_chat", NeedsGpu = false };
            }

            // Extract JSON from response (FLM may wrap it in markdown)
            var json = ExtractJson(content);
            var plan = JsonSerializer.Deserialize<NpuPlan>(json, JsonOptions)
                       ?? new NpuPlan { TaskType = "simple_chat" };

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FLM classification failed, falling back to GPU");
            return new NpuPlan
            {
                TaskType = "deep_reasoning",
                NeedsGpu = true,
                Confidence = 0.5f
            };
        }
    }

    public async Task<ChatResponse> GenerateAsync(ChatRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient
            .PostAsJsonAsync("/v1/chat/completions", request, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);

        return result ?? throw new InvalidOperationException("FLM returned null response.");
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("data: "))
            {
                var data = line[6..];
                if (data == "[DONE]")
                    yield break;

                var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, JsonOptions);
                if (chunk is not null)
                    yield return chunk;
            }
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];
        return "{}";
    }
}
