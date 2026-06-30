# NeuroRoute — Windows Deployment Guide

## 1. Quick Install (Recommended)

The `install.ps1` script handles everything: downloading or building NeuroRoute, installing FastFlowLM, registering the Windows Service, and creating Start Menu shortcuts.

### 1.1 Prerequisites

| Component | Requirement | Check |
|-----------|-------------|-------|
| OS | Windows 10 22H2+, Windows Server 2022+ (64-bit) | `[Environment]::Is64BitOperatingSystem` |
| PowerShell | 7.0+ (PowerShell Core) | `$PSVersionTable.PSVersion` |
| Administrator | Required for service installation | Run as admin or script auto-elevates |

### 1.2 Download and Run

```pwsh
# Download the installer from the latest release
Invoke-WebRequest -Uri "https://github.com/mrctrn/NeuroRoute/releases/latest/download/install.ps1" -OutFile "install.ps1"

# Run with defaults (sideload FLM, download release)
.\install.ps1
```

### 1.3 Common Scenarios

```pwsh
# Install from source (for development)
.\install.ps1 -BuildFromSource -FlmMode Skip

# Install with global FLM (system-wide)
.\install.ps1 -FlmMode Global

# Service only (no Start Menu shortcuts)
.\install.ps1 -ServiceOnly

# Uninstall completely
.\install.ps1 -Uninstall

# Install specific version from draft release
.\install.ps1 -Version 0.1.0 -DraftToken ghp_xxxxxxxxxxxx
```

### 1.4 What Happens

| Phase | Action |
|-------|--------|
| Elevation | Auto-restarts as admin if needed |
| FLM | Detects existing FLM → checks version (skips if valid); or sideloads/installs fresh |
| FLM Validate | Runs `flm validate` to verify the FLM installation |
| FLM Model Pull | Runs `flm pull <NpuFlmModelTag>` to download the default model |
| NeuroRoute | Downloads release zip from GitHub (or builds from source: Service, Tray, Dashboard) |
| Config | Generates `appsettings.json` for Service and Dashboard with correct backend, ports, paths |
| Service | Registers `NeuroRoute` (API) and `NeuroRouteDashboard` as Windows Services, starts both |
| Shortcuts | Adds Start Menu group with Dashboard URL, API Health, Tray, Uninstall |

### 1.5 Target Layout

```
C:\Program Files\NeuroRoute\
├── NeuroRoute.Service.exe
├── NeuroRoute.Tray.exe
├── appsettings.json
├── install.ps1
├── flm\                         (if sideloaded)
│   └── flm.exe
├── Dashboard\
│   ├── NeuroRoute.Dashboard.exe
│   ├── appsettings.json
│   └── *.dll
└── *.dll
```

---

## 2. Prerequisites (Manual)

| Component | Requirement | Check |
|-----------|-------------|-------|
| OS | Windows 10 22H2+, Windows Server 2022+ | `[System.Environment]::OSVersion.Version` |
| .NET Runtime | .NET 10.0+ | `dotnet --list-runtimes` |
| Visual C++ Redist | VC++ 2019+ x64 | Required by ONNX Runtime (ONNX backend) |
| GPU Backend | Any OpenAI-compatible server (vLLM, llama.cpp, etc.) | HTTP endpoint required |
| NPU Model / Tool | **ONNX backend**: `.onnx` file; **FLM backend**: `flm.exe` in PATH | See sections below |

### 2.1 Install .NET Runtime

```pwsh
# Check if .NET 10 is installed
dotnet --list-runtimes | Select-String "10.0"

# If not, download from https://dotnet.microsoft.com/download/dotnet/10.0
# Install the .NET Runtime 10.0.x (not just the SDK) on the target machine
```

### 2.2 Install Visual C++ Redistributable

Download and install the latest VC++ 2015-2022 x64 redistributable from Microsoft.

### 2.3 Install FastFlowLM (for FLM backend)

If using `NpuBackend: "flm"`, download and install FastFlowLM:

```pwsh
# Download the setup from GitHub
curl -L -o flm-setup.exe https://github.com/FastFlowLM/FastFlowLM/releases/latest/download/flm-setup.exe

# Run the installer
.\flm-setup.exe
```

After install, verify `flm.exe` is in PATH:

```pwsh
flm list
```

If not in PATH, locate it at `C:\Users\<USER>\AppData\Local\Programs\FastFlowLM\flm.exe` and add to PATH.

