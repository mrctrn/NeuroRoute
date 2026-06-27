# Mock Backend & Playwright Integration Testing

**Date:** 2026-06-26
**Status:** Draft
**Approach:** A — Mock backends + Admin mock API

## 1. Goal

Run NeuroRoute Service standalone without real NPU/GPU hardware, with programmable fake backends controlled via HTTP, enabling both:

- **Dev mode** — manual QA, Dashboard demos, UI development
- **Automated tests** — Playwright tests exercising the full stack (Service API + Dashboard UI) in CI

## 2. Architecture

```
config: UseMockBackends=true
         │
         ▼
┌──────────────────────────────────────────────┐
│           NeuroRoute.Service                 │
│                                              │
│  Program.cs ──→ conditional DI registration  │
│                    │            │            │
│                    ▼            ▼            │
│  MockNpuBackend : INpuBackend  MockGpuClient │
│  MockScenario  (singleton)     : IGpuClient  │
│                    │                         │
│                    ▼                         │
│  AdminController (/v1/admin/mock/scenario)   │
│                                              │
└──────────────────┬───────────────────────────┘
                   │
    POST /v1/admin/mock/scenario   (program fakes)
    GET  /v1/admin/mock/scenario   (inspect state)
    POST /v1/admin/mock/scenario/reset (defaults)
                   │
                   ▼
┌──────────────────────────────────────────────┐
│        Playwright Test (or curl/rest)        │
│  Setup → navigate Dashboard → assert → tear  │
└──────────────────────────────────────────────┘
```

## 3. Files

### Modified — NeuroRoute.Service

| File | Change |
|------|--------|
| `Gpu/IGpuClient.cs` | **New** — extracted interface |
| `Gpu/GpuClient.cs` | Implements `IGpuClient` (no other changes) |
| `Testing/MockScenario.cs` | **New** — programmable state singleton |
| `Testing/MockNpuBackend.cs` | **New** — `INpuBackend` implementation |
| `Testing/MockGpuClient.cs` | **New** — `IGpuClient` implementation |
| `Controllers/AdminController.cs` | Add mock scenario endpoints (guarded by `UseMockBackends`) |
| `Services/HealthService.cs` | Accept optional `MockScenario`; return mock health when present |
| `Routing/Router.cs` | Depend on `IGpuClient` instead of `GpuClient` |
| `Program.cs` | Conditional registration of mock backends |
| `Models/NeuroRouteOptions.cs` | Add `UseMockBackends` property |
| `appsettings.Development.json` | **New** — `UseMockBackends: true` |

### New — NeuroRoute.Tests.Integration

| File | Purpose |
|------|---------|
| `NeuroRoute.Tests.Integration.csproj` | xUnit + Microsoft.Playwright |
| `PlaywrightFixture.cs` | `IAsyncLifetime` — starts Service + Dashboard processes, manages Playwright browser |
| `Scenarios/DashboardHealthTests.cs` | Health card states (green/yellow/red) |
| `Scenarios/DashboardAdminTests.cs` | Admin button functionality |
| `Scenarios/DashboardMetricsTests.cs` | Metrics display and updates |
| `appsettings.Test.json` | Test config overrides |

## 4. MockScenario State

```csharp
public sealed class MockScenario
{
    // NPU
    public bool NpuAvailable { get; set; } = true;
    public string NpuBackend { get; set; } = "mock";
    public string NpuModel { get; set; } = "mock-npu-model-v1";

    // Classification result
    public string TaskType { get; set; } = "simple_chat";
    public bool NeedsGpu { get; set; } = false;
    public string RoutingCase { get; set; } = "C";

    // NPU generation
    public string NpuResponseText { get; set; } = "Hello from mock NPU!";

    // GPU
    public string GpuResponseText { get; set; } = "Complex reasoning from mock GPU!";
    public bool GpuAvailable { get; set; } = true;
    public string GpuModel { get; set; } = "mock-gpu-model-v1";
    public string GpuEndpoint { get; set; } = "http://mock-gpu:8080";

    // Simulation
    public int SimulatedLatencyMs { get; set; } = 50;
    public int StreamDelayMs { get; set; } = 10;
}
```

