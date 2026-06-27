# Mock Backend & Playwright Testing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add programmable mock NPU/GPU backends to NeuroRoute.Service and create Playwright E2E tests for the Dashboard.

**Architecture:** Conditional DI registration of `MockNpuBackend : INpuBackend` and `MockGpuClient : IGpuClient` when `UseMockBackends=true`. A `MockScenario` singleton holds programmable state controlled via admin Minimal API endpoints. Playwright tests use process-level fixture to start Service + Dashboard, program mocks via HTTP, and verify Dashboard UI.

**Tech Stack:** .NET 10, C# 14, xUnit, Microsoft.Playwright, Minimal API

---

## File Structure

### Service project changes

| File | Change |
|------|--------|
| `Gpu/IGpuClient.cs` | **Create** — extracted interface |
| `Gpu/GpuClient.cs` | **Modify** — add `: IGpuClient` (no body changes) |
| `Testing/MockScenario.cs` | **Create** — programmable state singleton |
| `Testing/MockScenarioRequest.cs` | **Create** — DTO for partial updates |
| `Testing/MockNpuBackend.cs` | **Create** — `: INpuBackend` |
| `Testing/MockGpuClient.cs` | **Create** — `: IGpuClient` |
| `Services/HealthService.cs` | **Modify** — optional `MockScenario`, mock health paths |
| `Routing/Router.cs` | **Modify** — `IGpuClient` instead of `GpuClient` |
| `Models/NeuroRouteOptions.cs` | **Modify** — add `UseMockBackends` |
| `Program.cs` | **Modify** — conditional DI + mock minimal API |
| `appsettings.Development.json` | **Create** — `UseMockBackends: true` |

### Test project (new)

| File | Purpose |
|------|---------|
| `NeuroRoute.Tests.Integration/NeuroRoute.Tests.Integration.csproj` | xUnit + Playwright |
| `NeuroRoute.Tests.Integration/PlaywrightFixture.cs` | Process lifecycle + Playwright |
| `NeuroRoute.Tests.Integration/DashboardHealthTests.cs` | Health card states |
| `NeuroRoute.Tests.Integration/DashboardAdminTests.cs` | Admin button tests |
| `NeuroRoute.Tests.Integration/DashboardMetricsTests.cs` | Metrics display tests |

---

### Task 1: Extract IGpuClient interface

**Files:**
- Create: `NeuroRoute.Service/Gpu/IGpuClient.cs`
- Modify: `NeuroRoute.Service/Gpu/GpuClient.cs`

- [ ] **Step 1: Create IGpuClient.cs**

Write the interface matching `GpuClient`'s public API:

```csharp
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Gpu;

public interface IGpuClient
{
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatRequest request, CancellationToken ct = default);
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Update GpuClient class declaration**

```csharp
public sealed class GpuClient : IGpuClient
```

No other changes to GpuClient.cs.

- [ ] **Step 3: Build to verify**

Run: `dotnet build NeuroRoute.Service\NeuroRoute.Service.csproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 4: Commit**

```bash
git add NeuroRoute.Service/Gpu/IGpuClient.cs NeuroRoute.Service/Gpu/GpuClient.cs
git commit -m "feat: extract IGpuClient interface for mockability"
```

---

### Task 2: Update Router to use IGpuClient

**Files:**
- Modify: `NeuroRoute.Service/Routing/Router.cs`

- [ ] **Step 1: Change field and constructor parameter**

```csharp
    private readonly IGpuClient _gpuClient;

    public Router(
        NpuPlanner planner,
        PromptBuilder promptBuilder,
        NpuModel npuModel,
        IGpuClient gpuClient,
        MetricsService metrics,
        ILogger<Router> logger)
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build NeuroRoute.Service\NeuroRoute.Service.csproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 3: Commit**

```bash
git add NeuroRoute.Service/Routing/Router.cs
git commit -m "refactor: Router depends on IGpuClient interface"
```

---

### Task 3: Update existing tests to use IGpuClient

**Files:**
- Modify: `NeuroRoute.Tests/RouterTests.cs`

- [ ] **Step 1: Add cast to IGpuClient**

Change line 27-28 from `new GpuClient(...)` to use the `IGpuClient` type:

```csharp
        IGpuClient gpuClient = new GpuClient(
            httpClient, NullLogger<GpuClient>.Instance);
