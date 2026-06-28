# FLM Lifecycle & Dashboard NPU Management — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reliable FLM NPU backend lifecycle with configurable host/port/ctxLen, admin API for model management, and Dashboard NPU info panel.

**Architecture:** Add `FlmCliService` for one-off FLM commands, new admin endpoints under `/v1/admin/npu`, expand Dashboard NPU card, update installer to validate and pull model at install time. All changes are backward-compatible with existing ONNX path.

**Tech Stack:** .NET 10, ASP.NET Core, MudBlazor, FastFlowLM CLI, xUnit

---

## File Structure

### New files:
- `NeuroRoute.Service/Npu/FlmCliService.cs` — spawns `flm.exe` for list/pull/remove
- `NeuroRoute.Service/Npu/FlmStatus.cs` — `FlmStatus` record
- `NeuroRoute.Service/Models/FlmModelEntry.cs` — `FlmModelEntry` record
- `NeuroRoute.Service/Models/NpuRequestModels.cs` — request DTOs for pull/load
- `NeuroRoute.Service/Controllers/NpuController.cs` — new admin endpoints

### Modified files:
- `NeuroRoute.Service/Npu/FlmProcessManager.cs` — add host/port/ctxLen/pmode, reset restart counter, expose FlmStatus
- `NeuroRoute.Service/Models/NeuroRouteOptions.cs` — add NpuFlmHost, NpuFlmPort, NpuFlmCtxLen, NpuFlmPmode
- `NeuroRoute.Service/Controllers/AdminController.cs` — add persistence helper
- `NeuroRoute.Service/Services/HealthService.cs` — read FlmStatus for FLM path
- `NeuroRoute.Service/Program.cs` — register FlmCliService, update DI
- `NeuroRoute.Dashboard/Services/NeuroRouteApiClient.cs` — add NPU admin API methods
- `NeuroRoute.Dashboard/Components/Pages/Dashboard.razor` — expand NPU card
- `install.ps1` — add flm validate + flm pull step, update Build-NeuroRouteConfig

---

### Task 1: Add config options

**Files:**
- Modify: `NeuroRoute.Service/Models/NeuroRouteOptions.cs`
- Modify: `install.ps1` (Build-NeuroRouteConfig)

- [ ] **Step 1: Add new properties to NeuroRouteOptions**

```csharp
public string NpuFlmHost { get; set; } = "127.0.0.1";
public int NpuFlmPort { get; set; } = 52625;
public int NpuFlmCtxLen { get; set; }
public string NpuFlmPmode { get; set; } = "performance";
```

- [ ] **Step 2: Update installer's Build-NeuroRouteConfig**

Add the new keys:
```powershell
NpuFlmHost        = "127.0.0.1"
NpuFlmPort        = 52625
NpuFlmCtxLen      = 0
NpuFlmPmode       = "performance"
```

- [ ] **Step 3: Build to verify**

```
dotnet build NeuroRoute.Service/NeuroRoute.Service.csproj --nologo
```
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add NeuroRoute.Service/Models/NeuroRouteOptions.cs install.ps1
git commit -m "feat(config): add NpuFlmHost, Port, CtxLen, Pmode options"
```

---

### Task 2: FlmStatus record

**Files:**
- Create: `NeuroRoute.Service/Npu/FlmStatus.cs`

- [ ] **Step 1: Write the FlmStatus record**

```csharp
namespace NeuroRoute.Service.Npu;

public sealed record FlmStatus(
    string Status,
    string? Message,
    string ModelTag,
    string Host,
    int Port,
    int CtxLen,
    string Pmode,
    int? Pid,
    DateTime? StartedAt
);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build NeuroRoute.Service/NeuroRoute.Service.csproj --nologo`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add NeuroRoute.Service/Npu/FlmStatus.cs
git commit -m "feat(npu): add FlmStatus record for structured NPU state"
```

---

### Task 3: FlmProcessManager — pass host/port/ctxLen/pmode, reset restart counter, expose FlmStatus

**Files:**
- Modify: `NeuroRoute.Service/Npu/FlmProcessManager.cs`

- [ ] **Step 1: Update constructor to accept IOptions<NeuroRouteOptions>**

