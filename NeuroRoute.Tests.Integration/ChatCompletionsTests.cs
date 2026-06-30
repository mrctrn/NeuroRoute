using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuroRoute.Tests.Integration;

[Collection("Api")]
public sealed class ChatCompletionsTests
{
    private readonly ApiCollectionFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ChatCompletionsTests(ApiCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task NonStreaming_RoutesToNpu_ByDefault()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }]
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);

        Assert.StartsWith("chat.completion", result.Object);
        Assert.NotEmpty(result.Id);
        Assert.True(result.Created > 0);
        Assert.Equal("neuro-route", result.Model);
        Assert.Single(result.Choices);
        Assert.Equal("assistant", result.Choices[0].Message?.Role);
        Assert.Equal("stop", result.Choices[0].FinishReason);
        Assert.NotNull(result.Usage);
        Assert.True(result.Usage.PromptTokens >= 0);
        Assert.True(result.Usage.CompletionTokens >= 0);
        Assert.True(result.Usage.TotalTokens >= 0);
    }

    [Fact]
    public async Task NonStreaming_RoutesToGpu_WhenNeedsGpuIsTrue()
    {
        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            gpuResponseText = "GPU processed this request",
            gpuAvailable = true
        });

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Complex math problem" }]
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);

        Assert.Contains("GPU", result.Choices[0].Message?.Content, StringComparison.OrdinalIgnoreCase);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task NonStreaming_NpuResponseText_AppearsInOutput()
    {
        const string expectedText = "Custom NPU response for testing";

        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = false,
            npuResponseText = expectedText
        });

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Say something" }]
        });

        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.Equal(expectedText, result.Choices[0].Message?.Content);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task NonStreaming_GpuResponseText_AppearsInOutput()
    {
        const string expectedText = "Custom GPU response for testing";

        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            gpuResponseText = expectedText,
            gpuAvailable = true
        });

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Complex task" }]
        });

        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.Equal(expectedText, result.Choices[0].Message?.Content);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task NonStreaming_PreservesModelName()
    {
        const string modelName = "neuro-route-test";

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Model = modelName,
            Messages = [new ChatMessageDto { Role = "user", Content = "Hi" }]
        });

        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.Equal(modelName, result.Model);
    }

    [Fact]
    public async Task NonStreaming_ReportsTokenUsage()
    {
        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "What is the capital of France?" }]
        });

        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.NotNull(result.Usage);
        Assert.True(result.Usage.PromptTokens > 0, "Prompt tokens should be > 0");
        Assert.True(result.Usage.CompletionTokens > 0, "Completion tokens should be > 0");
        Assert.Equal(result.Usage.PromptTokens + result.Usage.CompletionTokens, result.Usage.TotalTokens);
    }

    [Fact]
    public async Task NonStreaming_MultipleMessages_WorksCorrectly()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages =
            [
                new ChatMessageDto { Role = "system", Content = "You are a helpful assistant." },
                new ChatMessageDto { Role = "user", Content = "Hello" },
                new ChatMessageDto { Role = "assistant", Content = "Hi there!" },
                new ChatMessageDto { Role = "user", Content = "How are you?" }
            ]
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.Single(result.Choices);
        Assert.Equal("assistant", result.Choices[0].Message?.Role);
    }

    [Fact]
    public async Task NonStreaming_Temperature_IsAccepted()
    {
        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }],
            Temperature = 0.0f
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonStreaming_MaxTokens_IsRespected()
    {
        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }],
            MaxTokens = 8
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Streaming_ReturnsSseContentType()
    {
        await _fixture.ResetScenarioAsync();

        var json = JsonSerializer.Serialize(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }],
            Stream = true
        }, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Streaming_ReceivesMultipleChunks()
    {
        await _fixture.ResetScenarioAsync();

        var json = JsonSerializer.Serialize(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }],
            Stream = true,
            MaxTokens = 32
        }, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        var body = await response.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var chunks = lines
            .Where(l => l.StartsWith("data: ") && l != "data: [DONE]")
            .Select(l => l[6..])
            .ToList();

        Assert.NotEmpty(chunks);
    }

    [Fact]
    public async Task Streaming_EndsWithDoneSignal()
    {
        await _fixture.ResetScenarioAsync();

        var json = JsonSerializer.Serialize(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }],
            Stream = true
        }, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: [DONE]", body);
    }

    [Fact]
    public async Task Streaming_ContainsValidChunkJson()
    {
        await _fixture.ResetScenarioAsync();

        var json = JsonSerializer.Serialize(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }],
            Stream = true
        }, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        var body = await response.Content.ReadAsStringAsync();
        var dataLines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("data: ") && l != "data: [DONE]");

        foreach (var line in dataLines)
        {
            var chunkJson = line[6..];
            var chunk = JsonSerializer.Deserialize<StreamChunkDto>(chunkJson, JsonOptions);
            Assert.NotNull(chunk);
            Assert.NotEmpty(chunk.Id);
            Assert.Equal("chat.completion.chunk", chunk.Object);
            Assert.NotEmpty(chunk.Choices);
        }
    }

    [Fact]
    public async Task Streaming_AccumulatesContentFromDeltas()
    {
        await _fixture.ResetScenarioAsync();

        var json = JsonSerializer.Serialize(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }],
            Stream = true
        }, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        var body = await response.Content.ReadAsStringAsync();
        var dataLines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("data: ") && l != "data: [DONE]");

        var accumulated = new StringBuilder();
        foreach (var line in dataLines)
        {
            var chunkJson = line[6..];
            var chunk = JsonSerializer.Deserialize<StreamChunkDto>(chunkJson, JsonOptions);
            if (chunk?.Choices.Count > 0)
                accumulated.Append(chunk.Choices[0].Delta?.Content);
        }

        Assert.NotEmpty(accumulated.ToString());
    }

    [Fact]
    public async Task Streaming_Gpu_CorrectlyStreams()
    {
        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            gpuResponseText = "GPU streaming works",
            gpuAvailable = true
        });

        var json = JsonSerializer.Serialize(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Stream this" }],
            Stream = true
        }, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: [DONE]", body);

        var dataLines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("data: ") && l != "data: [DONE]");

        var accumulated = new StringBuilder();
        foreach (var line in dataLines)
        {
            var chunkJson = line[6..];
            var chunk = JsonSerializer.Deserialize<StreamChunkDto>(chunkJson, JsonOptions);
            if (chunk?.Choices.Count > 0)
                accumulated.Append(chunk.Choices[0].Delta?.Content);
        }

        Assert.Contains("GPU", accumulated.ToString());

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task NonStreaming_PassthroughMode_SendsToNpu()
    {
        await _fixture.ResetScenarioAsync();

        var settingsResponse = await _fixture.HttpClient.PostAsync("/v1/admin/settings",
            new StringContent("{\"passthroughMode\":true}", Encoding.UTF8, "application/json"));
        settingsResponse.EnsureSuccessStatusCode();

        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            npuResponseText = "Passthrough NPU response",
            gpuAvailable = false
        });

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }]
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);

        Assert.Equal("Passthrough NPU response", result.Choices[0].Message?.Content);

        await _fixture.HttpClient.PostAsync("/v1/admin/settings",
            new StringContent("{\"passthroughMode\":false}", Encoding.UTF8, "application/json"));
        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task NonStreaming_GpuFallback_ReturnsNpuResponse()
    {
        await _fixture.ResetScenarioAsync();

        await _fixture.HttpClient.PostAsync("/v1/admin/settings",
            new StringContent("{\"passthroughMode\":false,\"gpuFallbackToNpu\":true}", Encoding.UTF8, "application/json"));

        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            npuResponseText = "Fallback NPU response",
            gpuAvailable = false
        });

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Complex task" }]
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);

        Assert.Equal("Fallback NPU response", result.Choices[0].Message?.Content);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task NonStreaming_GpuFallbackDisabled_ThrowsError()
    {
        await _fixture.ResetScenarioAsync();

        await _fixture.HttpClient.PostAsync("/v1/admin/settings",
            new StringContent("{\"passthroughMode\":false,\"gpuFallbackToNpu\":false}", Encoding.UTF8, "application/json"));

        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            gpuAvailable = false
        });

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Complex task" }]
        });

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        await _fixture.HttpClient.PostAsync("/v1/admin/settings",
            new StringContent("{\"gpuFallbackToNpu\":true}", Encoding.UTF8, "application/json"));
        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task NonStreaming_ContainsNeuroRouteMetadata()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }]
        });

        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.NotNull(result.NeuroRouteMeta);
        Assert.Equal("npu", result.NeuroRouteMeta.Backend);
        Assert.False(result.NeuroRouteMeta.Fallback);
        Assert.False(result.NeuroRouteMeta.Passthrough);
        Assert.True(result.NeuroRouteMeta.DurationMs > 0);
        Assert.Equal("simple_chat", result.NeuroRouteMeta.TaskType);
    }

    [Fact]
    public async Task NonStreaming_NeuroRouteMetadata_OnGpuRoute()
    {
        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            gpuResponseText = "GPU response with meta",
            gpuAvailable = true
        });

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Complex" }]
        });

        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.NotNull(result.NeuroRouteMeta);
        Assert.Equal("gpu", result.NeuroRouteMeta.Backend);
        Assert.False(result.NeuroRouteMeta.Fallback);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task NonStreaming_NeuroRouteMetadata_OnFallback()
    {
        await _fixture.HttpClient.PostAsync("/v1/admin/settings",
            new StringContent("{\"passthroughMode\":false,\"gpuFallbackToNpu\":true}", Encoding.UTF8, "application/json"));

        await _fixture.ProgramScenarioAsync(new
        {
            needsGpu = true,
            npuResponseText = "Fallback meta test",
            gpuAvailable = false
        });

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Complex" }]
        });

        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.NotNull(result.NeuroRouteMeta);
        Assert.Equal("npu", result.NeuroRouteMeta.Backend);
        Assert.True(result.NeuroRouteMeta.Fallback);

        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task NonStreaming_NeuroRouteMetadata_PassthroughMode()
    {
        await _fixture.HttpClient.PostAsync("/v1/admin/settings",
            new StringContent("{\"passthroughMode\":true}", Encoding.UTF8, "application/json"));

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }]
        });

        var result = await _fixture.DeserializeAsync<ChatResponseDto>(response);
        Assert.NotNull(result.NeuroRouteMeta);
        Assert.True(result.NeuroRouteMeta.Passthrough);
        Assert.Null(result.NeuroRouteMeta.TaskType);
        Assert.Null(result.NeuroRouteMeta.RoutingCase);

        await _fixture.HttpClient.PostAsync("/v1/admin/settings",
            new StringContent("{\"passthroughMode\":false}", Encoding.UTF8, "application/json"));
    }

    [Fact]
    public async Task NonStreaming_ContainsHttpHeaders()
    {
        await _fixture.ResetScenarioAsync();

        var json = System.Text.Json.JsonSerializer.Serialize(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }]
        }, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        Assert.True(response.Headers.Contains("X-NeuroRoute-Mode"));
        Assert.True(response.Headers.Contains("X-NeuroRoute-Backend"));
        Assert.True(response.Headers.Contains("X-NeuroRoute-Fallback"));
        Assert.True(response.Headers.Contains("X-NeuroRoute-Duration-Ms"));
    }

    [Fact]
    public async Task Streaming_LastChunk_HasFinishReason()
    {
        await _fixture.ResetScenarioAsync();

        var json = JsonSerializer.Serialize(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = "Hello" }],
            Stream = true
        }, JsonOptions);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        var body = await response.Content.ReadAsStringAsync();
        var dataLines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("data: ") && l != "data: [DONE]")
            .ToList();

        var lastChunkJson = dataLines[^1][6..];
        var lastChunk = JsonSerializer.Deserialize<StreamChunkDto>(lastChunkJson, JsonOptions);
        Assert.NotNull(lastChunk);
        Assert.Equal("stop", lastChunk.Choices[0].FinishReason);
    }
}

public sealed class StreamChunkDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("object")] public string Object { get; set; } = string.Empty;
    [JsonPropertyName("created")] public long Created { get; set; }
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("choices")] public List<ChunkChoiceDto> Choices { get; set; } = [];
}

public sealed class ChunkChoiceDto
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("delta")] public DeltaDto? Delta { get; set; }
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
}

public sealed class DeltaDto
{
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
}
