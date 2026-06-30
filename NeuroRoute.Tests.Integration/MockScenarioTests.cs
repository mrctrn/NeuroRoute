using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuroRoute.Tests.Integration;

[Collection("Api")]
public sealed class MockScenarioTests
{
    private readonly ApiCollectionFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MockScenarioTests(ApiCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetScenario_ReturnsCurrentState()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/admin/mock/scenario");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var scenario = JsonSerializer.Deserialize<ScenarioStateDto>(body, JsonOptions);

        Assert.NotNull(scenario);
        Assert.True(scenario.NpuAvailable);
        Assert.True(scenario.GpuAvailable);
        Assert.Equal("mock", scenario.NpuBackend);
        Assert.Equal("simple_chat", scenario.TaskType);
    }

    [Fact]
    public async Task PostScenario_UpdatesNpuAvailable()
    {
        await _fixture.ProgramScenarioAsync(new { npuAvailable = false });

        var response = await _fixture.HttpClient.GetAsync("/v1/admin/mock/scenario");
        var scenario = await DeserializeScenarioAsync(response);

        Assert.False(scenario.NpuAvailable);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task PostScenario_UpdatesGpuAvailable()
    {
        await _fixture.ProgramScenarioAsync(new { gpuAvailable = false });

        var response = await _fixture.HttpClient.GetAsync("/v1/admin/mock/scenario");
        var scenario = await DeserializeScenarioAsync(response);

        Assert.False(scenario.GpuAvailable);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task PostScenario_UpdatesNeedsGpu()
    {
        await _fixture.ProgramScenarioAsync(new { needsGpu = true });

        var response = await _fixture.HttpClient.GetAsync("/v1/admin/mock/scenario");
        var scenario = await DeserializeScenarioAsync(response);

        Assert.True(scenario.NeedsGpu);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task PostScenario_UpdatesNpuResponseText()
    {
        const string expectedText = "Overridden NPU text";

        await _fixture.ProgramScenarioAsync(new { npuResponseText = expectedText });

        var chatResponse = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Test" }]
        });
        var chat = await _fixture.DeserializeAsync<ChatResponseDto>(chatResponse);
        Assert.Equal(expectedText, chat.Choices[0].Message?.Content);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task PostScenario_UpdatesGpuResponseText()
    {
        const string expectedText = "Overridden GPU text";

        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            gpuResponseText = expectedText,
            gpuAvailable = true
        });

        var chatResponse = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Test" }]
        });
        var chat = await _fixture.DeserializeAsync<ChatResponseDto>(chatResponse);
        Assert.Equal(expectedText, chat.Choices[0].Message?.Content);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task PostScenario_UpdatesTaskType()
    {
        await _fixture.ProgramScenarioAsync(new { taskType = "translation" });

        var response = await _fixture.HttpClient.GetAsync("/v1/admin/mock/scenario");
        var scenario = await DeserializeScenarioAsync(response);

        Assert.Equal("translation", scenario.TaskType);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task PostScenario_UpdatesRoutingCase()
    {
        await _fixture.ProgramScenarioAsync(new { routingCase = "D" });

        var response = await _fixture.HttpClient.GetAsync("/v1/admin/mock/scenario");
        var scenario = await DeserializeScenarioAsync(response);

        Assert.Equal("D", scenario.RoutingCase);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Reset_ReturnsToDefaults()
    {
        await _fixture.ProgramScenarioAsync(new
        {
            npuAvailable = false,
            gpuAvailable = false,
            needsGpu = true,
            npuResponseText = "Changed",
            gpuResponseText = "Changed GPU",
            taskType = "code_gen",
            routingCase = "B"
        });

        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/admin/mock/scenario");
        var scenario = await DeserializeScenarioAsync(response);

        Assert.True(scenario.NpuAvailable);
        Assert.True(scenario.GpuAvailable);
        Assert.False(scenario.NeedsGpu);
        Assert.Equal("Hello from mock NPU!", scenario.NpuResponseText);
        Assert.Equal("Complex reasoning from mock GPU!", scenario.GpuResponseText);
        Assert.Equal("simple_chat", scenario.TaskType);
        Assert.Equal("C", scenario.RoutingCase);
    }

    [Fact]
    public async Task Reset_ReturnsSuccess()
    {
        var response = await _fixture.HttpClient.PostAsync("/v1/admin/mock/scenario/reset", null);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ProgramThenNpuResponse_ReflectsChange()
    {
        await _fixture.ProgramScenarioAsync(new { npuResponseText = "First message" });

        var r1 = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hi" }]
        });
        var c1 = await _fixture.DeserializeAsync<ChatResponseDto>(r1);
        Assert.Equal("First message", c1.Choices[0].Message?.Content);

        await _fixture.ProgramScenarioAsync(new { npuResponseText = "Second message" });

        var r2 = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hi" }]
        });
        var c2 = await _fixture.DeserializeAsync<ChatResponseDto>(r2);
        Assert.Equal("Second message", c2.Choices[0].Message?.Content);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Scenario_SurvivesMultipleGpuEscalations()
    {
        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            gpuResponseText = "GPU round",
            gpuAvailable = true
        });

        for (int i = 0; i < 3; i++)
        {
            var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
            {
                Messages = [new ChatMessageDto { Role = "user", Content = $"Round {i}" }]
            });
            var chat = await _fixture.DeserializeAsync<ChatResponseDto>(response);
            Assert.Equal("GPU round", chat.Choices[0].Message?.Content);
        }

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Scenario_NpuDown_Returns503Health()
    {
        await _fixture.ProgramScenarioAsync(new
        {
            npuAvailable = false,
            gpuAvailable = false
        });

        var healthResponse = await _fixture.HttpClient.GetAsync("/v1/health");
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, healthResponse.StatusCode);

        await _fixture.ResetScenarioAsync();
    }

    private async Task<ScenarioStateDto> DeserializeScenarioAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ScenarioStateDto>(body, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize: {body}");
    }
}

public sealed class ScenarioStateDto
{
    [JsonPropertyName("npuAvailable")] public bool NpuAvailable { get; set; }
    [JsonPropertyName("npuBackend")] public string NpuBackend { get; set; } = string.Empty;
    [JsonPropertyName("npuModel")] public string NpuModel { get; set; } = string.Empty;
    [JsonPropertyName("taskType")] public string TaskType { get; set; } = string.Empty;
    [JsonPropertyName("needsGpu")] public bool NeedsGpu { get; set; }
    [JsonPropertyName("routingCase")] public string RoutingCase { get; set; } = string.Empty;
    [JsonPropertyName("npuResponseText")] public string NpuResponseText { get; set; } = string.Empty;
    [JsonPropertyName("gpuResponseText")] public string GpuResponseText { get; set; } = string.Empty;
    [JsonPropertyName("gpuAvailable")] public bool GpuAvailable { get; set; }
    [JsonPropertyName("gpuModel")] public string GpuModel { get; set; } = string.Empty;
    [JsonPropertyName("gpuEndpoint")] public string GpuEndpoint { get; set; } = string.Empty;
    [JsonPropertyName("simulatedLatencyMs")] public int SimulatedLatencyMs { get; set; }
    [JsonPropertyName("streamDelayMs")] public int StreamDelayMs { get; set; }
}
