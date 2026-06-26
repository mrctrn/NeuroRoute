using NeuroRoute.Service.Gpu;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using NeuroRoute.Service.Routing;
using NeuroRoute.Service.Services;
using NeuroRoute.Service.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

builder.Services.Configure<NeuroRouteOptions>(
    builder.Configuration.GetSection(NeuroRouteOptions.SectionName));

builder.Services.AddControllers();

builder.Services.AddSingleton<ITokenizer, ApproximateTokenizer>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<NpuPlanner>();
builder.Services.AddSingleton<Router>();
builder.Services.AddSingleton<HealthService>();
builder.Services.AddSingleton<MetricsService>();

// Mock or real backends based on config
var neuroRouteSection = builder.Configuration.GetSection(NeuroRouteOptions.SectionName);
var useMockBackends = neuroRouteSection.GetValue<bool>("UseMockBackends");

if (useMockBackends)
{
    NpuModel.BackendName = "mock";
    builder.Services.AddSingleton<MockScenario>();
    builder.Services.AddSingleton<INpuBackend, MockNpuBackend>();
    builder.Services.AddSingleton<IGpuClient, MockGpuClient>();
}
else
{
    // ONNX backend
    builder.Services.AddSingleton<OnnxSessionFactory>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var modelPath = config.GetSection("NeuroRoute")["NpuModelPath"]
                        ?? "Models/gemma-4-int4.onnx";
        return new OnnxSessionFactory(modelPath);
    });
    builder.Services.AddSingleton<OnnxBackend>();

    // FLM backend
    builder.Services.AddHttpClient<FlmClient>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
    });
    builder.Services.AddSingleton(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var modelTag = config.GetSection("NeuroRoute")["NpuFlmModelTag"]
                       ?? "gemma4-it:e4b";
        var flmClient = sp.GetRequiredService<FlmClient>();
        var logger = sp.GetRequiredService<ILogger<FlmProcessManager>>();
        return new FlmProcessManager(modelTag, flmClient, logger);
    });
    builder.Services.AddSingleton<FlmBackend>();

    // Select NPU backend based on configuration
    builder.Services.AddSingleton<INpuBackend>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var backend = config.GetSection("NeuroRoute")["NpuBackend"] ?? "onnx";

        if (backend.Equals("flm", StringComparison.OrdinalIgnoreCase))
        {
            NpuModel.BackendName = "flm";
            return sp.GetRequiredService<FlmBackend>();
        }

        NpuModel.BackendName = "onnx";
        return sp.GetRequiredService<OnnxBackend>();
    });

    // GPU HTTP client
    builder.Services.AddHttpClient<GpuClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<NeuroRouteOptions>>().Value;
        client.BaseAddress = new Uri(options.GpuEndpoint);
        client.Timeout = TimeSpan.FromSeconds(options.GpuTimeoutSeconds);
    });
}

// NpuModel uses the selected INpuBackend
builder.Services.AddSingleton<NpuModel>();

var app = builder.Build();
app.UseMiddleware<NeuroRoute.Service.Diagnostics.RequestLoggingMiddleware>();
app.MapControllers();

if (useMockBackends)
{
    var mockGroup = app.MapGroup("/v1/admin/mock");

    mockGroup.MapGet("/scenario", (MockScenario s) => s);

    mockGroup.MapPost("/scenario", (MockScenario s, MockScenarioRequest update) =>
    {
        if (update.NpuAvailable.HasValue) s.NpuAvailable = update.NpuAvailable.Value;
        if (update.NpuBackend is not null) s.NpuBackend = update.NpuBackend;
        if (update.NpuModel is not null) s.NpuModel = update.NpuModel;
        if (update.TaskType is not null) s.TaskType = update.TaskType;
        if (update.NeedsGpu.HasValue) s.NeedsGpu = update.NeedsGpu.Value;
        if (update.RoutingCase is not null) s.RoutingCase = update.RoutingCase;
        if (update.NpuResponseText is not null) s.NpuResponseText = update.NpuResponseText;
        if (update.GpuResponseText is not null) s.GpuResponseText = update.GpuResponseText;
        if (update.GpuAvailable.HasValue) s.GpuAvailable = update.GpuAvailable.Value;
        if (update.GpuModel is not null) s.GpuModel = update.GpuModel;
        if (update.GpuEndpoint is not null) s.GpuEndpoint = update.GpuEndpoint;
        if (update.SimulatedLatencyMs.HasValue) s.SimulatedLatencyMs = update.SimulatedLatencyMs.Value;
        if (update.StreamDelayMs.HasValue) s.StreamDelayMs = update.StreamDelayMs.Value;
        return Results.Ok(new { message = "Mock scenario updated" });
    });

    mockGroup.MapPost("/scenario/reset", (MockScenario s) =>
    {
        s.ResetToDefaults();
        return Results.Ok(new { message = "Mock scenario reset to defaults" });
    });
}

app.Run();
