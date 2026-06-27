# NeuroRoute — Full Project Specification v1.0

Hybrid NPU→GPU Routing Gateway for Local LLM Execution

## 1. API Endpoints

### POST /v1/chat/completions

OpenAI-compatible chat completions endpoint. Supports both streaming and non-streaming modes.

### GET /v1/health

Component-level health check returning NPU, GPU, and overall service status.

### GET /v1/metrics

Aggregated routing metrics including request counts, routing case breakdown, task type distribution, and duration statistics.

---

## 2. Chat Completions

### Request Body

```json
{
  "model": "neuro-route",
  "messages": [{ "role": "user", "content": "Hello" }],
  "max_tokens": 512,
  "temperature": 0.7,
  "stream": false
}
```

### Response (non-streaming)

```json
{
  "id": "abc123",
  "object": "chat.completion",
  "created": 1719000000,
  "model": "neuro-route",
  "choices": [{
    "index": 0,
    "message": { "role": "assistant", "content": "..." },
    "finish_reason": "stop"
  }],
  "usage": { "prompt_tokens": 10, "completion_tokens": 50, "total_tokens": 60 }
}
```

### Response (streaming — SSE)

```
data: {"id":"...","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant"},"finish_reason":null}]}

data: {"id":"...","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"Hello"},"finish_reason":null}]}

data: {"id":"...","object":"chat.completion.chunk","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

data: [DONE]
```

---

## 3. Health Endpoint

### GET /v1/health

Returns component-level health status of the NeuroRoute service.

#### Response (200 — healthy or degraded)

```json
{
  "status": "healthy",
  "version": "1.0.0",
  "uptime": "0.00:05:23",
  "components": {
    "npu": { "status": "healthy", "message": "ONNX session created, model loaded" },
    "gpu": { "status": "healthy", "message": "GPU endpoint reachable" }
  }
}
```

#### Response (503 — unhealthy)

Returned when all components are unreachable or in a failing state.

| Status Code | Condition |
|-------------|-----------|
| 200 | All components healthy, or at least one healthy (degraded) |
| 503 | No components are healthy |

#### Component Status Values

| Component | Status | Meaning |
|-----------|--------|---------|
| npu | healthy | ONNX session created successfully |
| npu | unhealthy | Model file missing, session creation failed |
| gpu | healthy | GPU endpoint responds to GET /v1/health within 5s |
| gpu | unhealthy | GPU endpoint unreachable, timed out, or returned error |

---

## 4. Routing Rules

The context window limit (`NpuLimit`, default 65536) and slice size (`NpuSlice`, default 2048) are configurable in `appsettings.json`.

| Case | Condition | NPU Action | GPU Action |
|------|-----------|------------|------------|
| A | `≤ NpuLimit tokens`, simple task | Answer directly | — |
| A | `≤ NpuLimit tokens`, complex task | Classify `needs_gpu=true` | Answer |
| B | `> NpuLimit tokens` | Classify last `NpuSlice` tokens only | Answer with full prompt |
| C | `compressed_prompt` returned | — | Use compressed prompt |
| D | `notes_for_gpu` returned | — | Prepend notes to full prompt |

## 5. NPU Planner JSON Output

```json
{
  "task_type": "simple_chat | summarize | classify | code | deep_reasoning",
  "needs_gpu": true,
  "compressed_prompt": "optional compressed text",
  "notes_for_gpu": "optional instructions"
}
```

## 6. Performance Targets

| Metric | Target |
|--------|--------|
| Idle RAM | 20–40 MB |
| NPU model loaded | +50–150 MB |
| GPU memory | VRAM only (not system RAM) |
| NPU classification | < 50 ms |
| NPU generation | depends on prompt length |
| GPU inference | depends on model size |

## 7. Dependencies

### NuGet packages (always required)

| Package | Version | Purpose |
|---------|---------|---------|
| .NET | 10.0 | Runtime framework |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.9 | Windows Service hosting |

### NuGet packages (optional — ONNX backend only)

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.ML.OnnxRuntime | 1.27.0 | ONNX inference engine |
| Microsoft.ML.OnnxRuntimeGenAI | 0.14.1 | GenAI model loading |

### External process (optional — FLM backend only)

| Process | Source | Purpose |
|---------|--------|---------|
| `flm.exe` | [FastFlowLM](https://github.com/FastFlowLM/FastFlowLM) installer | AMD NPU inference server on port 52625 |

## 8. Configuration (`appsettings.json`)

```json
{
  "NeuroRoute": {
    "NpuBackend": "onnx",
    "NpuModelPath": "path/to/your-onnx-model.onnx",
    "NpuFlmModelTag": "gemma4-it:e4b",
    "NpuFlmEndpoint": "http://127.0.0.1:52625",
    "GpuEndpoint": "http://localhost:8080",
    "NpuLimit": 65536,
    "NpuSlice": 2048,
    "GpuMaxRetries": 3,
    "GpuTimeoutSeconds": 300,
    "UseMockBackends": false
  }
}
```

| Key | Default | Applicable backend | Description |
|-----|---------|--------------------|-------------|
| `NpuBackend` | `onnx` | both | Selects NPU inference provider (`onnx` or `flm`). FLM auto-starts `flm serve` as child process. |
| `NpuModelPath` | `Models/gemma-4-int4.onnx` | onnx | Path to ONNX GenAI model file |
| `NpuFlmModelTag` | `gemma4-it:e4b` | flm | FastFlowLM model tag for `flm serve` |
| `NpuFlmEndpoint` | `http://127.0.0.1:52625` | flm | FastFlowLM server base URL |
| `GpuEndpoint` | `http://localhost:8080` | both | Base URL of the OpenAI-compatible GPU server |
| `NpuLimit` | 65536 | both | Max tokens for NPU-only classification (set to match model's context window) |
| `NpuSlice` | 2048 | both | Tokens from end when prompt exceeds NpuLimit |
| `GpuMaxRetries` | 3 | both | Retry attempts for GPU requests |
| `GpuTimeoutSeconds` | 300 | both | GPU request timeout |
| `UseMockBackends` | false | both | When true, replaces real NPU/GPU backends with programmable mocks for dev/test |

## 9. Admin Mock Endpoints

When `UseMockBackends` is `true`, the following endpoints are available for controlling mock behavior:

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/v1/admin/mock/scenario` | Returns the current mock scenario state |
| POST | `/v1/admin/mock/scenario` | Partially updates the mock scenario (JSON body) |
| POST | `/v1/admin/mock/scenario/reset` | Resets mock scenario to factory defaults |

All values are configurable — no hardcoded model names.

## 10. Deployment

- Build: `dotnet publish -r win-x64 --self-contained`
- Install as Windows Service: `sc create NeuroRoute binPath=...\NeuroRoute.Service.exe`
- GPU server (any OpenAI-compatible backend — LM Studio, Lemonade Server, Unsloth Studio, llama.cpp, vLLM, etc.) runs as a separate process
- FLM server (when `NpuBackend == "flm"`) is managed as a child process by NeuroRoute — auto-starts, monitors, restarts (max 3)
- Service auto-retries GPU connection with exponential backoff (3 attempts)
- Dev mode: set `UseMockBackends: true` to run without real hardware for dashboard demos and testing

See [DEPLOYMENT.md](./DEPLOYMENT.md) for full deployment instructions.