> **Note**: The ONNX backend does not require FastFlowLM. Choose the backend that matches your hardware. FLM is for AMD Ryzen AI NPUs with XDNA2. Set `NpuBackend: "flm"` in config to enable FLM mode.

---

## 3. Build (Manual)

Run on a dev machine with the .NET SDK installed:

```pwsh
# Clone or copy source
git clone <repo-url> C:\NeuroRoute
Set-Location -LiteralPath C:\NeuroRoute

# Restore dependencies
dotnet restore

# Publish self-contained (includes runtime)
dotnet publish .\NeuroRoute.Service\NeuroRoute.Service.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output C:\NeuroRoute\publish
```

Output: `C:\NeuroRoute\publish\NeuroRoute.Service.exe` (single-file)

### Publish without self-contained (needs runtime on target)

```pwsh
dotnet publish .\NeuroRoute.Service\NeuroRoute.Service.csproj `
  --configuration Release `
  --framework net10.0 `
  --output C:\NeuroRoute\publish
```

---

## 4. Configuration

### 4.1 Main Config File

Copy `appsettings.json` to the publish directory. All settings are in the `NeuroRoute` section:

| Key | Default | Applicable backend | Description |
|-----|---------|--------------------|-------------|
| `NpuBackend` | `onnx` | both | Selects NPU inference provider (`onnx` or `flm`) |
| `NpuModelPath` | `Models/gemma-4-int4.onnx` | onnx | Path to ONNX GenAI model file (relative to exe or absolute) |
| `NpuFlmModelTag` | `gemma4-it:e4b` | flm | FastFlowLM model tag for `flm serve` |
| `NpuFlmEndpoint` | `http://127.0.0.1:52625` | flm | FastFlowLM server base URL (derived from host:port if not set) |
| `NpuFlmHost` | `127.0.0.1` | flm | Host binding for `flm serve --host` |
| `NpuFlmPort` | `52625` | flm | Port for `flm serve --port` |
| `NpuFlmCtxLen` | `0` | flm | Context length (`0` = model default, omit `--ctx-len`) |
| `NpuFlmPmode` | `performance` | flm | NPU power mode: `powersaver`, `balanced`, `performance`, `turbo` |
| `GpuEndpoint` | `http://localhost:8080` | both | Base URL of the OpenAI-compatible GPU backend |
| `NpuLimit` | `65536` | both | Max tokens for NPU-only classification (set to match model's context window) |
| `NpuSlice` | `2048` | both | Token count from the end when prompt exceeds NpuLimit |
| `GpuMaxRetries` | `3` | both | Retry attempts for GPU requests |
| `GpuTimeoutSeconds` | `300` | both | GPU request timeout |
| `PassthroughMode` | `false` | both | When true, skip planner/classification and route all requests directly to NPU |
| `GpuFallbackToNpu` | `true` | both | When true, fall back to NPU if GPU is unreachable instead of returning an error |

### 4.2 Binding / Port

The Kestrel section controls the HTTP endpoint for the API service:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      }
    }
  }
}
```

The Dashboard service binds to `http://0.0.0.0:5001` by default. Both
bind to all interfaces so they're accessible from other machines on the
network. Change the port or bind to a specific IP as needed.

For production, consider HTTPS via reverse proxy (IIS, nginx).

### 4.3 Backend-specific config

#### ONNX backend (`NpuBackend: "onnx"`)

Ensure the model file exists at the path specified in `NpuModelPath`:

```
C:\NeuroRoute\Models\
  └── gemma-4-int4.onnx
```

#### FLM backend (`NpuBackend: "flm"`)

NeuroRoute will auto-start `flm serve <NpuFlmModelTag>` with the configured host, port, context length, and power mode as a child process on startup:

```
flm serve <NpuFlmModelTag> --host <NpuFlmHost> --port <NpuFlmPort> --pmode <NpuFlmPmode> [--ctx-len <NpuFlmCtxLen>]
```

