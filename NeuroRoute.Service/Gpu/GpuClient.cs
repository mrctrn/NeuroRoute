using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Gpu;

public sealed class GpuClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GpuClient> _logger;
    private const int MaxRetries = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GpuClient(HttpClient httpClient, ILogger<GpuClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await _httpClient
                    .PostAsJsonAsync("/v1/chat/completions", request, ct);

                if (!response.IsSuccessStatusCode && attempt < MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 * attempt), ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var result = await response.Content
                    .ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);

                return result ?? throw new InvalidOperationException("GPU returned null response.");
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex,
                    "GPU request failed (attempt {Attempt}/{MaxRetries}), retrying...",
                    attempt, MaxRetries);
                await Task.Delay(TimeSpan.FromSeconds(1 * attempt), ct);
            }
        }

        throw new InvalidOperationException("GPU server unreachable after retries.");
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        using var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task<HttpResponseMessage> SendStreamRequestAsync(
        ChatRequest request, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
                {
                    Content = JsonContent.Create(request, options: JsonOptions)
                };

                var response = await _httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex,
                    "GPU stream request failed (attempt {Attempt}/{MaxRetries}), retrying...",
                    attempt, MaxRetries);
                await Task.Delay(TimeSpan.FromSeconds(1 * attempt), ct);
            }
        }

        throw new InvalidOperationException("GPU server unreachable after retries.");
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await SendStreamRequestAsync(request, ct);
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
}
