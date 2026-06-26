# FastFlowLM (FLM) Backend Reference

Integrates [FastFlowLM](https://github.com/FastFlowLM/FastFlowLM) as an external NPU inference backend — replaces direct ONNX GenAI loading with an `flm serve` child process exposing an OpenAI-compatible API.

> **Status**: ✅ Implemented in v1.0.0

## Motivation

- AMD NPU-optimized runtime (XDNA2 NPUs: Strix, Strix Halo, Kraken, Gorgon Point)
- 17 MB footprint, installs in 20 seconds
- Runs models `flm run <model_tag>` or `flm serve <model_tag>` on port 52625
- Supports streaming, vision, audio, embeddings via OpenAI-compatible API
- No GPU, no CUDA, no direct ONNX runtime on the target machine

## Architecture

```
ChatController
  → Router
    → NpuPlanner (ApproximateTokenizer — unchanged)
    → NpuModel → INpuBackend
      ├── OnnxBackend (ONNX GenAI via OnnxSessionFactory)
      └── FlmBackend → FlmClient → flm serve :52625
    → GpuClient → GPU server (unchanged)
```

Backend selection is driven by the `NpuBackend` config key (`"onnx"` or `"flm"`).

## Components

| Component | File | Responsibility |
|-----------|------|----------------|
| `INpuBackend` | `Npu/INpuBackend.cs` | Abstraction over ONNX and FLM inference |
| `OnnxBackend` | `Npu/OnnxBackend.cs` | ONNX GenAI via OnnxSessionFactory (original path) |
| `FlmBackend` | `Npu/FlmBackend.cs` | FLM inference via FlmClient |
| `FlmClient` | `Npu/FlmClient.cs` | HTTP client for FLM OpenAI API on port 52625 |
| `FlmProcessManager` | `Npu/FlmProcessManager.cs` | FLM child process lifecycle |
| `NpuModel` | `Npu/NpuModel.cs` | Delegates to the active INpuBackend |

## FlmProcessManager Lifecycle

```
Service.Start
  → Worker.ExecuteAsync
    → Detect config: NpuBackend == "flm"
    → Check flm.exe in PATH
    → If not found: log error, service starts (NPU degraded)
    → Spawn: flm serve <NpuFlmModelTag>
    → Poll: GET /v1/models every 500ms, max 30s
    → On ready: log "FLM backend ready"
    → On crash: log warning, attempt restart (max 3)
    → Service.Stop: send CtrlBreak, wait 5s, kill if needed
```

## FlmClient API Mapping

| NeuroRoute call | FLM HTTP call |
|----------------|---------------|
| `ClassifyAsync(prompt)` | `POST /v1/chat/completions` with classification system prompt |
| `GenerateAsync(messages)` | `POST /v1/chat/completions` non-streaming |
| `StreamAsync(messages)` | `POST /v1/chat/completions` with `stream: true`, SSE parsing |
| `PingAsync()` | `GET /v1/models` (health check) |

FLM uses port 52625 by default, no auth required (dummy key).

## Configuration

```json
{
  "NeuroRoute": {
    "NpuBackend": "flm",
    "NpuFlmModelTag": "gemma4-it:e4b",
    "NpuFlmEndpoint": "http://127.0.0.1:52625"
  }
}
```

## Fallback Behavior

- If `NpuBackend == "flm"` but `flm.exe` is not found or FLM fails to start, NeuroRoute logs a critical error and the NPU path is degraded (all requests go to GPU)
- If `NpuBackend == "onnx"` but the model file is missing, same degraded behavior
- The health endpoint (`/v1/health`) reflects the NPU component status accordingly

## Dependency Changes

| Package | Status |
|---------|--------|
| `Microsoft.ML.OnnxRuntime` 1.27.0 | Optional (only if `NpuBackend == "onnx"`) |
| `Microsoft.ML.OnnxRuntimeGenAI` 0.14.1 | Optional (only if `NpuBackend == "onnx"`) |
| `flm.exe` (external) | Required if `NpuBackend == "flm"` — installed via FastFlowLM setup |
