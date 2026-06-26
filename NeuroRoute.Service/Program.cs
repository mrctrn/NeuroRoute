using NeuroRoute.Service.Gpu;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using NeuroRoute.Service.Routing;
using NeuroRoute.Service.Services;
using Microsoft.AspNetCore.Builder;
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

// NpuModel uses the selected INpuBackend
builder.Services.AddSingleton<NpuModel>();

builder.Services.AddHttpClient<GpuClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<NeuroRouteOptions>>().Value;
    client.BaseAddress = new Uri(options.GpuEndpoint);
    client.Timeout = TimeSpan.FromSeconds(options.GpuTimeoutSeconds);
});

var app = builder.Build();
app.UseMiddleware<NeuroRoute.Service.Diagnostics.RequestLoggingMiddleware>();
app.MapControllers();
app.Run();
