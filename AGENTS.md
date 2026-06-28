# Agent Instructions — NeuroRoute

This file documents project-specific rules for AI agents working on NeuroRoute.
Human-relevant info lives in `README.md` and `docs/`. This file is for agent
workflow discipline that humans don't need to read.

## Process & Service Management

### Starting background processes
When a command starts a long-lived process (e.g. `dotnet run`, `sc.exe start`,
a published EXE), **always** capture its `ProcessId` via `$PROCESS_ID =
$pid`-style tracking or `Start-Process -PassThru`. This prevents orphan
processes and enables cleanup.

```pwsh
# GOOD — track PID
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --project NeuroRoute.Service" -PassThru -NoNewWindow
$procId = $proc.Id
```

### Blocking vs background classification

| Category | Handle via | Example |
|----------|-----------|---------|
| Quick sync (≤30s) | `Start-Process -Wait` with timeout | `dotnet build`, `dotnet test` |
| Long-lived server | Background `Start-Process -PassThru` | `dotnet run`, published EXE |
| Service management | `sc.exe` / `*-Service` cmdlets | `Start-Service`, `Stop-Service` |

### Timeout rule
Every blocking command must have an explicit timeout. Use `Start-Process -Wait`
with `$task.Wait(timeout)` or embed a timeout in the tool call parameter.

```pwsh
# GOOD — timed wait
$task = Start-Process -FilePath "dotnet" -ArgumentList "test .\NeuroRoute.Tests" -Wait -PassThru -NoNewWindow
if ($task.ExitCode -ne 0) { throw "Test failed with exit $($task.ExitCode)" }
```

Avoid bare `Start-Process -Wait` without a timeout fallback mechanism.

### Cleanup imperative
After launching a background process:
1. Store its PID
2. On completion of the task, **terminate** the process and **verify** via
   `Get-Process -Id $pid -ErrorAction SilentlyContinue`
3. Check that network ports are freed (`Get-NetTCPConnection -LocalPort
   $port -ErrorAction SilentlyContinue`)

```pwsh
# Cleanup pattern
if ($procId -and (Get-Process -Id $procId -ErrorAction SilentlyContinue)) {
    Stop-Process -Id $procId -Force
    Wait-Process -Id $procId -Timeout 5 -ErrorAction SilentlyContinue
}
# Verify port is free
$listener = Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue
if (-not $listener) { "Port 5000 freed" }
```

### Service lifecycle
Use PowerShell cmdlets over `sc.exe` where possible:

```pwsh
# Preferred
Start-Service NeuroRoute
Stop-Service NeuroRoute
Restart-Service NeuroRoute
Get-Service NeuroRoute, NeuroRouteDashboard

# Fallback (when cmdlet unavailable or for creation)
sc.exe create NeuroRoute binPath="..." start=auto
sc.exe delete NeuroRoute
```

Always verify state after service commands:
```pwsh
$svc = Get-Service NeuroRoute
if ($svc.Status -ne 'Running') { throw "Service failed to start (status: $($svc.Status))" }
```

## Repeated Tool Calls & Commands

All of these must be referenced here rather than retyped from scratch each time.

### Build
```pwsh
# Full solution publish (Service + Tray + Dashboard)
dotnet publish .\NeuroRoute.Service\NeuroRoute.Service.csproj -c Release -r win-x64 --self-contained -o .\publish\service --nologo
dotnet publish .\NeuroRoute.Tray\NeuroRoute.Tray.csproj -c Release -r win-x64 --self-contained -o .\publish\tray --nologo
dotnet publish .\NeuroRoute.Dashboard\NeuroRoute.Dashboard.csproj -c Release -r win-x64 --self-contained -o .\publish\dashboard --nologo

# Single project (quick iteration)
dotnet publish .\NeuroRoute.Service\NeuroRoute.Service.csproj -c Release -r win-x64 --self-contained -o .\publish
```

### Test
```pwsh
# Unit tests
dotnet test .\NeuroRoute.Tests --no-restore --nologo

# Integration tests (requires Chromium: `playwright install chromium`)
dotnet test .\NeuroRoute.Tests.Integration --no-restore --nologo

# All tests
dotnet test .\NeuroRoute.Tests --no-restore --nologo && dotnet test .\NeuroRoute.Tests.Integration --no-restore --nologo
```

### Dev mode (mock backends, no hardware)
```pwsh
# Terminal 1 — Service
$env:NeuroRoute__UseMockBackends = "true"
dotnet run --project .\NeuroRoute.Service

# Terminal 2 — Dashboard
dotnet run --project .\NeuroRoute.Dashboard --urls http://localhost:5001
```

### Smoke test (after service is running)
```pwsh
Invoke-RestMethod -Uri http://localhost:5000/v1/chat/completions `
  -Method Post `
  -Body '{"model":"neuro-route","messages":[{"role":"user","content":"Hello"}],"max_tokens":32}' `
  -ContentType "application/json"

# Health check
Invoke-RestMethod -Uri http://localhost:5000/v1/health
```

### Mock scenario programming
```pwsh
# Force GPU escalation
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario -Method Post `
  -Body '{"needsGpu":true,"gpuAvailable":true,"gpuResponseText":"Complex reasoning!"}' `
  -ContentType "application/json"

# Simulate NPU failure
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario -Method Post `
  -Body '{"npuAvailable":false}' `
  -ContentType "application/json"

# Reset to defaults
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario/reset -Method Post
```

### NPU admin
```pwsh
# Status
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/npu

# List models
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/npu/models

# Load model
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/npu/load -Method Post `
  -Body '{"modelTag":"gemma4-it:e4b","ctxLen":65536,"pmode":"performance","persist":true}' `
  -ContentType "application/json"
```

### Install / Uninstall
```pwsh
# Full install (download release)
.\install.ps1

# Build from source, skip FLM
.\install.ps1 -BuildFromSource -FlmMode Skip

# Uninstall
.\install.ps1 -Uninstall

# Validate installer logic
.\tests\install-validation.ps1
```

### Installer validation
```pwsh
# Logic only (no admin required)
.\tests\install-validation.ps1

# Full validation including integration (admin required)
.\tests\install-validation.ps1 -SkipIntegration:$false
```

### Service logs
```pwsh
Get-WinEvent -LogName "NeuroRoute" -MaxEvents 100 | Format-Table TimeCreated, LevelDisplayName, Message -Wrap
```

## Architecture Reference

| Component | Tech | Entry Point | Port |
|-----------|------|-------------|------|
| Service | .NET Worker Service | `NeuroRoute.Service.exe` | 5000 |
| Dashboard | Blazor Server | `NeuroRoute.Dashboard.exe` | 5001 |
| Tray | WinForms NotifyIcon | `NeuroRoute.Tray.exe` | — |

The Dashboard reads the Service URL from `appsettings.json` key
`NeuroRouteApi:ServiceUrl` (default `http://localhost:5000`).

## Repository Rules

- Do **not** create documentation files (*.md outside `docs/`) unless explicitly asked.
- The `install.ps1` Version parameter is extracted from `Directory.Build.props` `<Version>`.
- CI publishes only Service + Tray (not Dashboard).
- Release archives are plain zip, not signed.
