using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuroRoute.Tests.Integration;

public sealed class ApiCollectionFixture : IAsyncLifetime
{
    private Process? _serviceProcess;
    private const string ServiceUrl = "http://localhost:5000";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public HttpClient HttpClient { get; } = new() { BaseAddress = new Uri(ServiceUrl) };

    public async Task InitializeAsync()
    {
        await KillProcessOnPort(5000);

        var solutionRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        var serviceRoot = Path.Combine(solutionRoot, "NeuroRoute.Service");

        if (!Directory.Exists(serviceRoot))
            throw new DirectoryNotFoundException($"Service project not found at {serviceRoot}");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{serviceRoot}\" --no-launch-profile",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.EnvironmentVariables["NeuroRoute__UseMockBackends"] = "true";
        psi.EnvironmentVariables["Kestrel__Endpoints__Http__Url"] = ServiceUrl;

        _serviceProcess = new Process { StartInfo = psi };
        _serviceProcess.Start();

        // Read stdout/stderr in background to avoid deadlocks
        _ = Task.Run(() =>
        {
            try
            {
                while (_serviceProcess?.HasExited == false)
                {
                    var line = _serviceProcess?.StandardOutput.ReadLine();
                    if (line is not null)
                        Debug.WriteLine($"[Service] {line}");
                }
            }
            catch { }
        });
        _ = Task.Run(() =>
        {
            try
            {
                while (_serviceProcess?.HasExited == false)
                {
                    var line = _serviceProcess?.StandardError.ReadLine();
                    if (line is not null)
                        Debug.WriteLine($"[Service:ERR] {line}");
                }
            }
            catch { }
        });

        await WaitForHealthAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync()
    {
        if (_serviceProcess is not null && !_serviceProcess.HasExited)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await HttpClient.PostAsync("/v1/admin/stop", null, cts.Token);
                await Task.Delay(2000);
            }
            catch { }
            try { _serviceProcess.Kill(entireProcessTree: true); } catch { }
        }

        await KillProcessOnPort(5000);
        HttpClient.Dispose();
    }

    public async Task ProgramScenarioAsync(object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await HttpClient.PostAsync("/v1/admin/mock/scenario", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResetScenarioAsync()
    {
        var response = await HttpClient.PostAsync("/v1/admin/mock/scenario/reset", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> MakeChatRequestAsync(object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await HttpClient.PostAsync("/v1/chat/completions", content);
    }

    public async Task<T> DeserializeAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize response: {body}");
    }

    private async Task WaitForHealthAsync(TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            try
            {
                var response = await client.GetAsync($"{ServiceUrl}/v1/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch { }
            await Task.Delay(500);
        }

        throw new TimeoutException($"Service did not become healthy within {timeout}");
    }

    private static async Task KillProcessOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return;

            var output = await proc.StandardOutput.ReadToEndAsync();
            foreach (var line in output.Split(Environment.NewLine))
            {
                if (line.Contains($":{port} ") && line.Contains("LISTENING"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && int.TryParse(parts[^1], out var pid))
                    {
                        try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
                    }
                }
            }
        }
        catch { }
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiCollectionFixture> { }

public sealed class ChatRequestDto
{
    [JsonPropertyName("model")] public string Model { get; set; } = "neuro-route";
    [JsonPropertyName("messages")] public List<ChatMessageDto> Messages { get; set; } = [];
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 64;
    [JsonPropertyName("temperature")] public float Temperature { get; set; } = 0.7f;
    [JsonPropertyName("stream")] public bool Stream { get; set; }
}

public sealed class ChatMessageDto
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
}

public sealed class ChatResponseDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("object")] public string Object { get; set; } = string.Empty;
    [JsonPropertyName("created")] public long Created { get; set; }
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("choices")] public List<ChatChoiceDto> Choices { get; set; } = [];
    [JsonPropertyName("usage")] public UsageInfoDto? Usage { get; set; }
}

public sealed class ChatChoiceDto
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("message")] public ChatMessageDto? Message { get; set; }
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
}

public sealed class UsageInfoDto
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
}
