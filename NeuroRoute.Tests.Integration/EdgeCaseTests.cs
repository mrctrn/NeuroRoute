using System.Text;

namespace NeuroRoute.Tests.Integration;

[Collection("Api")]
public sealed class EdgeCaseTests
{
    private readonly ApiCollectionFixture _fixture;

    public EdgeCaseTests(ApiCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EmptyMessagesList_ReturnsResponse()
    {
        var json = "{\"model\":\"neuro-route\",\"messages\":[]}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingModelField_StillWorksWithDefault()
    {
        await _fixture.ResetScenarioAsync();

        var json = "{\"messages\":[{\"role\":\"user\",\"content\":\"Hello\"}]}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        // Should default to some model handling
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownModel_IsAccepted()
    {
        await _fixture.ResetScenarioAsync();

        var json = "{\"model\":\"nonexistent-model-12345\",\"messages\":[{\"role\":\"user\",\"content\":\"Hello\"}]}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SpecialCharacters_InMessage_WorksCorrectly()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages =
            [
                new ChatMessageDto
                {
                    Role = "user",
                    Content = "Hello! @#$%^&*() Special chars: éüñäö. Emoji: 🎉🚀"
                }
            ]
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SystemMessage_IsAccepted()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages =
            [
                new ChatMessageDto { Role = "system", Content = "You are a helpful assistant." },
                new ChatMessageDto { Role = "user", Content = "Hello" }
            ]
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MultipleUserMessages_WorksCorrectly()
    {
        await _fixture.ResetScenarioAsync();

        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages =
            [
                new ChatMessageDto { Role = "user", Content = "First message" },
                new ChatMessageDto { Role = "assistant", Content = "First response" },
                new ChatMessageDto { Role = "user", Content = "Second message" }
            ]
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LargeMessage_WithinNpuLimit_Succeeds()
    {
        await _fixture.ResetScenarioAsync();

        var longContent = new string('A', 10000);
        var response = await _fixture.MakeChatRequestAsync(new ChatRequestDto
        {
            Messages = [new ChatMessageDto { Role = "user", Content = longContent }]
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ZeroMaxTokens_IsAccepted()
    {
        await _fixture.ResetScenarioAsync();

        var json = "{\"model\":\"neuro-route\",\"messages\":[{\"role\":\"user\",\"content\":\"Hi\"}],\"max_tokens\":0}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidJsonBody_Returns400()
    {
        using var content = new StringContent("this is not json", Encoding.UTF8, "application/json");
        var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyBody_Returns400()
    {
        using var content = new StringContent("", Encoding.UTF8, "application/json");
        var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidContentType_Returns415()
    {
        using var content = new StringContent("hello", Encoding.UTF8, "text/plain");
        var response = await _fixture.HttpClient.PostAsync("/v1/chat/completions", content);

        Assert.Equal(System.Net.HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Health_InvalidMethod_Returns405()
    {
        var response = await _fixture.HttpClient.PostAsync("/v1/health", null);

        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_InvalidMethod_Returns405()
    {
        var json = "{}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _fixture.HttpClient.PostAsync("/v1/metrics", content);

        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task UnknownEndpoint_Returns404()
    {
        var response = await _fixture.HttpClient.GetAsync("/v1/nonexistent");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdminConfigReload_IsReachable()
    {
        var response = await _fixture.HttpClient.PostAsync("/v1/admin/reload-config", null);

        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.OK ||
            response.StatusCode == System.Net.HttpStatusCode.InternalServerError,
            $"Expected 200 or 500, got {(int)response.StatusCode}");
    }
}
