# FastFlowLM (FLM) Backend Reference

Integrates [FastFlowLM](https://github.com/FastFlowLM/FastFlowLM) as an external NPU inference backend — replaces direct ONNX GenAI loading with an `flm serve` child process exposing an OpenAI-compatible API.

> **Status**: ✅ Implemented in v1.0.0

## Motivation

- AMD NPU-optimized runtime (XDNA2 NPUs: Strix, Strix Halo, Kraken, Gorgon Point)
- 17 MB footprint, installs in 20 seconds
- Runs models via `flm serve <model_tag>` on configurable port
- Supports streaming, vision, audio, embeddings via OpenAI-compatible API
- No GPU, no CUDA, no direct ONNX runtime on the target machine

## Architecture

```
ChatController
  → Router
    → NpuPlanner (ApproximateTokenizer — unchanged)
    → NpuModel → INpuBackend
      ├── OnnxBackend (ONNX GenAI via OnnxSessionFactory)
      └── FlmBackend → FlmClient → flm serve :{NpuFlmPort}
    → GpuClient → GPU server (unchanged)

Admin endpoints (via NpuController):
  GET  /v1/admin/npu                → FlmStatus (current runtime state)
  GET  /v1/admin/npu/models         → flm list --json
  POST /v1/admin/npu/pull           → flm pull <tag>
  POST /v1/admin/npu/load           → restart flm serve with new params
  DELETE /v1/admin/npu/models/{tag} → flm remove <tag>

Dashboard polls these endpoints every 5s for the expanded NPU card.
```

Backend selection is driven by the `NpuBackend` config key (`"onnx"` or `"flm"`).

## Components

| Component | File | Responsibility |
|-----------|------|----------------|
| `INpuBackend` | `Npu/INpuBackend.cs` | Abstraction over ONNX and FLM inference |
| `OnnxBackend` | `Npu/OnnxBackend.cs` | ONNX GenAI via OnnxSessionFactory (original path) |
| `FlmBackend` | `Npu/FlmBackend.cs` | FLM inference via FlmClient |
| `FlmClient` | `Npu/FlmClient.cs` | HTTP client for FLM OpenAI API on configured port |
| `FlmProcessManager` | `Npu/FlmProcessManager.cs` | FLM child process lifecycle (host, port, ctxLen, pmode) |
| `FlmCliService` | `Npu/FlmCliService.cs` | One-off FLM commands (list, pull, remove models) |
| `NpuController` | `Controllers/NpuController.cs` | Admin API for NPU management |
| `FlmStatus` | `Npu/FlmStatus.cs` | Structured record of current FLM runtime state |
| `NpuModel` | `Npu/NpuModel.cs` | Delegates to the active INpuBackend |

## Configuration

```json
{
  "NeuroRoute": {
    "NpuBackend": "flm",
    "NpuFlmModelTag": "gemma4-it:e4b",
    "NpuFlmHost": "127.0.0.1",
    "NpuFlmPort": 52625,
    "NpuFlmCtxLen": 0,
    "NpuFlmPmode": "performance"
  }
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `NpuFlmModelTag` | `gemma4-it:e4b` | Model tag for `flm serve` |
| `NpuFlmHost` | `127.0.0.1` | Host binding for `flm serve --host` |
| `NpuFlmPort` | `52625` | Port for `flm serve --port` |
| `NpuFlmCtxLen` | `0` | Context length (`0` = model default, omit `--ctx-len`) |
| `NpuFlmPmode` | `performance` | NPU power mode: `powersaver`, `balanced`, `performance`, `turbo` |

`NpuFlmEndpoint` is derived from `http://{NpuFlmHost}:{NpuFlmPort}`. The old key is kept for backwards compatibility.

## FlmProcessManager Lifecycle

```
Service.Start
  → Worker.ExecuteAsync
    → Detect config: NpuBackend == "flm"
    → Check flm.exe (sideload dir first, then PATH)
    → If not found: log error, service starts (NPU degraded)
    → Spawn: flm serve <tag> --host <H> --port <P> --ctx-len <C> --pmode <M>
    → Poll: GET /v1/models every 500ms, max 30s
    → On ready: log "FLM backend ready", reset restart counter
    → On crash: log warning, attempt restart (max 3, counter resets on success)
    → Service.Stop: send CtrlBreak, wait 5s, kill if needed
```

## FlmClient API Mapping

| NeuroRoute call | FLM HTTP call |
|----------------|---------------|
| `ClassifyAsync(prompt)` | `POST /v1/chat/completions` with classification system prompt |
| `GenerateAsync(messages)` | `POST /v1/chat/completions` non-streaming |
| `StreamAsync(messages)` | `POST /v1/chat/completions` with `stream: true`, SSE parsing |
| `PingAsync()` | `GET /v1/models` (health check) |

## Admin API — NPU Management

All endpoints under `/v1/admin/npu`:

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/v1/admin/npu` | Current `FlmStatus` (model, host, port, ctxLen, pmode, pid, uptime) |
| `GET` | `/v1/admin/npu/models?filter=installed` | List models from `flm list --json` |
| `POST` | `/v1/admin/npu/pull` | Async pull model `{"tag":"gemma4-it:e4b"}` |
| `POST` | `/v1/admin/npu/load` | Switch model `{"modelTag","ctxLen","pmode","persist"}` |
| `DELETE` | `/v1/admin/npu/models/{tag}` | Remove model via `flm remove <tag>` |

### POST /v1/admin/npu/load

```json
{
  "modelTag": "gemma4-it:e4b",
  "ctxLen": 65536,
  "pmode": "performance",
  "persist": false
}
```

Stops current `flm serve`, restarts with new model/params. When `persist: true`, saves new values to `appsettings.json` (survives restart).

### POST /v1/admin/npu/pull

Returns `202 Accepted` immediately. Progress is logged to the NeuroRoute event log. The Dashboard polls `GET /v1/admin/npu/models?filter=installed` to detect when the download completes.

## Dashboard — NPU Info Panel

The main Dashboard page shows an expanded NPU card with:

- **Status indicator** — green pulsing (healthy) / red static (unhealthy)
- **Backend type** — `flm`
- **Model** — dropdown of downloaded models, switch on selection
- **Host / Port** — read-only display
- **Context length** — editable number input
- **Power mode** — dropdown (powersaver, balanced, performance, turbo)
- **PID / Uptime** — read-only process info
- **"Save as default"** checkbox — persists ctxLen and pmode to `appsettings.json`
- **Action buttons** — Restart, Pull Model (inline input for tag), Remove

## Installer — Validation & Model Pull

The installer runs `flm validate` and `flm pull <NpuFlmModelTag>` after FLM installation. Both are non-blocking — the install continues if they fail. This ensures the default model is downloaded before the service starts for the first time.

## Fallback Behavior

- If `NpuBackend == "flm"` but `flm.exe` is not found or FLM fails to start, NeuroRoute logs a critical error and the NPU path is degraded (all requests go to GPU)
- If `NpuBackend == "onnx"` but the model file is missing, same degraded behavior
- The health endpoint (`/v1/health`) reflects the NPU component status accordingly, using `FlmStatus` for the FLM path

## Dependency Changes

| Package | Status |
|---------|--------|
| `Microsoft.ML.OnnxRuntime` 1.27.0 | Optional (only if `NpuBackend == "onnx"`) |
| `Microsoft.ML.OnnxRuntimeGenAI` 0.14.1 | Optional (only if `NpuBackend == "onnx"`) |
| `flm.exe` (external) | Required if `NpuBackend == "flm"` — installed via FastFlowLM setup |