```

- [ ] **Step 2: Run tests to verify they still compile and pass**

Run: `dotnet test NeuroRoute.Tests\NeuroRoute.Tests.csproj`
Expected: 9/9 passing

- [ ] **Step 3: Commit**

```bash
git add NeuroRoute.Tests/RouterTests.cs
git commit -m "test: update RouterTests for IGpuClient interface"
```

---

### Task 4: Add UseMockBackends to NeuroRouteOptions

**Files:**
- Modify: `NeuroRoute.Service/Models/NeuroRouteOptions.cs`

- [ ] **Step 1: Add property**

```csharp
    public bool UseMockBackends { get; set; }
```

Place it after `GpuTimeoutSeconds`.

- [ ] **Step 2: Build**

Run: `dotnet build NeuroRoute.Service\NeuroRoute.Service.csproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 3: Commit**

```bash
git add NeuroRoute.Service/Models/NeuroRouteOptions.cs
git commit -m "feat: add UseMockBackends config flag"
```

---

### Task 5: Create MockScenario singleton

**Files:**
- Create: `NeuroRoute.Service/Testing/MockScenario.cs`
- Create: `NeuroRoute.Service/Testing/MockScenarioRequest.cs`

- [ ] **Step 1: Create Testing directory and MockScenario.cs**

```csharp
namespace NeuroRoute.Service.Testing;

public sealed class MockScenario
{
    public bool NpuAvailable { get; set; } = true;
    public string NpuBackend { get; set; } = "mock";
    public string NpuModel { get; set; } = "mock-npu-model-v1";

    public string TaskType { get; set; } = "simple_chat";
    public bool NeedsGpu { get; set; } = false;
    public string RoutingCase { get; set; } = "C";

    public string NpuResponseText { get; set; } = "Hello from mock NPU!";

    public string GpuResponseText { get; set; } = "Complex reasoning from mock GPU!";
    public bool GpuAvailable { get; set; } = true;
    public string GpuModel { get; set; } = "mock-gpu-model-v1";
    public string GpuEndpoint { get; set; } = "http://mock-gpu:8080";

    public int SimulatedLatencyMs { get; set; } = 50;
    public int StreamDelayMs { get; set; } = 10;

    public void ResetToDefaults()
    {
        NpuAvailable = true;
        NpuBackend = "mock";
        NpuModel = "mock-npu-model-v1";
        TaskType = "simple_chat";
        NeedsGpu = false;
        RoutingCase = "C";
        NpuResponseText = "Hello from mock NPU!";
        GpuResponseText = "Complex reasoning from mock GPU!";
        GpuAvailable = true;
        GpuModel = "mock-gpu-model-v1";
        GpuEndpoint = "http://mock-gpu:8080";
        SimulatedLatencyMs = 50;
        StreamDelayMs = 10;
    }
}
```

- [ ] **Step 2: Create MockScenarioRequest.cs (DTO for partial updates)**

```csharp
namespace NeuroRoute.Service.Testing;

public sealed class MockScenarioRequest
{
    public bool? NpuAvailable { get; init; }
    public string? NpuBackend { get; init; }
    public string? NpuModel { get; init; }
    public string? TaskType { get; init; }
    public bool? NeedsGpu { get; init; }
    public string? RoutingCase { get; init; }
    public string? NpuResponseText { get; init; }
    public string? GpuResponseText { get; init; }
    public bool? GpuAvailable { get; init; }
    public string? GpuModel { get; init; }
    public string? GpuEndpoint { get; init; }
    public int? SimulatedLatencyMs { get; init; }
    public int? StreamDelayMs { get; init; }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build NeuroRoute.Service\NeuroRoute.Service.csproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 4: Commit**

```bash
git add NeuroRoute.Service/Testing/MockScenario.cs NeuroRoute.Service/Testing/MockScenarioRequest.cs
git commit -m "feat: add MockScenario programmable state singleton"
```

---

### Task 6: Create MockNpuBackend

**Files:**
- Create: `NeuroRoute.Service/Testing/MockNpuBackend.cs`

- [ ] **Step 1: Implement INpuBackend**

```csharp
using System.Runtime.CompilerServices;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;