```csharp
private readonly string _modelTag;
private readonly string _host;
private readonly int _port;
private readonly int _ctxLen;
private readonly string _pmode;
private readonly string? _sideloadDir;

public FlmProcessManager(
    string modelTag,
    string host,
    int port,
    int ctxLen,
    string pmode,
    FlmClient flmClient,
    ILogger<FlmProcessManager> logger,
    string? sideloadDir = null)
{
    _modelTag = modelTag;
    _host = host;
    _port = port;
    _ctxLen = ctxLen;
    _pmode = pmode;
    _flmClient = flmClient;
    _logger = logger;
    _sideloadDir = sideloadDir;
}
```

- [ ] **Step 2: Update StartAsync to pass host/port/ctxLen/pmode to flm serve**

Replace the current `Arguments` building with:
```csharp
var args = $"serve {_modelTag} --host {_host} --port {_port} --pmode {_pmode}";
if (_ctxLen > 0)
    args += $" --ctx-len {_ctxLen}";

var startInfo = new ProcessStartInfo
{
    FileName = flmPath,
    Arguments = args,
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true
};
```

- [ ] **Step 3: Reset restart counter on successful start**

After `WaitForReadyAsync` returns true, add:
```csharp
_restartCount = 0;
```

- [ ] **Step 4: Update FindFlmExecutable to check sideload dir first**

```csharp
private string? FindFlmExecutable()
{
    // Check sideload dir first (version-pinned)
    if (_sideloadDir is not null)
    {
        var sideloadPath = Path.Combine(_sideloadDir, "flm.exe");
        if (File.Exists(sideloadPath))
            return sideloadPath;
    }

    // Fall back to PATH
    var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
    foreach (var path in paths)
    {
        var full = Path.Combine(path.Trim(), "flm.exe");
        if (File.Exists(full))
            return full;
    }

    return null;
}
```

- [ ] **Step 5: Add GetStatus() method and _startTime tracking**

Add field: `private DateTime? _startTime;`

Set `_startTime = DateTime.UtcNow` when `WaitForReadyAsync` returns true.

```csharp
public FlmStatus GetStatus()
{
    return new FlmStatus(
        Status,
        StatusMessage,
        _modelTag,
        _host,
        _port,
        _ctxLen,
        _pmode,
        _process?.HasExited == false ? _process.Id : null,
        _startTime
    );
}
```

- [ ] **Step 6: Update Program.cs FlmProcessManager registration**

```csharp
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var options = sp.GetRequiredService<IOptions<NeuroRouteOptions>>().Value;
    var modelTag = config.GetSection("NeuroRoute")["NpuFlmModelTag"] ?? "gemma4-it:e4b";
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
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build NeuroRoute.Service/NeuroRoute.Service.csproj --nologo`
Expected: Build succeeded, 0 errors

- [ ] **Step 8: Commit**

```bash
git add NeuroRoute.Service/Npu/FlmProcessManager.cs NeuroRoute.Service/Program.cs
git commit -m "feat(flm): pass host/port/ctxLen/pmode to flm serve, reset restart counter, expose FlmStatus"
```

---

### Task 4: FlmCliService

**Files:**
- Create: `NeuroRoute.Service/Npu/FlmCliService.cs`
- Create: `NeuroRoute.Service/Models/FlmModelEntry.cs`

- [ ] **Step 1: Write FlmModelEntry record**

```csharp
using System.Text.Json.Serialization;

namespace NeuroRoute.Service.Models;

public sealed record FlmModelEntry(
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("installed")] bool Installed,
    [property: JsonPropertyName("size")] string? Size,
    [property: JsonPropertyName("quantization")] string? Quantization,
    [property: JsonPropertyName("description")] string? Description
);
```

- [ ] **Step 2: Write FlmCliService**