## 5. Mock Implementations

### MockNpuBackend : INpuBackend

- `ClassifyAsync` — reads `MockScenario.TaskType`, `NeedsGpu`, `RoutingCase` into `NpuPlan`
- `GenerateAsync` — returns `ChatResponse` with `NpuResponseText` after `SimulatedLatencyMs` delay
- `StreamAsync` — yields tokens from `NpuResponseText` with `StreamDelayMs` between each

### MockGpuClient : IGpuClient

- `SendAsync` — returns `ChatResponse` with `GpuResponseText` after `SimulatedLatencyMs` delay
- `StreamAsync` — yields tokens from `GpuResponseText` with `StreamDelayMs` between each
- `PingAsync` — returns `GpuAvailable`
- `GetAvailableModelsAsync` — returns `[GpuModel]` if `GpuAvailable`, empty otherwise

### IGpuClient (extracted interface)

```csharp
public interface IGpuClient
{
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatRequest request, CancellationToken ct = default);
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default);
}
```

## 6. HealthService in Mock Mode

`HealthService` accepts an optional `MockScenario` parameter (default `null`). When `MockScenario` is non-null:

- **GetNpuHealth** — returns `{ Status: "healthy"/"unhealthy", Backend: "mock", Model: MockScenario.NpuModel, ModelLoaded: NpuAvailable }`
- **GetGpuHealthAsync** — returns `{ Status: GpuAvailable ? "healthy" : "unhealthy", Model: GpuModel, ModelLoaded: GpuAvailable }`

This bypasses `OnnxSessionFactory`/`FlmProcessManager`/`GpuClient` for health checks when in mock mode.

## 7. Admin Mock Endpoints

Registered conditionally in `Program.cs` using Minimal API:

```csharp
if (options.UseMockBackends)
{
    var mockGroup = app.MapGroup("/v1/admin/mock");

    mockGroup.MapGet("/scenario", (MockScenario s) => s);
    mockGroup.MapPost("/scenario", (MockScenario s, MockScenarioRequest update) => {
        // Partial update: only set non-null properties
        if (update.NeedsGpu.HasValue) s.NeedsGpu = update.NeedsGpu.Value;
        if (update.NpuAvailable.HasValue) s.NpuAvailable = update.NpuAvailable.Value;
        if (update.TaskType is not null) s.TaskType = update.TaskType;
        // ... etc
    });
    mockGroup.MapPost("/scenario/reset", (MockScenario s) => {
        s.ResetToDefaults();
    });
}
```

These endpoints only exist when `UseMockBackends=true`. No security risk in production.

## 8. Dev Mode Usage

```pwsh
# Run with mock backends
$env:NeuroRoute__UseMockBackends = "true"
dotnet run --project NeuroRoute.Service

# In another terminal, program the fakes
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario `
  -Method Post `
  -Body '{"needsGpu":true,"gpuAvailable":true}' `
  -ContentType "application/json"

