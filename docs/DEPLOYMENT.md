# NeuroRoute — Windows Deployment Guide

## 1. Prerequisites

| Component | Requirement | Check |
|-----------|-------------|-------|
| OS | Windows 10 22H2+, Windows Server 2022+ | `[System.Environment]::OSVersion.Version` |
| .NET Runtime | .NET 10.0+ | `dotnet --list-runtimes` |
| Visual C++ Redist | VC++ 2019+ x64 | Required by ONNX Runtime (ONNX backend) |
| GPU Backend | Any OpenAI-compatible server (vLLM, llama.cpp, etc.) | HTTP endpoint required |
| NPU Model / Tool | **ONNX backend**: `.onnx` file; **FLM backend**: `flm.exe` in PATH | See sections below |

### 1.1 Install .NET Runtime

```pwsh
# Check if .NET 10 is installed
dotnet --list-runtimes | Select-String "10.0"

# If not, download from https://dotnet.microsoft.com/download/dotnet/10.0
# Install the .NET Runtime 10.0.x (not just the SDK) on the target machine
```

### 1.2 Install Visual C++ Redistributable

Download and install the latest VC++ 2015-2022 x64 redistributable from Microsoft.

### 1.3 Install FastFlowLM (for FLM backend)

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

## 2. Build

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

## 3. Configuration

### 3.1 Main Config File

Copy `appsettings.json` to the publish directory. All settings are in the `NeuroRoute` section:

| Key | Default | Applicable backend | Description |
|-----|---------|--------------------|-------------|
| `NpuBackend` | `onnx` | both | Selects NPU inference provider (`onnx` or `flm`) |
| `NpuModelPath` | `Models/gemma-4-int4.onnx` | onnx | Path to ONNX GenAI model file (relative to exe or absolute) |
| `NpuFlmModelTag` | `gemma4-it:e4b` | flm | FastFlowLM model tag for `flm serve` |
| `NpuFlmEndpoint` | `http://127.0.0.1:52625` | flm | FastFlowLM server base URL |
| `GpuEndpoint` | `http://localhost:8080` | both | Base URL of the OpenAI-compatible GPU backend |
| `NpuLimit` | `65536` | both | Max tokens for NPU-only classification (set to match model's context window) |
| `NpuSlice` | `2048` | both | Token count from the end when prompt exceeds NpuLimit |
| `GpuMaxRetries` | `3` | both | Retry attempts for GPU requests |
| `GpuTimeoutSeconds` | `300` | both | GPU request timeout |

### 3.2 Binding / Port

The Kestrel section controls the HTTP endpoint:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"
      }
    }
  }
}
```

Change the port or bind to a network interface as needed. For production, consider HTTPS via reverse proxy (IIS, nginx).

### 3.3 Backend-specific config

#### ONNX backend (`NpuBackend: "onnx"`)

Ensure the model file exists at the path specified in `NpuModelPath`:

```
C:\NeuroRoute\Models\
  └── gemma-4-int4.onnx
```

#### FLM backend (`NpuBackend: "flm"`)

NeuroRoute will auto-start `flm serve <NpuFlmModelTag>` as a child process on startup.
The model tag uses the FastFlowLM format — available tags with `flm list`.
FastFlowLM must be installed on the machine (see [1.3 Install FastFlowLM](#13-install-fastflowlm-for-flm-backend)).

### 3.4 Logging Levels

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

## 4. Install as Windows Service

### 4.1 Using `sc.exe` (built-in)

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

### 4.2 Using `New-Service` (PowerShell)

```pwsh
New-Service -Name NeuroRoute `
  -BinaryPathName "C:\NeuroRoute\publish\NeuroRoute.Service.exe --content-root C:\NeuroRoute\publish" `
  -DisplayName "NeuroRoute Routing Gateway" `
  -Description "Hybrid NPU-to-GPU routing gateway for local LLM execution" `
  -StartupType Automatic