```csharp
using System.Diagnostics;
using System.Text.Json;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Npu;

public sealed class FlmCliService
{
    private readonly string _flmPath;
    private readonly ILogger<FlmCliService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FlmCliService(string flmPath, ILogger<FlmCliService> logger)
    {
        _flmPath = flmPath;
        _logger = logger;
    }

    public async Task<List<FlmModelEntry>> ListModelsAsync(string filter = "installed", CancellationToken ct = default)
    {
        var validFilters = new[] { "all", "installed", "not-installed" };
        if (!validFilters.Contains(filter))
            filter = "installed";

        var (exitCode, stdout) = await RunFlmAsync($"list --json --filter {filter}", ct);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return [];

        try
        {
            var models = JsonSerializer.Deserialize<List<FlmModelEntry>>(stdout, JsonOptions);
            return models ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse flm list output");
            return [];
        }
    }

    public async Task<bool> PullModelAsync(string tag, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        var (exitCode, stdout) = await RunFlmAsync($"pull {tag}", ct, onOutput);
        return exitCode == 0;
    }

    public async Task<bool> RemoveModelAsync(string tag, CancellationToken ct = default)
    {
        var (exitCode, _) = await RunFlmAsync($"remove {tag}", ct);
        return exitCode == 0;
    }

    private async Task<(int ExitCode, string Stdout)> RunFlmAsync(
        string arguments,
        CancellationToken ct,
        Action<string>? onOutput = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _flmPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var outputBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputBuilder) { outputBuilder.AppendLine(e.Data); }
            onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputBuilder) { outputBuilder.AppendLine(e.Data); }
            onOutput?.Invoke($"[stderr] {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        return (process.ExitCode, outputBuilder.ToString());
    }
}
```

- [ ] **Step 3: Register FlmCliService in Program.cs**

Inside the `else` (non-mock) block, after the FLM process manager registration:
```csharp
// FLM CLI service for one-off commands (list, pull, remove)
builder.Services.AddSingleton(sp =>
{
    var flmPath = FindFlmExecutable(sideloadDir: sideloadDir);
    if (flmPath is null)
    {
        var logger = sp.GetRequiredService<ILogger<FlmCliService>>();
        logger.LogWarning("flm.exe not found, FlmCliService disabled");
        return null;
    }
    var flmLogger = sp.GetRequiredService<ILogger<FlmCliService>>();
    return new FlmCliService(flmPath, flmLogger);
});
```

Add a shared `FindFlmExecutable` helper to `Program.cs`:
```csharp
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
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build NeuroRoute.Service/NeuroRoute.Service.csproj --nologo`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add NeuroRoute.Service/Npu/FlmCliService.cs NeuroRoute.Service/Models/FlmModelEntry.cs NeuroRoute.Service/Program.cs
git commit -m "feat(flm): add FlmCliService for list/pull/remove model commands"
```

---

### Task 5: NpuController — admin API for NPU management

**Files:**
- Create: `NeuroRoute.Service/Controllers/NpuController.cs`
- Create: `NeuroRoute.Service/Models/NpuRequestModels.cs`

- [ ] **Step 1: Write request DTOs**

```csharp
using System.Text.Json.Serialization;

namespace NeuroRoute.Service.Models;

public sealed record NpuLoadRequest(
    [property: JsonPropertyName("modelTag")] string ModelTag,
    [property: JsonPropertyName("ctxLen")] int CtxLen = 0,
    [property: JsonPropertyName("pmode")] string Pmode = "performance",
    [property: JsonPropertyName("persist")] bool Persist = false
);

