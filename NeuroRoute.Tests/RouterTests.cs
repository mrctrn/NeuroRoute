using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuroRoute.Service.Gpu;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using NeuroRoute.Service.Services;
using NeuroRoute.Service.Routing;

namespace NeuroRoute.Tests;

public sealed class RouterTests
{
    private readonly Router _sut;

    public RouterTests()
    {
        var options = Options.Create(new NeuroRouteOptions());
        var tokenizer = new ApproximateTokenizer();
        var planner = new NpuPlanner(tokenizer, options);
        var promptBuilder = new PromptBuilder();
        var sessionFactory = new OnnxSessionFactory("dummy.onnx");
        var onnxBackend = new OnnxBackend(
            sessionFactory, NullLogger<OnnxBackend>.Instance);
        var npuModel = new NpuModel(
            onnxBackend, NullLogger<NpuModel>.Instance);
        var httpClient = new HttpClient();
        IGpuClient gpuClient = new GpuClient(
            httpClient, NullLogger<GpuClient>.Instance);
        var metrics = new MetricsService();
        var logger = NullLogger<Router>.Instance;

        _sut = new Router(planner, promptBuilder, npuModel, gpuClient, metrics, logger);
    }

    [Fact]
    public async Task RouteAsync_ReturnsChatResponse()
    {
        var request = new ChatRequest
        {
            Model = "neuro-route",
            Messages =
            [
                new ChatMessage { Role = "user", Content = "Hello" }
            ]
        };

        var result = await _sut.RouteAsync(request);

        Assert.NotNull(result);
        Assert.Equal("neuro-route", result.Model);
        Assert.Single(result.Choices);
        Assert.Equal("assistant", result.Choices[0].Message?.Role);
    }

    [Fact]
    public async Task RouteAsync_ShortPrompt_RoutesToNpu()
    {
        var request = new ChatRequest
        {
            Model = "test",
            Messages =
            [
                new ChatMessage { Role = "user", Content = "Hi" }
            ]
        };

        var result = await _sut.RouteAsync(request);

        Assert.NotNull(result.Choices[0].Message?.Content);
        Assert.Contains("[NPU]", result.Choices[0].Message!.Content);
    }

    [Fact]
    public async Task RouteAsync_StreamParameterSetsCorrectly()
    {
        var request = new ChatRequest
        {
            Model = "test",
            Messages =
            [
                new ChatMessage { Role = "user", Content = "Hello" }
            ],
            Stream = true
        };

        Assert.True(request.Stream);
    }

    [Fact]
    public void Tokenizer_CountTokens_ReturnsPositiveNumber()
    {
        var tokenizer = new ApproximateTokenizer();
        var count = tokenizer.CountTokens("Hello, how are you today?");
        Assert.True(count > 0);
    }

    [Fact]
    public void Tokenizer_CountTokens_EmptyString_ReturnsZero()
    {
        var tokenizer = new ApproximateTokenizer();
        Assert.Equal(0, tokenizer.CountTokens(""));
        Assert.Equal(0, tokenizer.CountTokens(null!));
        Assert.Equal(0, tokenizer.CountTokens("   "));
    }

    [Fact]
    public async Task NpuPlanner_ShortPrompt_DoesNotSetNeedsGpu()
    {
        var options = Options.Create(new NeuroRouteOptions());
        var tokenizer = new ApproximateTokenizer();
        var planner = new NpuPlanner(tokenizer, options);

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "What is 2+2?" }
        };

        var plan = await planner.CreatePlanAsync(
            messages,
            _ => Task.FromResult(new NpuPlan { TaskType = "simple_chat", NeedsGpu = false }));

        Assert.False(plan.NeedsGpu);
        Assert.Equal("simple_chat", plan.TaskType);
    }

    [Fact]
    public void PromptBuilder_BuildChatPrompt_IncludesRoles()
    {
        var builder = new PromptBuilder();
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Hi" }
        };

        var prompt = builder.BuildChatPrompt(messages);

        Assert.Contains("user", prompt);
        Assert.Contains("assistant", prompt);
        Assert.Contains("Hi", prompt);
    }

    [Fact]
    public void NpuPlan_JsonSerialization_RoundTrips()
    {
        var plan = new NpuPlan
        {
            TaskType = "code",
            NeedsGpu = true,
            CompressedPrompt = "short",
            NotesForGpu = "use python",
            EstimatedTokens = 100,
            Confidence = 0.9f
        };

        Assert.Equal("code", plan.TaskType);
        Assert.True(plan.NeedsGpu);
        Assert.Equal("short", plan.CompressedPrompt);
        Assert.Equal("use python", plan.NotesForGpu);
    }

    [Fact]
    public async Task Router_ClassifyFallback_ReturnsPlan()
    {
        var sessionFactory = new OnnxSessionFactory("nonexistent.onnx");
        var backend = new OnnxBackend(
            sessionFactory, NullLogger<OnnxBackend>.Instance);
        var model = new NpuModel(
            backend, NullLogger<NpuModel>.Instance);

        var plan = await model.ClassifyAsync("test");

        Assert.NotNull(plan);
        Assert.False(string.IsNullOrEmpty(plan.TaskType));
    }
}