Start-Service -Name NeuroRoute
```

### 4.3 FLM process management

When using `NpuBackend: "flm"`, NeuroRoute automatically:

1. Detects `flm.exe` in PATH on startup
2. Spawns `flm serve <NpuFlmModelTag>` as a child process
3. Polls `GET /v1/models` every 500ms (up to 30s) until the server is ready
4. Monitors the process; auto-restarts up to 3 times if it crashes
5. Stops the FLM process on NeuroRoute service shutdown

If `flm.exe` is not found, NeuroRoute logs a critical error and runs in degraded mode (all requests routed to GPU).

### 4.4 Working Directory

The `--content-root` argument sets `IHostEnvironment.ContentRootPath` so that relative paths in `appsettings.json` (like `Models/gemma-4-int4.onnx`) resolve correctly. Alternatively, use absolute paths in config.

### 4.5 Verify Installation

```pwsh
# Check service status
Get-Service NeuroRoute

# Test API
Invoke-RestMethod -Uri http://localhost:5000/v1/chat/completions `
  -Method Post `
  -Body '{"model":"neuro-route","messages":[{"role":"user","content":"ping"}],"max_tokens":16}' `
  -ContentType "application/json"
```

---

## 5. Service Management

### Start / Stop / Restart

```pwsh
Start-Service NeuroRoute
Stop-Service NeuroRoute
Restart-Service NeuroRoute
```

### Set Startup Type

```pwsh
# Automatic (default)
sc.exe config NeuroRoute start=auto

# Manual
sc.exe config NeuroRoute start=demand

# Disabled
sc.exe config NeuroRoute start=disabled
```

### View Status

```pwsh
Get-Service NeuroRoute | Select-Object Name, Status, StartType
```

---

## 6. Logs & Monitoring

### 6.1 Application Logs

Logs go to the Windows Event Log under **Applications and Services Logs > NeuroRoute**. View with:

```pwsh
Get-WinEvent -LogName "NeuroRoute" -MaxEvents 100 | `
  Format-Table TimeCreated, LevelDisplayName, Message -Wrap
```

### 6.2 Console Logging (for debugging)

Stop the service and run the executable directly in a terminal:

```pwsh
& C:\NeuroRoute\publish\NeuroRoute.Service.exe --content-root C:\NeuroRoute\publish
```

All log output appears in the console with real-time timestamps.

### 6.3 File Logging (optional)

To add rolling file logging, add the `Serilog` packages and configure in `appsettings.json`:

```
dotnet add .\NeuroRoute.Service\NeuroRoute.Service.csproj package Serilog.AspNetCore
dotnet add .\NeuroRoute.Service\NeuroRoute.Service.csproj package Serilog.Sinks.File
```

--- wait for integration if needed.

---

## 7. GPU Backend Setup

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

## 7.5 System Tray Application

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

## 8. Directory Layout (Production)

```
C:\NeuroRoute\
├── publish\
│   ├── NeuroRoute.Service.exe
│   ├── appsettings.json
│   └── *.dll
├── Models\                        <-- ONNX model (ONNX backend)
│   └── gemma-4-int4.onnx
├── gpu-backend\                   <-- GPU server binary
│   └── ...
└── logs\                          <-- optional file logs
```

> For FLM backend, models are stored in FLM's own directory (`C:\Users\<USER>\Documents\flm\models\`).

---

## 9. Uninstall

```pwsh
# Stop the service
Stop-Service NeuroRoute

# Delete
sc.exe delete NeuroRoute

# Or via PowerShell
Remove-Service -Name NeuroRoute

# Clean up files
Remove-Item -Path C:\NeuroRoute -Recurse -Force
```

---

## 10. Troubleshooting

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

## 11. Security Notes

- By default, the service binds to `http://localhost:5000` (loopback only)
- For remote access, put a reverse proxy (IIS ARR, nginx) with HTTPS in front
- Do not expose the GPU backend directly — route through NeuroRoute
- Run GPU backend on the same host bound to `127.0.0.1` only