namespace NeuroRoute.Service.Testing;

public sealed class MockNpuBackend : INpuBackend
{
    private readonly MockScenario _scenario;

    public MockNpuBackend(MockScenario scenario)
    {
        _scenario = scenario;
    }

    public Task<NpuPlan> ClassifyAsync(string prompt, CancellationToken ct = default)
    {
        return Task.FromResult(new NpuPlan
        {
            TaskType = _scenario.TaskType,
            NeedsGpu = _scenario.NeedsGpu,
            RoutingCase = _scenario.RoutingCase,
            EstimatedTokens = prompt.Length / 4,
            Confidence = 0.95f
        });
    }

    public async Task<ChatResponse> GenerateAsync(string prompt, ChatRequest request, CancellationToken ct = default)
    {
        if (_scenario.SimulatedLatencyMs > 0)
            await Task.Delay(_scenario.SimulatedLatencyMs, ct);

        return new ChatResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage { Role = "assistant", Content = _scenario.NpuResponseText },
                    FinishReason = "stop"
                }
            ],
            Usage = new UsageInfo
            {
                PromptTokens = prompt.Length / 4,
                CompletionTokens = 10,
                TotalTokens = prompt.Length / 4 + 10
            }
        };
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        string prompt,
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        yield return new ChatCompletionChunk
        {
            Id = id,
            Created = created,
            Model = request.Model,
            Choices =
            [
                new ChunkChoice
                {
                    Index = 0,
                    Delta = new ChatMessage { Role = "assistant" }
                }
            ]
        };

        var words = _scenario.NpuResponseText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();

            yield return new ChatCompletionChunk
            {
                Id = id,
                Created = created,
                Model = request.Model,
                Choices =
                [
                    new ChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatMessage { Content = word + " " }
                    }
                ]
            };

            await Task.Delay(_scenario.StreamDelayMs, ct);
        }

        yield return new ChatCompletionChunk
        {
            Id = id,
            Created = created,
            Model = request.Model,
            Choices =
            [
                new ChunkChoice
                {
                    Index = 0,
                    Delta = new ChatMessage(),
                    FinishReason = "stop"
                }
            ]
        };
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build NeuroRoute.Service\NeuroRoute.Service.csproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 3: Commit**

```bash
git add NeuroRoute.Service/Testing/MockNpuBackend.cs
git commit -m "feat: add MockNpuBackend for programmable NPU responses"
```

---

### Task 7: Create MockGpuClient

**Files:**
- Create: `NeuroRoute.Service/Testing/MockGpuClient.cs`

- [ ] **Step 1: Implement IGpuClient**

```csharp
using System.Runtime.CompilerServices;
using NeuroRoute.Service.Gpu;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Testing;

public sealed class MockGpuClient : IGpuClient
{
    private readonly MockScenario _scenario;

    public MockGpuClient(MockScenario scenario)
    {
        _scenario = scenario;
    }

    public Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
    {
        // Simulate latency
        if (_scenario.SimulatedLatencyMs > 0)
            Task.Delay(_scenario.SimulatedLatencyMs, ct).Wait(ct);

        return Task.FromResult(new ChatResponse
        {
            Choices =
            [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage { Role = "assistant", Content = _scenario.GpuResponseText },
                    FinishReason = "stop"
                }
            ]
        });
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        yield return new ChatCompletionChunk
        {
            Id = id,
            Created = created,
            Model = request.Model,
            Choices =
            [
                new ChunkChoice
                {
                    Index = 0,
                    Delta = new ChatMessage { Role = "assistant" }
                }
            ]
        };

        var words = _scenario.GpuResponseText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();

            yield return new ChatCompletionChunk
            {
                Id = id,
                Created = created,
                Model = request.Model,
                Choices =
                [
                    new ChunkChoice
                    {
                        Index = 0,
                        Delta = new ChatMessage { Content = word + " " }
                    }
                ]
            };

            await Task.Delay(_scenario.StreamDelayMs, ct);
        }

        yield return new ChatCompletionChunk
        {
            Id = id,
            Created = created,
            Model = request.Model,
            Choices =
            [
                new ChunkChoice
                {
                    Index = 0,
                    Delta = new ChatMessage(),
                    FinishReason = "stop"
                }
            ]
        };
    }

    public Task<bool> PingAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_scenario.GpuAvailable);
    }

    public Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(
            _scenario.GpuAvailable
                ? new List<string> { _scenario.GpuModel }
                : new List<string>());
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build NeuroRoute.Service\NeuroRoute.Service.csproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 3: Commit**

```bash
git add NeuroRoute.Service/Testing/MockGpuClient.cs
git commit -m "feat: add MockGpuClient for programmable GPU responses"
```

---

### Task 8: Update HealthService for mock mode

**Files:**
- Modify: `NeuroRoute.Service/Services/HealthService.cs`

- [ ] **Step 1: Add MockScenario using + optional constructor parameter**

Add import:
```csharp
using NeuroRoute.Service.Testing;
```

Add field:
```csharp
    private readonly GpuClient _gpuClient;
    // ADD:
    private readonly MockScenario? _mockScenario;
