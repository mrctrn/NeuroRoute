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
builder.Services.AddSingleton<IConfigurationRoot>(sp =>
    (IConfigurationRoot)sp.GetRequiredService<IConfiguration>());

builder.Services.AddSingleton<ITokenizer, ApproximateTokenizer>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<NpuPlanner>();
builder.Services.AddSingleton<Router>();
builder.Services.AddSingleton<HealthService>();
builder.Services.AddSingleton<MetricsService>();
builder.Services.AddSingleton<RuntimeSettings>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var section = config.GetSection("NeuroRoute");
    return new RuntimeSettings
    {
        PassthroughMode = section.GetValue<bool>("PassthroughMode"),
        GpuFallbackToNpu = section.GetValue<bool?>("GpuFallbackToNpu") ?? true
    };
});

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
    var flmHost = neuroRouteSection["NpuFlmHost"] ?? "0.0.0.0";
    var flmPort = neuroRouteSection.GetValue<int?>("NpuFlmPort") ?? 52625;
    // Client always connects via loopback regardless of FLM's --host binding
    var flmEndpoint = neuroRouteSection["NpuFlmEndpoint"] ?? $"http://127.0.0.1:{flmPort}";
    builder.Services.AddHttpClient<FlmClient>(client =>
    {
        client.BaseAddress = new Uri(flmEndpoint);
        client.Timeout = TimeSpan.FromMinutes(5);
    });
    builder.Services.AddSingleton(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var options = sp.GetRequiredService<IOptions<NeuroRouteOptions>>().Value;
        var modelTag = config.GetSection("NeuroRoute")["NpuFlmModelTag"]
                       ?? "gemma4-it:e4b";
        var flmClient = sp.GetRequiredService<FlmClient>();
        var logger = sp.GetRequiredService<ILogger<FlmProcessManager>>();
        var sideloadDir = Path.Combine(AppContext.BaseDirectory, "flm");
        return new FlmProcessManager(
            modelTag,
            options.NpuFlmHost,
            options.NpuFlmPort,
            options.NpuFlmCtxLen,
            options.NpuFlmPmode,
            flmClient,
            logger,
            Directory.Exists(sideloadDir) ? sideloadDir : null
        );
    });
    builder.Services.AddSingleton<FlmBackend>();

    // FLM CLI service for one-off commands (list, pull, remove)
#pragma warning disable CS8634, CS8621
    builder.Services.AddSingleton<FlmCliService?>(sp =>
    {
        var sideloadDir = Path.Combine(AppContext.BaseDirectory, "flm");
        var flmPath = FindFlmExecutable(sideloadDir);
        if (flmPath is null)
        {
            var log = sp.GetRequiredService<ILogger<FlmCliService>>();
            log.LogWarning("flm.exe not found, FlmCliService disabled");
            return null;
        }
        var flmLogger = sp.GetRequiredService<ILogger<FlmCliService>>();
        return new FlmCliService(flmPath, flmLogger);
    });
#pragma warning restore CS8634, CS8621

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

    // GPU HTTP client (registered as IGpuClient interface)
    builder.Services.AddHttpClient<IGpuClient, GpuClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<NeuroRouteOptions>>().Value;
        client.BaseAddress = new Uri(options.GpuEndpoint);
        client.Timeout = TimeSpan.FromSeconds(options.GpuTimeoutSeconds);
    });
}

// NpuModel uses the selected INpuBackend
builder.Services.AddSingleton<NpuModel>();

// Worker auto-starts FLM backend when NpuBackend = flm
builder.Services.AddHostedService<NeuroRoute.Service.Worker>();

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

static string? FindFlmExecutable(string? sideloadDir = null)
{
    if (sideloadDir is not null)
    {
        var sideload = Path.Combine(sideloadDir, "flm.exe");
        if (File.Exists(sideload)) return sideload;
    }
    var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
    foreach (var path in paths)
    {
        var full = Path.Combine(path.Trim(), "flm.exe");
        if (File.Exists(full)) return full;
    }
    return null;
}

app.Run();
