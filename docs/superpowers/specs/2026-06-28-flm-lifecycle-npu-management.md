# FLM Lifecycle & Dashboard NPU Management

Date: 2026-06-28
Status: Draft

## Summary

Overhaul the FLM NPU backend lifecycle — reliable startup with configurable host/port/ctxLen, admin API for model management, Dashboard NPU info panel with model switching, pull, remove, and context control.

---

## 1. Installer Changes

### FLM Validation & Model Pull

New step after FLM installation (Phase 1), before building NeuroRoute:

```pwsh
flm validate                    # check NPU is ready, logs pass/fail
flm pull <NpuFlmModelTag>       # download default model
```

Both are non-blocking warnings — install continues regardless. Failures are logged so the user knows to pull manually later.

### FLM Path Detection

`FindFlmExecutable()` currently searches `PATH` only. Add sideload directory check: if `C:\Program Files\NeuroRoute\flm\flm.exe` exists, prefer it (version-pinned). Fall back to PATH.

---

## 2. Config Options

Add to `NeuroRouteOptions`:

| Key | Default | Description |
|-----|---------|-------------|
| `NpuFlmHost` | `127.0.0.1` | Host for `flm serve --host` |
| `NpuFlmPort` | `52625` | Port for `flm serve --port` |
| `NpuFlmCtxLen` | `0` | Context length (`0` = model default, omit `--ctx-len`) |
| `NpuFlmPmode` | `performance` | NPU power mode (powersaver, balanced, performance, turbo) |

`NpuFlmEndpoint` is derived as `http://{NpuFlmHost}:{NpuFlmPort}`. Keep the config key for backwards compat but it becomes secondary.

Also add to installer's `Build-NeuroRouteConfig`.

---

## 3. FlmProcessManager Improvements

### Process Start

```
flm serve <modelTag>
  --host <NpuFlmHost>
  --port <NpuFlmPort>
  --ctx-len <NpuFlmCtxLen>    (omitted if 0)
  --pmode <NpuFlmPmode>
```

### Restart Counter Reset

After `WaitForReadyAsync` returns `true` -> set `_restartCount = 0`. This prevents permanent lockout from transient crashes.

### Exposed `FlmStatus` Record

Replace ad-hoc `Status` / `StatusMessage` string properties with a structured record:

```
FlmStatus
- Status: string (healthy | unhealthy)
- Message: string?
- ModelTag: string
- Host: string
- Port: int
- CtxLen: int
- Pmode: string
- Pid: int?
- StartedAt: DateTime?
```

Exposed as `FlmProcessManager.GetStatus()`.

### Path Search Order

1. Sideload: `InstallDir\flm\flm.exe` (passed via constructor)
2. PATH: scan environment PATH entries

---

## 4. New Service: `FlmCliService`

Spawns `flm.exe` for one-off commands. Registered in DI when `NpuBackend == "flm"`.

```
FlmCliService
- ListModelsAsync(filter: string = "all") -> List<FlmModelEntry>
- PullModelAsync(tag: string, onOutput: Action<string>?) -> bool
- RemoveModelAsync(tag: string) -> bool
```

```
FlmModelEntry
- Tag: string
- Installed: bool
- Size: string
- Quantization: string
- Description: string?
```

`PullModelAsync` reads stdout/stderr line by line, passing each line to the `onOutput` callback for log streaming.

---

## 5. Admin API — New Endpoints

All under `/v1/admin/npu`:

| Method | Path | Body / Params | Returns |
|--------|------|---------------|---------|
| `GET` | `/v1/admin/npu` | - | `FlmStatus` |
| `GET` | `/v1/admin/npu/models` | `?filter=installed` | `List<FlmModelEntry>` |
| `POST` | `/v1/admin/npu/pull` | `{"tag":"gemma4-it:e4b"}` | `202 Accepted` |
| `POST` | `/v1/admin/npu/load` | `{"modelTag","ctxLen","pmode","persist"}` | `{"message":"..."}` |
| `DELETE` | `/v1/admin/npu/models/{tag}` | - | `{"message":"..."}` |

### POST /v1/admin/npu/load

```json
{
  "modelTag": "gemma4-it:e4b",
  "ctxLen": 65536,
  "pmode": "performance",
  "persist": false
}
```

Stops current `flm serve`, restarts with new params. If `persist: true`, writes `NpuFlmModelTag`, `NpuFlmCtxLen`, `NpuFlmPmode` to `appsettings.json` before restarting.

### POST /v1/admin/npu/pull

Async. Returns `202 Accepted` immediately. Progress logged to event log. Dashboard polls `/v1/admin/npu/models?filter=installed` to detect completion.

---

## 6. Dashboard — NPU Card Expansion

### Layout

The existing NPU card expands to show FLM runtime info and controls:

- Status indicator (green pulsing / red static)
- Backend type, model tag (dropdown of installed models)
- Host, Port (read-only display)
- Context length (editable number input)
- Power mode (dropdown)
- PID, uptime (read-only)
- Action buttons: Restart, Pull Model, Remove
- "Save as default" checkbox next to context length + power mode

### Data flow

| Action | Endpoint | Effect |
|--------|----------|--------|
| Page load + 5s poll | `GET /v1/admin/npu` | Update all fields |
| Model dropdown load | `GET /v1/admin/npu/models?filter=installed` | Populate model select |
| Select model | `POST /v1/admin/npu/load { modelTag, ctxLen, pmode, persist }` | Switch running model |
| Edit context | On next load (input + checkbox state) | Sent with load request |
| Change power | On next load | Sent with load request |
| Click Pull | `POST /v1/admin/npu/pull { tag }` | Show spinner, refresh models on completion |
| Click Remove | `DELETE /v1/admin/npu/models/{tag}` | Remove model file |
| Click Restart | `POST /v1/admin/restart-backend` | Restart current FLM serve |

---

## 7. HealthService — Use FlmStatus

`HealthService.GetNpuHealth()` for FLM mode reads from `FlmProcessManager.GetStatus()` instead of the old `Status`/`StatusMessage` properties.

Mapping:

| ComponentHealth | FlmStatus source |
|----------------|------------------|
| `Status` | `FlmStatus.Status` ("healthy" -> "healthy", else "unhealthy") |
| `Message` | `FlmStatus.Message` |
| `Backend` | `"flm"` |
| `Endpoint` | `http://{Host}:{Port}` |
| `Model` | `ModelTag` |
| `ModelLoaded` | `Status == "healthy"` |

ONNX path unchanged.

---

## 8. Persistence Mechanism

When `POST /v1/admin/npu/load` has `persist: true`:

1. Read `appsettings.json` from `IHostEnvironment.ContentRootPath`
2. Deserialize with `System.Text.Json.Nodes.JsonNode`
3. Navigate to `NeuroRoute.NpuFlmModelTag`, `.NpuFlmCtxLen`, `.NpuFlmPmode`
4. Update values from request body
5. Write back with indentation

---

## Future Considerations

- `POST /v1/admin/npu/save-config` as dedicated endpoint for persisting config changes
- `flm serve` streaming output progress in Dashboard (real-time log view)
- CORS toggle in Dashboard for network-exposed FLM servers