```

Update constructor — add optional `MockScenario` parameter at the end:
```csharp
    public HealthService(
        OnnxSessionFactory onnxSessionFactory,
        GpuClient gpuClient,
        IOptions<NeuroRouteOptions> options,
        ILogger<HealthService> logger,
        FlmProcessManager? flmProcessManager = null,
        MockScenario? mockScenario = null)
    {
        _onnxSessionFactory = onnxSessionFactory;
        _gpuClient = gpuClient;
        _flmProcessManager = flmProcessManager;
        _options = options;
        _logger = logger;
        _mockScenario = mockScenario;
    }
```

- [ ] **Step 2: Update GetNpuHealth to check MockScenario first**

At the start of `GetNpuHealth()`, add:
```csharp
    if (_mockScenario is not null)
    {
        return new ComponentHealth
        {
            Status = _mockScenario.NpuAvailable ? "healthy" : "unhealthy",
            Message = _mockScenario.NpuAvailable ? "Mock NPU backend running" : "Mock NPU backend disabled",
            Backend = "mock",
            Model = _mockScenario.NpuModel,
            ModelLoaded = _mockScenario.NpuAvailable
        };
    }
```

- [ ] **Step 3: Update GetGpuHealthAsync to check MockScenario first**

At the start of `GetGpuHealthAsync()`, add:
```csharp
    if (_mockScenario is not null)
    {
        _lastGpuHealth = new ComponentHealth
        {
            Status = _mockScenario.GpuAvailable ? "healthy" : "unhealthy",
            Message = _mockScenario.GpuAvailable ? "Mock GPU available" : "Mock GPU disabled",
            Endpoint = _mockScenario.GpuEndpoint,
            Model = _mockScenario.GpuModel,
            ModelLoaded = _mockScenario.GpuAvailable
        };
        _lastGpuCheck = DateTime.UtcNow;
        return _lastGpuHealth;
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build NeuroRoute.Service\NeuroRoute.Service.csproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 5: Commit**

```bash
git add NeuroRoute.Service/Services/HealthService.cs
git commit -m "feat: HealthService supports optional MockScenario for mock health"
```

---

### Task 9: Update Program.cs with conditional DI + mock endpoints

**Files:**
- Modify: `NeuroRoute.Service/Program.cs`

- [ ] **Step 1: Add using for Testing namespace**

```csharp
using NeuroRoute.Service.Testing;
```

- [ ] **Step 2: After existing DI registrations, add conditional mock block**

Insert after `builder.Services.AddSingleton<MetricsService>();`:

```csharp
// Mock backends (dev/test mode — no hardware required)
var neuroRouteSection = builder.Configuration.GetSection(NeuroRouteOptions.SectionName);
var useMockBackends = neuroRouteSection.GetValue<bool>("UseMockBackends");

if (useMockBackends)
{
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

    // GPU HTTP client
    builder.Services.AddHttpClient<GpuClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<NeuroRouteOptions>>().Value;
        client.BaseAddress = new Uri(options.GpuEndpoint);
        client.Timeout = TimeSpan.FromSeconds(options.GpuTimeoutSeconds);
    });
}
```

- [ ] **Step 3: Remove old ONNX/FLM/GPU registrations**

Delete the old code block that was between `builder.Services.AddSingleton<MetricsService>();` and `var app = builder.Build();` — the old ONNX session factory, FLM process manager, INpuBackend selection, NpuModel, and AddHttpClient<GpuClient> calls.

Keep NpuModel registration outside the if/else:
```csharp
builder.Services.AddSingleton<NpuModel>();
```

- [ ] **Step 4: Add conditional mock minimal API endpoints after MapControllers**

Before `app.Run()`, add:
```csharp
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
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build NeuroRoute.Service\NeuroRoute.Service.csproj`
Expected: 0 errors, 0 warnings

- [ ] **Step 6: Commit**

```bash
git add NeuroRoute.Service/Program.cs
git commit -m "feat: conditional DI registration for mock backends + mock admin endpoints"
```

---

### Task 10: Create appsettings.Development.json

**Files:**
- Create: `NeuroRoute.Service/appsettings.Development.json`

- [ ] **Step 1: Create file**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "NeuroRoute": "Debug"
    }
  },
  "NeuroRoute": {
    "UseMockBackends": true
  }
}
```

- [ ] **Step 2: Commit**

```bash
git add NeuroRoute.Service/appsettings.Development.json
git commit -m "chore: add Development config with UseMockBackends=true"
```

---

### Task 11: Build verification

Run: `dotnet build`
Expected: All 4 projects build with 0 errors, 0 warnings

Run: `dotnet test NeuroRoute.Tests\NeuroRoute.Tests.csproj`
Expected: 9/9 passing

---

### Task 12: Create NeuroRoute.Tests.Integration project

**Files:**
- Create: `NeuroRoute.Tests.Integration/NeuroRoute.Tests.Integration.csproj`
- Create: `NeuroRoute.Tests.Integration/PlaywrightFixture.cs`
- Create: `NeuroRoute.Tests.Integration/DashboardHealthTests.cs`
- Create: `NeuroRoute.Tests.Integration/DashboardAdminTests.cs`
- Create: `NeuroRoute.Tests.Integration/DashboardMetricsTests.cs`

- [ ] **Step 1: Create directory and csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Microsoft.Playwright" Version="1.52.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create PlaywrightFixture.cs**

```csharp
using System.Diagnostics;