public sealed record NpuPullRequest(
    [property: JsonPropertyName("tag")] string Tag
);
```

- [ ] **Step 2: Write NpuController**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeuroRoute.Service.Controllers;

[ApiController]
[Route("v1/admin/npu")]
public sealed class NpuController : ControllerBase
{
    private readonly FlmProcessManager _flmProcessManager;
    private readonly FlmCliService? _flmCliService;
    private readonly IOptions<NeuroRouteOptions> _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<NpuController> _logger;

    public NpuController(
        FlmProcessManager flmProcessManager,
        IOptions<NeuroRouteOptions> options,
        IHostEnvironment env,
        ILogger<NpuController> logger,
        FlmCliService? flmCliService = null)
    {
        _flmProcessManager = flmProcessManager;
        _flmCliService = flmCliService;
        _options = options;
        _env = env;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(_flmProcessManager.GetStatus());
    }

    [HttpGet("models")]
    public async Task<IActionResult> ListModels([FromQuery] string filter = "installed")
    {
        if (_flmCliService is null)
            return StatusCode(500, new { message = "FLM CLI not available" });

        var models = await _flmCliService.ListModelsAsync(filter);
        return Ok(models);
    }

    [HttpPost("pull")]
    public async Task<IActionResult> PullModel([FromBody] NpuPullRequest request)
    {
        if (_flmCliService is null)
            return StatusCode(500, new { message = "FLM CLI not available" });

        _logger.LogInformation("Pulling FLM model: {Tag}", request.Tag);

        _ = Task.Run(async () =>
        {
            var success = await _flmCliService.PullModelAsync(request.Tag,
                onOutput: line => _logger.LogInformation("FLM pull: {Line}", line));
            _logger.LogInformation(success
                ? "FLM model {Tag} pulled successfully"
                : "FLM model {Tag} pull failed", request.Tag);
        });

        return Accepted(new { message = $"Pulling model '{request.Tag}' in background" });
    }

    [HttpPost("load")]
    public async Task<IActionResult> LoadModel([FromBody] NpuLoadRequest request)
    {
        _logger.LogInformation("Loading FLM model: {Tag}, ctxLen={CtxLen}, pmode={Pmode}, persist={Persist}",
            request.ModelTag, request.CtxLen, request.Pmode, request.Persist);

        if (request.Persist)
        {
            PersistConfig(request);
        }

        // Restart FLM serve with new parameters
        await _flmProcessManager.StopAsync();

        // Re-create FlmProcessManager with new values (or modify fields)
        // For simplicity, the current instance handles restart via RestartAsync
        // but we need to update its internal state. A cleaner approach is to
        // have a method on FlmProcessManager to update these.
        _flmProcessManager.UpdateModel(request.ModelTag, request.CtxLen, request.Pmode);
        await _flmProcessManager.StartAsync(HttpContext.RequestAborted);

        return Ok(new { message = $"Model '{request.ModelTag}' loaded" });
    }

    [HttpDelete("models/{tag}")]
    public async Task<IActionResult> RemoveModel(string tag)
    {
        if (_flmCliService is null)
            return StatusCode(500, new { message = "FLM CLI not available" });

        _logger.LogInformation("Removing FLM model: {Tag}", tag);
        var success = await _flmCliService.RemoveModelAsync(tag);

        if (!success)
            return StatusCode(500, new { message = $"Failed to remove model '{tag}'" });

        return Ok(new { message = $"Model '{tag}' removed" });
    }

    private void PersistConfig(NpuLoadRequest request)
    {
        try
        {
            var configPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
            if (!System.IO.File.Exists(configPath))
            {
                _logger.LogWarning("Cannot persist config: {Path} not found", configPath);
                return;
            }

            var json = System.IO.File.ReadAllText(configPath);
            var node = JsonNode.Parse(json);
            if (node is null) return;

            var neuroRoute = node["NeuroRoute"];
            if (neuroRoute is null) return;

            neuroRoute["NpuFlmModelTag"] = request.ModelTag;
            neuroRoute["NpuFlmCtxLen"] = request.CtxLen;
            neuroRoute["NpuFlmPmode"] = request.Pmode;

            var options = new JsonSerializerOptions { WriteIndented = true };
            System.IO.File.WriteAllText(configPath, node.ToJsonString(options));
            _logger.LogInformation("Config persisted to {Path}", configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist config changes");
        }
    }
}
```

- [ ] **Step 3: Add UpdateModel method to FlmProcessManager**

```csharp
public void UpdateModel(string modelTag, int ctxLen, string pmode)
{
    _modelTag = modelTag;
    _ctxLen = ctxLen;
    _pmode = pmode;
}
```

Change the fields to be mutable (remove `readonly`):
```csharp
private string _modelTag;
private string _host;
private int _port;
private int _ctxLen;
private string _pmode;
```

- [ ] **Step 4: Register NpuController in Program.cs**

No additional registration needed — `builder.Services.AddControllers()` already scans for all controllers.

