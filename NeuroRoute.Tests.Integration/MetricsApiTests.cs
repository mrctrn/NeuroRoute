using System.Text.Json.Serialization;

namespace NeuroRoute.Tests.Integration;

[Collection("Api")]
public sealed class MetricsApiTests
{
    private readonly ApiCollectionFixture _fixture;

    public MetricsApiTests(ApiCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Metrics_ReturnsSuccess()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/metrics");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_CanQuery()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/metrics");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Metrics_CountsIncrementOnNpuRequest()
    {
        await _fixture.ResetScenarioAsync();

        var before = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }]
        });

        var after = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        Assert.Equal(before.TotalRequests + 1, after.TotalRequests);
        Assert.Equal(before.NpuHandled + 1, after.NpuHandled);
        Assert.Equal(before.GpuEscalated, after.GpuEscalated);
    }

    [Fact]
    public async Task Metrics_CountsIncrementOnGpuRequest()
    {
        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            gpuAvailable = true
        });

        var before = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Complex task" }]
        });

        var after = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        Assert.Equal(before.TotalRequests + 1, after.TotalRequests);
        Assert.Equal(before.GpuEscalated + 1, after.GpuEscalated);
        Assert.Equal(before.NpuHandled, after.NpuHandled);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Metrics_StreamingRequestIncrementsStreamingCount()
    {
        await _fixture.ResetScenarioAsync();

        var before = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        var json = System.Text.Json.JsonSerializer.Serialize(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }],
            Stream = true
        }, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        var after = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        Assert.Equal(before.StreamingRequests + 1, after.StreamingRequests);
    }

    [Fact]
    public async Task Metrics_TaskTypesAreRecorded()
    {
        await _fixture.ProgramScenarioAsync(new { taskType = "code_gen" });

        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Write code" }]
        });

        var response = await _fixture.HttpClient.GetAsync("/v1/metrics");
        var metrics = await _fixture.DeserializeAsync<MetricsResponseDto>(response);

        Assert.True(metrics.ByTaskType.ContainsKey("code_gen"));

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Metrics_MultipleTaskTypesAreRecorded()
    {
        await _fixture.ProgramScenarioAsync(new { taskType = "simple_chat" });
        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hi" }]
        });

        await _fixture.ProgramScenarioAsync(new { taskType = "summarization" });
        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Summarize this" }]
        });

        var response = await _fixture.HttpClient.GetAsync("/v1/metrics");
        var metrics = await _fixture.DeserializeAsync<MetricsResponseDto>(response);

        Assert.True(metrics.ByTaskType.ContainsKey("simple_chat"));
        Assert.True(metrics.ByTaskType.ContainsKey("summarization"));

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Metrics_RoutingCasesAreRecorded()
    {
        await _fixture.ResetScenarioAsync();

        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Test" }]
        });

        var response = await _fixture.HttpClient.GetAsync("/v1/metrics");
        var metrics = await _fixture.DeserializeAsync<MetricsResponseDto>(response);

        // Short prompt without compression gets RoutingCase "A" from planner
        Assert.True(metrics.ByCase.ContainsKey("case_A"));
    }

    [Fact]
    public async Task Metrics_DurationStatsExist()
    {
        var before = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }]
        });

        var after = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        Assert.NotNull(after.DurationMs);
    }

    [Fact]
    public async Task Metrics_CumulativeAcrossMultipleRequests()
    {
        var before = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "A" }]
        });
        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "B" }]
        });
        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "C" }]
        });

        var after = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        Assert.Equal(before.TotalRequests + 3, after.TotalRequests);
        Assert.Equal(before.NpuHandled + 3, after.NpuHandled);
    }

    [Fact]
    public async Task Metrics_MixedNpuAndGpuRouting()
    {
        var before = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        await _fixture.ProgramScenarioAsync(new { needsGpu = false });
        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Simple" }]
        });

        await _fixture.ProgramScenarioAsync(new { needsGpu = true, gpuAvailable = true });
        await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Complex" }]
        });

        var after = await _fixture.DeserializeAsync<MetricsResponseDto>(
            await _fixture.HttpClient.GetAsync("/v1/metrics"));

        Assert.Equal(before.TotalRequests + 2, after.TotalRequests);
        Assert.Equal(before.NpuHandled + 1, after.NpuHandled);
        Assert.Equal(before.GpuEscalated + 1, after.GpuEscalated);

        await _fixture.ResetScenarioAsync();
    }
}

public sealed class MetricsResponseDto
{
    [JsonPropertyName("totalRequests")] public long TotalRequests { get; set; }
    [JsonPropertyName("npuHandled")] public long NpuHandled { get; set; }
    [JsonPropertyName("gpuEscalated")] public long GpuEscalated { get; set; }
    [JsonPropertyName("streamingRequests")] public long StreamingRequests { get; set; }
    [JsonPropertyName("byTaskType")] public Dictionary<string, long> ByTaskType { get; set; } = [];
    [JsonPropertyName("byCase")] public Dictionary<string, long> ByCase { get; set; } = [];
    [JsonPropertyName("durationMs")] public DurationStatsDto? DurationMs { get; set; }
}

public sealed class DurationStatsDto
{
    [JsonPropertyName("min")] public double Min { get; set; }
    [JsonPropertyName("max")] public double Max { get; set; }
}