namespace NeuroRoute.Tests.Integration;

public sealed class PlaywrightFixture : IAsyncLifetime
{
    private Process? _serviceProcess;
    private Process? _dashboardProcess;
    private const string ServiceUrl = "http://localhost:5000";
    private const string DashboardUrl = "http://localhost:5001";

    public async Task InitializeAsync()
    {
        // Kill any lingering processes from previous test runs
        await KillProcessOnPort(5000);
        await KillProcessOnPort(5001);

        // Start service process
        var serviceRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "NeuroRoute.Service"));

        _serviceProcess = StartDotNetRun(serviceRoot,
            ("NeuroRoute__UseMockBackends", "true"),
            ("Kestrel__Endpoints__Http__Url", ServiceUrl));

        await WaitForHealth(ServiceUrl);

        // Start dashboard process
        var dashboardRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "NeuroRoute.Dashboard"));

        _dashboardProcess = StartDotNetRun(dashboardRoot,
            ("Kestrel__Endpoints__Http__Url", DashboardUrl));

        await WaitForHttp(DashboardUrl);
    }

    public async Task DisposeAsync()
    {
        if (_serviceProcess is not null && !_serviceProcess.HasExited)
            _serviceProcess.Kill();
        if (_dashboardProcess is not null && !_dashboardProcess.HasExited)
            _dashboardProcess.Kill();

        await KillProcessOnPort(5000);
        await KillProcessOnPort(5001);
    }

    public async Task ProgramScenarioAsync(object body)
    {
        using var client = new HttpClient { BaseAddress = new Uri(ServiceUrl) };
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/v1/admin/mock/scenario", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResetScenarioAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(ServiceUrl) };
        var response = await client.PostAsync("/v1/admin/mock/scenario/reset", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task MakeChatRequestAsync(object requestBody)
    {
        using var client = new HttpClient { BaseAddress = new Uri(ServiceUrl) };
        var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task NavigateToDashboardAsync(IPage page)
    {
        await page.GotoAsync(DashboardUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static Process StartDotNetRun(string projectDir, params (string key, string value)[] envVars)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectDir}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var (key, value) in envVars)
            psi.EnvironmentVariables[key] = value;

        var process = new Process { StartInfo = psi };
        process.Start();
        return process;
    }

    private static async Task WaitForHealth(string baseUrl, int timeoutMs = 30_000)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var response = await client.GetAsync($"{baseUrl}/v1/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Service not ready yet
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Service at {baseUrl} did not become healthy within {timeoutMs}ms");
    }

    private static async Task WaitForHttp(string url, int timeoutMs = 30_000)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"HTTP endpoint at {url} did not respond within {timeoutMs}ms");
    }

    private static async Task KillProcessOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"exec --runtimeconfig \"{Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "NeuroRoute.Service", "bin", "Debug", "net10.0", "NeuroRoute.Service.runtimeconfig.json"))}\" \"{Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "net10.0", "NeuroRoute.Tests.Integration.dll"))}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        catch
        {
            // Best effort
        }

        await Task.CompletedTask;
    }

    public string GetDashboardUrl() => DashboardUrl;
}
```

- [ ] **Step 3: Create DashboardHealthTests.cs**

```csharp
using Microsoft.Playwright;

