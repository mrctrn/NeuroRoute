using System.Text.Json.Serialization;

namespace NeuroRoute.Tests.Integration;

[Collection("Api")]
public sealed class HealthApiTests
{
    private readonly ApiCollectionFixture _fixture;

    public HealthApiTests(ApiCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Health_Returns200_WhenAllHealthy()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);
        Assert.Equal("healthy", health.Status);
    }

    [Fact]
    public async Task Health_Returns200_WhenDegraded()
    {
        await _fixture.ProgramScenarioAsync(new { npuAvailable = false });

        var response = await _fixture.HttpClient.GetAsync("/v1/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);
        Assert.Equal("degraded", health.Status);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Health_Returns503_WhenUnhealthy()
    {
        await _fixture.ProgramScenarioAsync(new
        {
            npuAvailable = false,
            gpuAvailable = false
        });

        var response = await _fixture.HttpClient.GetAsync("/v1/health");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);
        Assert.Equal("unhealthy", health.Status);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Health_ReturnsVersionString()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/health");
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);

        Assert.NotEmpty(health.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+", health.Version);
    }

    [Fact]
    public async Task Health_ReturnsUptimeString()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/health");
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);

        Assert.NotEmpty(health.Uptime);
    }

    [Fact]
    public async Task Health_ReturnsNpuComponent()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/health");
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);

        Assert.NotNull(health.Components);
        Assert.True(health.Components.ContainsKey("npu"));
        Assert.Equal("healthy", health.Components["npu"].Status);
        Assert.Equal("mock", health.Components["npu"].Backend);
        Assert.True(health.Components["npu"].ModelLoaded);
    }

    [Fact]
    public async Task Health_ReturnsGpuComponent()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/health");
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);

        Assert.NotNull(health.Components);
        Assert.True(health.Components.ContainsKey("gpu"));
    }

    [Fact]
    public async Task Health_GpuComponent_ShowsUnhealthy_WhenGpuDown()
    {
        await _fixture.ProgramScenarioAsync(new { gpuAvailable = false });

        var response = await _fixture.HttpClient.GetAsync("/v1/health");
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);

        Assert.Equal("unhealthy", health.Components["gpu"].Status);
        Assert.False(health.Components["gpu"].ModelLoaded);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Health_NpuComponent_ShowsUnhealthy_WhenNpuDown()
    {
        await _fixture.ProgramScenarioAsync(new { npuAvailable = false });

        var response = await _fixture.HttpClient.GetAsync("/v1/health");
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);

        Assert.Equal("unhealthy", health.Components["npu"].Status);
        Assert.False(health.Components["npu"].ModelLoaded);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task Health_GpuComponent_HasCorrectBackend()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/health");
        var health = await _fixture.DeserializeAsync<HealthResponseDto>(response);

        Assert.Equal("mock", health.Components["npu"].Backend);
    }

    [Fact]
    public async Task Health_ResponseIsValidJson()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.HttpClient.GetAsync("/v1/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.NotNull(body);
        Assert.StartsWith("{", body);
        Assert.EndsWith("}", body);
    }
}

public sealed class HealthResponseDto
{
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("uptime")] public string Uptime { get; set; } = string.Empty;
    [JsonPropertyName("components")] public Dictionary<string, ComponentHealthDto> Components { get; set; } = [];
}

public sealed class ComponentHealthDto
{
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("backend")] public string Backend { get; set; } = string.Empty;
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = string.Empty;
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("model_loaded")] public bool ModelLoaded { get; set; }
}