The model tag uses the FastFlowLM format — available tags with `flm list`.
FastFlowLM must be installed on the machine (see [2.3 Install FastFlowLM](#23-install-fastflowlm-for-flm-backend)).

Context length and power mode are hot-swappable via `POST /v1/admin/npu/load` without editing the config file, and can be persisted to `appsettings.json` with the `persist` flag.

### 4.4 Logging Levels

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "NeuroRoute": "Information"
    }
  }
}
```

Set `NeuroRoute` to `Debug` for detailed routing decisions.

---

## 5. Install as Windows Service

### 5.1 Using `sc.exe` (built-in)

```pwsh
# Create the service
sc.exe create NeuroRoute `
  binPath="C:\NeuroRoute\publish\NeuroRoute.Service.exe --content-root C:\NeuroRoute\publish" `
  start=auto `
  DisplayName="NeuroRoute Routing Gateway"

# Set description
sc.exe description NeuroRoute "Hybrid NPU-to-GPU routing gateway for local LLM execution"

# Start
sc.exe start NeuroRoute
```

### 5.2 Using `New-Service` (PowerShell)

```pwsh
New-Service -Name NeuroRoute `
  -BinaryPathName "C:\NeuroRoute\publish\NeuroRoute.Service.exe --content-root C:\NeuroRoute\publish" `
  -DisplayName "NeuroRoute Routing Gateway" `
  -Description "Hybrid NPU-to-GPU routing gateway for local LLM execution" `
  -StartupType Automatic

Start-Service -Name NeuroRoute
```

### 5.3 FLM process management

When using `NpuBackend: "flm"`, NeuroRoute automatically:

1. Detects `flm.exe` — sideload dir first (`flm\flm.exe`), then PATH
2. Spawns `flm serve <NpuFlmModelTag> --host <host> --port <port> --pmode <pmode>` (with `--ctx-len` if `NpuFlmCtxLen > 0`)
3. Polls `GET /v1/models` every 500ms (up to 30s) until the server is ready
4. On ready: resets restart counter, records start time
5. Monitors the process; auto-restarts up to 3 times if it crashes
6. Stops the FLM process on NeuroRoute service shutdown
7. Exposes structured status via `FlmProcessManager.GetStatus()` (model tag, host, port, ctxLen, pmode, pid, uptime)
8. Supports live model switching via `FlmProcessManager.UpdateModel()` and `POST /v1/admin/npu/load`

If `flm.exe` is not found, NeuroRoute logs a critical error and runs in degraded mode (all requests routed to GPU).

### 5.4 Working Directory

The `--content-root` argument sets `IHostEnvironment.ContentRootPath` so that relative paths in `appsettings.json` (like `Models/gemma-4-int4.onnx`) resolve correctly. Alternatively, use absolute paths in config.

### 5.5 Dashboard Service

The Dashboard is registered as a companion Windows Service `NeuroRouteDashboard`
on port 5001:

```pwsh
sc.exe create NeuroRouteDashboard `
  binPath="C:\Program Files\NeuroRoute\Dashboard\NeuroRoute.Dashboard.exe --content-root C:\Program Files\NeuroRoute\Dashboard" `
  start=auto

sc.exe start NeuroRouteDashboard
```

### 5.6 Verify Installation

```pwsh
# Check service status
Get-Service NeuroRoute, NeuroRouteDashboard

# Test API
Invoke-RestMethod -Uri http://localhost:5000/v1/chat/completions `
  -Method Post `
  -Body '{"model":"neuro-route","messages":[{"role":"user","content":"ping"}],"max_tokens":16}' `
  -ContentType "application/json"

# Test Dashboard
Start-Process "http://localhost:5001"
```

---

## 6. Service Management

### Start / Stop / Restart

```pwsh
# Main API service
Start-Service NeuroRoute
Stop-Service NeuroRoute
Restart-Service NeuroRoute

# Dashboard companion service
Start-Service NeuroRouteDashboard
Stop-Service NeuroRouteDashboard
Restart-Service NeuroRouteDashboard
```

### Set Startup Type

```pwsh
# Automatic (default)
sc.exe config NeuroRoute start=auto
sc.exe config NeuroRouteDashboard start=auto

# Manual
sc.exe config NeuroRoute start=demand
sc.exe config NeuroRouteDashboard start=demand

# Disabled
sc.exe config NeuroRoute start=disabled
sc.exe config NeuroRouteDashboard start=disabled
```

### View Status

```pwsh
Get-Service NeuroRoute, NeuroRouteDashboard | Select-Object Name, Status, StartType
```

---

## 7. Logs & Monitoring

### 7.1 Application Logs

Logs go to the Windows Event Log under **Applications and Services Logs > NeuroRoute**. View with:

```pwsh
Get-WinEvent -LogName "NeuroRoute" -MaxEvents 100 | `
  Format-Table TimeCreated, LevelDisplayName, Message -Wrap
```