# Open Dashboard and verify it shows GPU card
dotnet run --project NeuroRoute.Dashboard
```

Or use `appsettings.Development.json` to set `UseMockBackends: true` permanently for dev.

## 9. Playwright Integration Tests

### Test Project

`NeuroRoute.Tests.Integration` — xUnit + `Microsoft.Playwright` NuGet. Target `net10.0`.

### Test Fixture

```csharp
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private Process? _serviceProcess;
    private Process? _dashboardProcess;
    public IBrowser Browser { get; private set; } = null!;
    public string DashboardUrl { get; private set; } = "http://localhost:5001";

    public async Task InitializeAsync()
    {
        // Start Service with mock backends on :5000
        _serviceProcess = StartDotNetRun(
            "NeuroRoute.Service",
            ("NeuroRoute:UseMockBackends", "true"),
            ("Kestrel:Endpoints:Http:Url", "http://localhost:5000"));
        await WaitForHealth("http://localhost:5000/v1/health");

        // Start Dashboard on :5001 (API client points at :5000)
        _dashboardProcess = StartDotNetRun(
            "NeuroRoute.Dashboard",
            ("Kestrel:Endpoints:Http:Url", "http://localhost:5001"));
        await WaitForHttp("http://localhost:5001");

        // Launch Playwright
        var playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync();
    }

    public async Task DisposeAsync()
    {
        Browser?.CloseAsync();
        if (_serviceProcess is not null && !_serviceProcess.HasExited)
            _serviceProcess.Kill();
        if (_dashboardProcess is not null && !_dashboardProcess.HasExited)
            _dashboardProcess.Kill();
    }
}
```

### Test Scenarios

#### DashboardHealthTests.cs

| Test | Setup | Assertion |
|------|-------|-----------|
| `BothHealthy_ShowsGreen` | defaults (NpuAvailable=true, GpuAvailable=true) | NPU card has badge "healthy" (green), GPU card has badge "healthy" (green), overall status "healthy" (green) |
| `NpuDown_ShowsRed` | `POST /mock/scenario {"npuAvailable":false}` | NPU card badge "unhealthy" (red), overall "degraded" |
| `GpuDown_ShowsRed` | `POST /mock/scenario {"gpuAvailable":false}` | GPU card badge "unhealthy" (red), overall "degraded" |
| `BothDown_ShowsUnhealthy` | `POST /mock/scenario {"npuAvailable":false,"gpuAvailable":false}` | Overall badge "unhealthy" (red) |
| `NpuHealthyGpuDown_Degraded` | `POST /mock/scenario {"gpuAvailable":false}` | Overall badge "degraded" (yellow) |

#### DashboardAdminTests.cs

| Test | Setup | Assertion |
|------|-------|-----------|
| `RestartNpu_ReturnsSuccess` | Click "Restart NPU Backend" | Button re-enables, no error shown |
| `ReloadConfig_ReturnsSuccess` | Click "Reload Configuration" | Button re-enables, no error shown |

#### DashboardMetricsTests.cs

| Test | Setup | Assertion |
|------|-------|-----------|
| `Metrics_DisplayAfterRequest` | `POST /v1/chat/completions` then reload Dashboard | TotalRequests > 0 |
| `NpuHandled_CountsCorrectly` | `POST /mock/scenario {"needsGpu":false}`, make chat request | NpuHandled incremented |
| `GpuEscalated_CountsCorrectly` | `POST /mock/scenario {"needsGpu":true}`, make chat request | GpuEscalated incremented |

## 10. Test Runner Integration

```pwsh
# Run integration tests
dotnet test NeuroRoute.Tests.Integration

# Run all tests
dotnet test
```

The integration tests depend on the Service and Dashboard projects being buildable. They are:
- **Slow** (~30-60s per run due to process startup)
- **Excluded** from `dotnet test` in CI by default (use `[Trait("Category", "Integration")]` + filter)
- **Require** Chromium installed Playwright browsers

## 11. Implementation Order

1. Extract `IGpuClient` interface, update `Router` and `HealthService` to use it
2. Add `UseMockBackends` to `NeuroRouteOptions`
3. Create `MockScenario` singleton
4. Create `MockNpuBackend : INpuBackend`
5. Create `MockGpuClient : IGpuClient`
6. Update `HealthService` to accept optional `MockScenario` for mock health checks
7. Add mock scenario admin endpoints (conditional Minimal API)
8. Update `Program.cs` with conditional DI registration
9. Create `appsettings.Development.json`
10. Create `NeuroRoute.Tests.Integration` project
11. Implement `PlaywrightFixture`
12. Write Dashboard health card tests
13. Write Dashboard admin button tests
14. Write Dashboard metrics tests
15. Verify `dotnet test` passes, `dotnet build` has 0 errors/warnings

## 12. Risks & Open Questions

- **Blazor Server SignalR:** Playwright needs to handle WebSocket-based SignalR connections used by Blazor Server's InteractiveServer rendering mode. The Dashboard's `Home.razor` uses `@rendermode InteractiveServer`, which relies on a SignalR circuit. Playwright should handle this transparently as the page loads in the browser, but test stability may be affected by connection timing.
- **Process management:** Child process startup is inherently racy. The `WaitForHealth` polling loop (retry every 500ms, timeout 30s) mitigates this.
- **Port conflicts:** If `:5000` or `:5001` are in use, tests fail. Could use random ports (`:0`) and read the actual port from process output.