namespace NeuroRoute.Tests.Integration;

[CollectionDefinition("Playwright")]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightFixture> { }

[Collection("Playwright")]
public sealed class DashboardHealthTests : IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public DashboardHealthTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var playwright = await Playwright.CreateAsync();
        _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _page = await _browser.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
    }

    [Fact]
    public async Task BothHealthy_ShowsGreen()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.NavigateToDashboardAsync(_page);

        // Overall status badge should show "healthy" with green class
        var statusBadge = await _page.TextContentAsync(".badge");
        Assert.Contains("healthy", statusBadge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NpuDown_ShowsDegradedOverall()
    {
        await _fixture.ProgramScenarioAsync(new { npuAvailable = false });
        await _fixture.NavigateToDashboardAsync(_page);

        var statusBadge = await _page.TextContentAsync(".badge");
        Assert.Contains("degraded", statusBadge, StringComparison.OrdinalIgnoreCase);
        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task GpuDown_ShowsDegradedOverall()
    {
        await _fixture.ProgramScenarioAsync(new { gpuAvailable = false });
        await _fixture.NavigateToDashboardAsync(_page);

        var statusBadge = await _page.TextContentAsync(".badge");
        Assert.Contains("degraded", statusBadge, StringComparison.OrdinalIgnoreCase);
        await _fixture.ResetScenarioAsync();
    }

    [Fact]
    public async Task BothDown_ShowsUnhealthy()
    {
        await _fixture.ProgramScenarioAsync(new { npuAvailable = false, gpuAvailable = false });
        await _fixture.NavigateToDashboardAsync(_page);

        var statusBadge = await _page.TextContentAsync(".badge");
        Assert.Contains("unhealthy", statusBadge, StringComparison.OrdinalIgnoreCase);
        await _fixture.ResetScenarioAsync();
    }
}
```

- [ ] **Step 4: Create DashboardAdminTests.cs**

```csharp
using Microsoft.Playwright;

namespace NeuroRoute.Tests.Integration;

[Collection("Playwright")]
public sealed class DashboardAdminTests : IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public DashboardAdminTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var playwright = await Playwright.CreateAsync();
        _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _page = await _browser.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
    }

    [Fact]
    public async Task RestartNpuButton_ReturnsSuccess()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.NavigateToDashboardAsync(_page);

        var restartButton = _page.GetByRole(AriaRole.Button, new() { Name = "Restart NPU Backend" });
        await Assertions.Expect(restartButton).ToBeEnabledAsync();

        await restartButton.ClickAsync();

        // Wait for button to re-enable (success response)
        await Task.Delay(2000);
        await Assertions.Expect(restartButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task ReloadConfigButton_ReturnsSuccess()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.NavigateToDashboardAsync(_page);

        var reloadButton = _page.GetByRole(AriaRole.Button, new() { Name = "Reload Configuration" });
        await Assertions.Expect(reloadButton).ToBeEnabledAsync();

        await reloadButton.ClickAsync();

        await Task.Delay(2000);
        await Assertions.Expect(reloadButton).ToBeEnabledAsync();
    }
}
```

- [ ] **Step 5: Create DashboardMetricsTests.cs**

```csharp
using Microsoft.Playwright;