### 7.2 Console Logging (for debugging)

Stop the service and run the executable directly in a terminal:

```pwsh
& C:\NeuroRoute\publish\NeuroRoute.Service.exe --content-root C:\NeuroRoute\publish
```

All log output appears in the console with real-time timestamps.

### 7.3 File Logging (optional)

To add rolling file logging, add the `Serilog` packages and configure in `appsettings.json`:

```
dotnet add .\NeuroRoute.Service\NeuroRoute.Service.csproj package Serilog.AspNetCore
dotnet add .\NeuroRoute.Service\NeuroRoute.Service.csproj package Serilog.Sinks.File
```

--- wait for integration if needed.

---

## 8. GPU Backend Setup

The GPU backend runs as a **separate process/service** on the same machine or a different host. NeuroRoute is compatible with any server exposing an OpenAI-compatible `/v1/chat/completions` endpoint.

### Recommended backends

| Backend | Setup | Notes |
|---------|-------|-------|
| [LM Studio](https://lmstudio.ai/docs/app) | GUI app; enable built-in OpenAI server in settings | Zero config, local models |
| [Lemonade Server](https://lemonade-server.ai/docs/) | `lemonade serve --port 8080` | Minimal CLI, auto-downloads models |
| [Unsloth Studio](https://unsloth.ai/docs/new/studio) | GUI app; exposes OpenAI API | Fast inference, memory efficient |
| [llama.cpp server](https://github.com/ggml-org/llama.cpp) | `llama-server -m model.gguf --port 8080` | Lightweight, widely supported |
| [vLLM](https://github.com/vllm-project/vllm) | `vllm serve /path/to/model --port 8080` | High throughput, production grade |

All expose the standard `POST /v1/chat/completions` with SSE streaming support.

### Examples

**LM Studio:** Open app → Local Inference Server → Start Server (default port 1234). Set `GpuEndpoint` to `http://localhost:1234`.

**Lemonade Server:**
```pwsh
lemonade serve --port 8080
```

**llama.cpp:**
```pwsh
& .\llama-server.exe -m .\model.gguf --host 127.0.0.1 --port 8080
```

**vLLM:**
```bash
vllm serve /path/to/model --host 0.0.0.0 --port 8080
```

### Configuration

The GPU endpoint URL is set in `appsettings.json`:

Configure the URL in `appsettings.json`:

```json
{
  "NeuroRoute": {
    "GpuEndpoint": "http://localhost:8080"
  }
}
```

---

## 9. Dev Mode — No Hardware Required

When developing the Dashboard, Tray app, or testing the API, use mock backends to run the service without any real NPU/GPU hardware.

### Configuration

Set `UseMockBackends: true` in `appsettings.Development.json`:

```json
{
  "NeuroRoute": {
    "UseMockBackends": true
  }
}
```

Or pass as an environment variable:

```pwsh
$env:NeuroRoute__UseMockBackends = "true"
dotnet run --project NeuroRoute.Service
```

### What Happens

- `MockNpuBackend` replaces `OnnxBackend`/`FlmBackend` — returns canned responses with programmable latency
- `MockGpuClient` replaces `GpuClient` — returns canned responses without HTTP calls
- `MockScenario` singleton holds all programmable state
- Admin mock endpoints are registered at `/v1/admin/mock/scenario`
- Health endpoint reflects mock availability state

### Programming the Fakes

Use the admin mock endpoints to control behavior per test scenario:

```pwsh
# GPU escalation
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario -Method Post `
  -Body '{"needsGpu":true,"gpuAvailable":true,"gpuResponseText":"Complex reasoning!"}' `
  -ContentType "application/json"

# NPU down
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario -Method Post `
  -Body '{"npuAvailable":false}' `
  -ContentType "application/json"

# Both down
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario -Method Post `
  -Body '{"npuAvailable":false,"gpuAvailable":false}' `
  -ContentType "application/json"

# Reset
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario/reset -Method Post
```

### Testing the Dashboard

```pwsh
# Terminal 1: Service with mocks
$env:NeuroRoute__UseMockBackends = "true"
dotnet run --project NeuroRoute.Service

# Terminal 2: Dashboard
dotnet run --project NeuroRoute.Dashboard

# Terminal 3: Program mocks & verify
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario -Method Post `
  -Body '{"needsGpu":false,"npuAvailable":true}' `
  -ContentType "application/json"
Start-Process "http://localhost:5001"
```

---

## 10. System Tray Application

### What It Does

`NeuroRoute.Tray.exe` is a companion application that runs in the Windows system tray (notification area). It provides:

- **Status at a glance** — icon color shows service health (green = healthy, yellow = degraded, red = unhealthy, gray = stopped)
- **Context menu** — right-click for actions (open dashboard, restart NPU, reload config, stop service, etc.)
- **Auto-start** — registers itself in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` on first launch

### Build

```pwsh
dotnet publish .\NeuroRoute.Tray\NeuroRoute.Tray.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output C:\NeuroRoute\tray
```

Output: `C:\NeuroRoute\tray\NeuroRoute.Tray.exe`

### Configuration

Copy `appsettings.json` from the publish directory. Settings are in the `NeuroRoute` section:

| Key | Default | Description |
|-----|---------|-------------|
| `ServiceEndpoint` | `http://localhost:5000` | NeuroRoute service URL |
| `PollIntervalSeconds` | `5` | How often to check health |
| `GpuGuiUrl` | `http://localhost:1234` | GPU backend UI (e.g., LM Studio) |
| `AdminKey` | `""` | Optional admin auth key (empty = no auth) |
| `AutoStart` | `true` | Register for auto-start on login |

### Usage

Launch manually:

```pwsh
& C:\NeuroRoute\tray\NeuroRoute.Tray.exe
```

Or let it auto-start (enabled by default). To disable auto-start, set `AutoStart: false` in `appsettings.json`.

### Uninstall Auto-Start

Remove the registry entry:

```pwsh
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "NeuroRoute.Tray"
```

---

## 11. Directory Layout (Production)

```
C:\Program Files\NeuroRoute\
├── NeuroRoute.Service.exe         # API service
├── NeuroRoute.Tray.exe            # System tray companion
├── appsettings.json               # API service config
├── install.ps1
├── Dashboard\
│   ├── NeuroRoute.Dashboard.exe   # Dashboard service
│   ├── appsettings.json           # Dashboard config
│   └── *.dll
├── flm\                           # FLM (if sideloaded)
│   └── flm.exe
├── Models\                        # ONNX model (ONNX backend)
│   └── gemma-4-int4.onnx
└── *.dll
```

> For FLM backend, models are stored in FLM's own directory (`C:\Users\<USER>\Documents\flm\models\`).

---

## 12. Uninstall

```pwsh
# Stop both services
Stop-Service NeuroRoute
Stop-Service NeuroRouteDashboard

# Delete both
sc.exe delete NeuroRoute
sc.exe delete NeuroRouteDashboard

# Or use the installer
.\install.ps1 -Uninstall

# Clean up files
Remove-Item -Path "C:\Program Files\NeuroRoute" -Recurse -Force
```

---

## 13. Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Service fails to start | Missing VC++ redist | Install VC++ 2015-2022 x64 |
| `0x80070002` on start | Binary path wrong or file missing | Verify exe path in `sc.exe qc NeuroRoute` |
| `Connection refused` on GPU call | GPU server not running | Start GPU backend first, verify port |
| `Model not found` | NpuModelPath wrong | Use absolute path or verify `--content-root` |
| `FLM process not found` | flm.exe not in PATH | Install FastFlowLM or add to PATH |
| `FLM failed to start` | NPU driver outdated | Check NPU driver version ≥ 32.0.203.304 |
| `FLM unhealthy` | Wrong model tag | Run `flm list` to check available tags |
| Service starts but HTTP fails | Port conflict | Change port in `appsettings.json` Kestrel section |
| High CPU on startup | ONNX model loading | Expected; loads once on startup |
| `Access denied` on log | User lacks EventLog permission | Run as `LocalSystem` (default) or `NetworkService` |

### View service exit code

```pwsh
Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Service Control Manager'} -MaxEvents 10
```

---

## 14. Security Notes

- By default, the service binds to `http://0.0.0.0:5000` (all interfaces)
- For production, put a reverse proxy (IIS ARR, nginx) with HTTPS in front
- Do not expose the GPU backend directly — route through NeuroRoute
- Run GPU backend on the same host bound to `127.0.0.1` only