But ensure `FlmCliService` is registered as nullable:
```csharp
builder.Services.AddSingleton<FlmCliService?>(sp => ...);
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build NeuroRoute.Service/NeuroRoute.Service.csproj --nologo`
Expected: Build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add NeuroRoute.Service/Controllers/NpuController.cs NeuroRoute.Service/Models/NpuRequestModels.cs NeuroRoute.Service/Npu/FlmProcessManager.cs NeuroRoute.Service/Npu/FlmProcessManager.cs
git commit -m "feat(api): add /v1/admin/npu endpoints for FLM model management"
```

---

### Task 6: Update HealthService to use FlmStatus

**Files:**
- Modify: `NeuroRoute.Service/Services/HealthService.cs`

- [ ] **Step 1: Update GetNpuHealth FLM path to use FlmStatus**

Replace the current FLM block:
```csharp
if (backend.Equals("flm", StringComparison.OrdinalIgnoreCase))
{
    var status = _flmProcessManager!.GetStatus();
    return new ComponentHealth
    {
        Status = status.Status == "healthy" ? "healthy" : "unhealthy",
        Message = status.Message ?? "FLM backend unavailable",
        Backend = "flm",
        Endpoint = $"http://{status.Host}:{status.Port}",
        Model = status.ModelTag,
        ModelLoaded = status.Status == "healthy"
    };
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build NeuroRoute.Service/NeuroRoute.Service.csproj --nologo`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add NeuroRoute.Service/Services/HealthService.cs
git commit -m "refactor(health): use FlmStatus for FLM health check"
```

---

### Task 7: Dashboard — expand NPU card

**Files:**
- Modify: `NeuroRoute.Dashboard/Services/NeuroRouteApiClient.cs`
- Modify: `NeuroRoute.Dashboard/Components/Pages/Dashboard.razor`

- [ ] **Step 1: Add NPU admin API methods to NeuroRouteApiClient**

```csharp
public async Task<JsonElement?> GetNpuStatusAsync(CancellationToken ct = default)
{
    try
    {
        return await _http.GetFromJsonAsync<JsonElement>("/v1/admin/npu", ct);
    }
    catch { return null; }
}

public async Task<List<FlmModelEntry>?> GetNpuModelsAsync(string filter = "installed", CancellationToken ct = default)
{
    try
    {
        return await _http.GetFromJsonAsync<List<FlmModelEntry>>($"/v1/admin/npu/models?filter={filter}", ct);
    }
    catch { return null; }
}

public async Task<bool> LoadNpuModelAsync(string modelTag, int ctxLen, string pmode, bool persist)
{
    try
    {
        var response = await _http.PostAsJsonAsync("/v1/admin/npu/load", new
        {
            modelTag, ctxLen, pmode, persist
        });
        return response.IsSuccessStatusCode;
    }
    catch { return false; }
}

public async Task<bool> PullNpuModelAsync(string tag)
{
    try
    {
        var response = await _http.PostAsJsonAsync("/v1/admin/npu/pull", new { tag });
        return response.IsSuccessStatusCode;
    }
    catch { return false; }
}

public async Task<bool> RemoveNpuModelAsync(string tag)
{
    try
    {
        var response = await _http.DeleteAsync($"/v1/admin/npu/models/{tag}");
        return response.IsSuccessStatusCode;
    }
    catch { return false; }
}
```

Add model class:
```csharp
public sealed record FlmModelEntry(
    string Tag,
    bool Installed,
    string? Size,
    string? Quantization,
    string? Description
);
```

- [ ] **Step 2: Restructure Dashboard.razor NPU card**

Replace the static NPU card with an expanded card showing:
- Status indicator
- Backend type
- Model dropdown
- Host/Port (read-only)
- Context length input
- Power mode dropdown
- PID/Uptime
- Pull/Remove/Restart buttons
- "Save as default" checkbox

Key changes:
```razor
@* NPU Card *@
<MudCard Class="mb-4">
    <MudCardHeader>
        <MudText Typo="Typo.h6">
            <MudBadge Color="@(npuStatus?.GetProperty("status").GetString() == "healthy" ? Color.Success : Color.Error)" Dot>
                NPU Backend
            </MudBadge>
        </MudText>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="@RestartBackend" Size="Size.Small">
            Restart
        </MudButton>
    </MudCardHeader>
    <MudCardContent>
        <MudNumericField @bind-Value="@ctxLen" Label="Context Length" Min="0" Max="262144" />
        <MudSelect @bind-Value="@selectedModel" Label="Model">
            @foreach (var m in installedModels)
            {
                <MudSelectItem Value="@m.Tag">@m.Tag</MudSelectItem>
            }
        </MudSelect>
        <MudCheckBox @bind-Checked="@persist" Label="Save as default" />
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="@LoadModel">Load Model</MudButton>
    </MudCardContent>
</MudCard>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build NeuroRoute.Dashboard/NeuroRoute.Dashboard.csproj --nologo`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add NeuroRoute.Dashboard/Services/NeuroRouteApiClient.cs NeuroRoute.Dashboard/Components/Pages/Dashboard.razor
git commit -m "feat(dashboard): expand NPU card with model selection and config controls"
```

---

### Task 8: Installer — validate + pull at install time

**Files:**
- Modify: `install.ps1`

- [ ] **Step 1: Add validate and pull steps after FLM installation**

After the FLM install block (Phase 1), add:
```powershell
# FLM Validation & Model Pull
if ($flmInstalled -or $resolvedFlmMode -eq "Global") {
    Write-Header "Phase 1b: FLM Validation & Model Pull"
    $flmExe = if ($flmInstalled) { Join-Path -Path $InstallDir -ChildPath "flm\flm.exe" } else { "flm.exe" }

    Write-Step "Validating NPU..."
    try {
        $validateOutput = & $flmExe validate 2>&1
        Write-Step $validateOutput
    } catch {
        Write-Warning "FLM validation failed (non-critical): $_"
    }

    Write-Step "Pulling default model ($npuFlmModelTag)..."
    try {
        & $flmExe pull $npuFlmModelTag 2>&1
        Write-Success "Model $npuFlmModelTag pulled"
    } catch {
        Write-Warning "Model pull failed (non-critical): $_"
        Write-Step "Run manually: flm pull $npuFlmModelTag"
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add install.ps1
git commit -m "feat(installer): add flm validate and flm pull at install time"
```

---

### Task 9: Tests for FlmCliService

**Files:**
- Create: `NeuroRoute.Tests/Npu/FlmCliServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;

namespace NeuroRoute.Tests.Npu;

public sealed class FlmCliServiceTests
{
    [Fact]
    public async Task ListModelsAsync_ReturnsEmptyList_WhenFlmNotAvailable()
    {
        // Use a fake path that doesn't exist
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<FlmCliService>();
        var service = new FlmCliService("nonexistent-flm.exe", logger);
        var result = await service.ListModelsAsync();
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NeuroRoute.Tests/NeuroRoute.Tests.csproj --filter "FlmCliServiceTests" --nologo`
Expected: Test run fails (type not found or project doesn't exist)

- [ ] **Step 3: Write passing test**

Add test project if needed, or skip if project doesn't exist yet.

- [ ] **Step 4: Commit**

```bash
git add NeuroRoute.Tests/Npu/FlmCliServiceTests.cs
git commit -m "test: add FlmCliService tests"
```

---

### Task 10: Tests for NpuController

**Files:**
- Create: `NeuroRoute.Tests/Controllers/NpuControllerTests.cs`

- [ ] **Step 1: Write tests for NpuController endpoints**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuroRoute.Service.Controllers;
using NeuroRoute.Service.Models;
using NeuroRoute.Service.Npu;
using NSubstitute;

namespace NeuroRoute.Tests.Controllers;

public sealed class NpuControllerTests
{
    [Fact]
    public async Task GetStatus_ReturnsOk_WithFlmStatus()
    {
        var processManager = Substitute.For<FlmProcessManager>();
        var status = new FlmStatus("healthy", null, "gemma4-it:e4b",
            "127.0.0.1", 52625, 65536, "performance", 12345, DateTime.UtcNow);
        processManager.GetStatus().Returns(status);

        var options = Substitute.For<IOptions<NeuroRouteOptions>>();
        var env = Substitute.For<IHostEnvironment>();
        var logger = NullLogger<NpuController>.Instance;

        var controller = new NpuController(processManager, options, env, logger);
        var result = await controller.GetStatus();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(status, okResult.Value);
    }
}
```

- [ ] **Step 2: Run tests to verify**

Run: `dotnet test NeuroRoute.Tests/NeuroRoute.Tests.csproj --filter "NpuControllerTests" --nologo`
Expected: Tests pass

- [ ] **Step 3: Commit**

```bash
git add NeuroRoute.Tests/Controllers/NpuControllerTests.cs
git commit -m "test: add NpuController tests"
```

---

### Task 11: Update FLM_INTEGRATION.md

**Files:**
- Modify: `docs/FLM_INTEGRATION.md`

- [ ] **Step 1: Update FLM_INTEGRATION.md with new architecture**

Add sections for:
- FlmCliService
- New config options (NpuFlmHost, NpuFlmPort, NpuFlmCtxLen, NpuFlmPmode)
- Admin API endpoints
- Dashboard NPU card

- [ ] **Step 2: Commit**

```bash
git add docs/FLM_INTEGRATION.md
git commit -m "docs: update FLM integration docs with new lifecycle and admin API"
```