namespace NeuroRoute.Tests.Integration;

[Collection("Playwright")]
public sealed class DashboardMetricsTests : IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    public DashboardMetricsTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var playwright = await Playwright.CreateAsync();
        _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _page = await _browser.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
    }

    [Fact]
    public async Task Metrics_DisplayAfterNpuRequest()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.ProgramScenarioAsync(new { needsGpu = false });
        await _fixture.MakeChatRequestAsync(new
        {
            model = "neuro-route",
            messages = new[] { new { role = "user", content = "Hello" } },
            max_tokens = 32
        });

        await _fixture.NavigateToDashboardAsync(_page);

        // Wait for the metrics to load (5s auto-refresh)
        await Task.Delay(2000);
        var pageText = await _page.TextContentAsync("body");

        Assert.Contains("Total", pageText);
    }

    [Fact]
    public async Task NpuHandled_ShowsCount()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.ProgramScenarioAsync(new { needsGpu = false });
        await _fixture.MakeChatRequestAsync(new
        {
            model = "neuro-route",
            messages = new[] { new { role = "user", content = "Hello" } },
            max_tokens = 32
        });

        await _fixture.NavigateToDashboardAsync(_page);
        await Task.Delay(2000);
        var pageText = await _page.TextContentAsync("body");

        Assert.Contains("NPU", pageText);
    }

    [Fact]
    public async Task GpuEscalated_ShowsCount()
    {
        await _fixture.ResetScenarioAsync();
        await _fixture.ProgramScenarioAsync(new { needsGpu = true, gpuAvailable = true });
        await _fixture.MakeChatRequestAsync(new
        {
            model = "neuro-route",
            messages = new[] { new { role = "user", content = "Complex task" } },
            max_tokens = 32
        });

        await _fixture.NavigateToDashboardAsync(_page);
        await Task.Delay(2000);
        var pageText = await _page.TextContentAsync("body");

        Assert.Contains("GPU", pageText);
    }
}
```

---

### Task 13: Add solution reference for integration test project

- [ ] **Step 1: Add project to solution**

```pwsh
dotnet sln add NeuroRoute.Tests.Integration\NeuroRoute.Tests.Integration.csproj
```

- [ ] **Step 2: Build solution**

Run: `dotnet build`
Expected: 5 projects build with 0 errors, 0 warnings

- [ ] **Step 3: Commit**

```bash
git add NeuroRoute.Tests.Integration/ NeuroRoute.sln
git commit -m "test: add NeuroRoute.Tests.Integration project with Playwright E2E tests"
```

---

### Task 14: Final verification

- [ ] **Step 1: Run unit tests**

Run: `dotnet test NeuroRoute.Tests\NeuroRoute.Tests.csproj`
Expected: 9/9 passing

- [ ] **Step 2: Run integration tests (requires Playwright browsers installed)**

Run: `dotnet test NeuroRoute.Tests.Integration\NeuroRoute.Tests.Integration.csproj`
Expected: Tests pass or are skipped if Playwright not installed

- [ ] **Step 3: Verify service starts in mock mode**

Run: `$env:NeuroRoute__UseMockBackends = "true"; dotnet run --project NeuroRoute.Service\NeuroRoute.Service.csproj`
In another terminal: `curl http://localhost:5000/v1/health`
Expected: Health shows mock backend status

- [ ] **Step 4: Verify mock admin endpoints work**

Run: `Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario -Method Post -Body '{"needsGpu":true}' -ContentType "application/json"`
Expected: `{"message": "Mock scenario updated"}`

- [ ] **Step 5: Final commit of any outstanding changes**

```bash
git add -A
git commit -m "chore: finalize mock backend and integration test implementation"
```
