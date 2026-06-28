# NeuroRoute API Reference

## Base URL

```
http://localhost:5000
```

All endpoints are served over HTTP. Use a reverse proxy (IIS ARR, nginx) for HTTPS in production.

---

## Chat Completions

### `POST /v1/chat/completions`

OpenAI-compatible chat completions endpoint.

#### Request Body

```json
{
  "model": "neuro-route",
  "messages": [
    { "role": "system",    "content": "You are a helpful assistant." },
    { "role": "user",      "content": "What is the capital of France?" },
    { "role": "assistant", "content": "Paris." },
    { "role": "user",      "content": "Tell me more about it." }
  ],
  "max_tokens": 512,
  "temperature": 0.7,
  "stream": false
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `model` | string | — | Model identifier (currently unused for routing, but must be present) |
| `messages` | array | — | Array of message objects with `role` and `content` |
| `max_tokens` | int | 512 | Maximum tokens in the response |
| `temperature` | float | 0.7 | Sampling temperature (0–2) |
| `stream` | bool | false | If true, response is SSE streamed |

#### Response (non-streaming, `stream: false`)

```json
{
  "id": "a1b2c3d4e5f6g7h8",
  "object": "chat.completion",
  "created": 1719000000,
  "model": "neuro-route",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "Paris is the capital and largest city of France..."
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 10,
    "completion_tokens": 50,
    "total_tokens": 60
  }
}
```

#### Response (streaming, `stream: true`)

The response uses Server-Sent Events (SSE):

```
data: {"id":"...","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant"},"finish_reason":null}]}

data: {"id":"...","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"Paris"},"finish_reason":null}]}

data: {"id":"...","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":" is"},"finish_reason":null}]}

data: {"id":"...","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":" the"},"finish_reason":null}]}

data: {"id":"...","object":"chat.completion.chunk","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

data: [DONE]
```

The stream may emit single tokens, multi-token chunks, or character-level deltas depending on the backend. The client should concatenate `delta.content` values.

Stream termination is signaled by `data: [DONE]`.

#### Streaming Chunk Fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Unique chunk ID (shared across all chunks in one response) |
| `object` | string | Always `chat.completion.chunk` |
| `created` | int | Unix timestamp of the original request |
| `model` | string | Matches the requested model |
| `choices[].index` | int | Always 0 (single response) |
| `choices[].delta.role` | string | Emitted once in the first chunk |
| `choices[].delta.content` | string | Token or content fragment |
| `choices[].finish_reason` | string | `null` during streaming, `"stop"` on final chunk |

---

## Health Check

### `GET /v1/health`

Returns the current health status of the NeuroRoute service and its components.

#### Response (200 — healthy)

```json
{
  "status": "healthy",
  "version": "1.0.0+commit.hash",
  "uptime": "0.00:05:23",
  "components": {
    "npu": {
      "status": "healthy",
      "message": "ONNX session created, model loaded"
    },
    "gpu": {
      "status": "healthy",
      "message": "GPU endpoint reachable"
    }
  }
}
```

#### Response (200 — degraded)

```json
{
  "status": "degraded",
  "version": "1.0.0+commit.hash",
  "uptime": "0.01:12:34",
  "components": {
    "npu": {
      "status": "healthy",
      "message": "ONNX session created, model loaded"
    },
    "gpu": {
      "status": "unhealthy",
      "message": "Connection refused at http://localhost:8080"
    }
  }
}
```

#### Response (503 — unhealthy)

```json
{
  "status": "unhealthy",
  "version": "1.0.0+commit.hash",
  "uptime": "0.00:00:15",
  "components": {
    "npu": {
      "status": "unhealthy",
      "message": "Model file not found at Models/gemma-4-int4.onnx"
    },
    "gpu": {
      "status": "unhealthy",
      "message": "GPU health check timed out"
    }
  }
}
```

#### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `status` | string | `healthy` (all components OK), `degraded` (some components down), `unhealthy` (all down) |
| `version` | string | SemVer + commit hash from assembly metadata |
| `uptime` | string | Duration format: `days.hh:mm:ss` |
| `components` | object | Map of component name to health status |

#### Per-Component Status

`npu` status depends on the active backend (`NpuBackend` config):

| Backend | Status | Meaning |
|---------|--------|---------|
| onnx | healthy | ONNX session created and model loaded |
| onnx | unhealthy | Model file not found, session creation failed |
| flm | healthy | FLM process running, server responding to health checks |
| flm | unhealthy | FLM executable not found, process crashed, or health check timeout |
| gpu | healthy | GPU endpoint responds to `GET /v1/health` within 5s |
| gpu | unhealthy | GPU endpoint unreachable, timed out, or returned error |

---

## Metrics

### `GET /v1/metrics`

Returns aggregated routing metrics for the NeuroRoute service.

#### Response

```json
{
  "totalRequests": 1000,
  "npuHandled": 750,
  "gpuEscalated": 250,
  "streamingRequests": 300,
  "byTaskType": {
    "simple_chat": 400,
    "summarize": 100,
    "classify": 150,
    "code": 200,
    "deep_reasoning": 150
  },
  "byCase": {
    "case_A": 700,
    "case_B": 100,
    "case_C": 50,
    "case_D": 150
  },
  "durationMs": {
    "min": 10.5,
    "max": 2500.0
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `totalRequests` | long | Total requests processed since service start |
| `npuHandled` | long | Requests answered entirely on NPU |
| `gpuEscalated` | long | Requests escalated to GPU backend |
| `streamingRequests` | long | Requests with `stream: true` |
| `byTaskType` | object | Breakdown by NPU classification (simple_chat, summarize, classify, code, deep_reasoning) |
| `byCase` | object | Breakdown by routing case (A, B, C, D) |
| `durationMs.min` | double | Fastest request duration in the recent sample window |
| `durationMs.max` | double | Slowest request duration in the recent sample window |

Metrics reset on service restart.

---

---

## Admin — NPU Model Management

Available when `NpuBackend: "flm"`. These endpoints manage the FLM model lifecycle.

### `GET /v1/admin/npu`

Returns the current FLM runtime state as a `FlmStatus` record.

#### Response (200)

```json
{
  "status": "healthy",
  "message": "FLM server ready (PID: 12345)",
  "modelTag": "gemma4-it:e4b",
  "host": "127.0.0.1",
  "port": 52625,
  "ctxLen": 65536,
  "pmode": "performance",
  "pid": 12345,
  "startedAt": "2026-06-28T12:00:00Z"
}
```

#### Response (503)

When the FLM process manager is not available (mock mode):

```json
{
  "message": "FLM process manager not available (mock mode?)"
}
```

### `GET /v1/admin/npu/models`

Lists available FLM models from `flm list --json`.

#### Query Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `filter` | string | `installed` | Filter: `all`, `installed`, `not-installed` |

#### Response (200)

```json
[
  {
    "tag": "gemma4-it:e4b",
    "installed": true,
    "size": "4.2 GB",
    "quantization": "e4b",
    "description": "Gemma 4 Instruct 4-bit"
  }
]
```

### `POST /v1/admin/npu/pull`

Initiates an asynchronous model pull. Returns immediately — progress is logged to the NeuroRoute event log.

#### Request Body

```json
{
  "tag": "gemma4-it:e4b"
}
```

#### Response (202)

```json
{
  "message": "Pulling model 'gemma4-it:e4b' in background"
}
```

### `POST /v1/admin/npu/load`

Stops the current `flm serve` and restarts with a new model, context length, and/or power mode.

#### Request Body

```json
{
  "modelTag": "gemma4-it:e4b",
  "ctxLen": 65536,
  "pmode": "performance",
  "persist": false
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `modelTag` | string | (required) | Model tag to load |
| `ctxLen` | int | `0` | Context length (`0` = model default) |
| `pmode` | string | `performance` | Power mode: `powersaver`, `balanced`, `performance`, `turbo` |
| `persist` | bool | `false` | When `true`, saves ctxLen and pmode to `appsettings.json` |

#### Response (200)

```json
{
  "message": "Model 'gemma4-it:e4b' loaded"
}
```

### `DELETE /v1/admin/npu/models/{tag}`

Removes a model from the local cache via `flm remove <tag>`.

#### Response (200)

```json
{
  "message": "Model 'gemma4-it:e4b' removed"
}
```

### Client Examples (PowerShell)

```pwsh
# Get NPU status
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/npu

# List installed models
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/npu/models

# Load a new model
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/npu/load `
  -Method Post `
  -Body '{"modelTag":"gemma4-it:e4b","ctxLen":65536,"pmode":"performance","persist":true}' `
  -ContentType "application/json"

# Pull a model in background
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/npu/pull `
  -Method Post `
  -Body '{"tag":"gemma4-it:e4b"}' `
  -ContentType "application/json"

# Remove a model
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/npu/models/gemma4-it:e4b -Method Delete
```

---

## Admin — Mock Scenario Control

Available only when `UseMockBackends: true` (dev/test mode). These endpoints let you program the fake NPU and GPU backends without real hardware.

### `GET /v1/admin/mock/scenario`

Returns the current mock scenario state — all programmable parameters.

#### Response (200)

```json
{
  "npuAvailable": true,
  "npuBackend": "mock",
  "npuModel": "mock-npu-model-v1",
  "taskType": "simple_chat",
  "needsGpu": false,
  "routingCase": "C",
  "npuResponseText": "Hello from mock NPU!",
  "gpuResponseText": "Complex reasoning from mock GPU!",
  "gpuAvailable": true,
  "gpuModel": "mock-gpu-model-v1",
  "gpuEndpoint": "http://mock-gpu:8080",
  "simulatedLatencyMs": 50,
  "streamDelayMs": 10
}
```

### `POST /v1/admin/mock/scenario`

Partially updates the mock scenario. Only supplied fields are changed.

#### Request Body

```json
{
  "needsGpu": true,
  "gpuAvailable": true,
  "taskType": "deep_reasoning"
}
```

#### Response (200)

```json
{
  "message": "Mock scenario updated"
}
```

### `POST /v1/admin/mock/scenario/reset`

Resets all mock scenario values to factory defaults.

#### Response (200)

```json
{
  "message": "Mock scenario reset to defaults"
}
```

### Client Examples (PowerShell)

```pwsh
# Set NPU to always classify as complex (GPU escalation)
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/mock/scenario `
  -Method Post `
  -Body '{"needsGpu":true,"gpuAvailable":true}' `
  -ContentType "application/json"

# Verify from Dashboard
Start-Process "http://localhost:5001"

# Reset to defaults
Invoke-RestMethod -Uri http://localhost:5000/v1/admin/mock/scenario/reset -Method Post
```

---

## Errors

All errors return JSON with consistent structure:

```json
{
  "error": "description of what went wrong"
}
```

| HTTP Status | Common Causes |
|-------------|---------------|
| 400 | Invalid request body (malformed JSON, missing required fields) |
| 503 | All backends unhealthy (routing cannot proceed) |
| 500 | Internal service error (check service logs) |

---

## Client Examples

### cURL

```pwsh
curl.exe -X POST http://localhost:5000/v1/chat/completions `
  -H "Content-Type: application/json" `
  -d '{\"model\":\"neuro-route\",\"messages\":[{\"role\":\"user\",\"content\":\"Hello\"}],\"max_tokens\":32}'
```

### PowerShell

```pwsh
Invoke-RestMethod -Uri http://localhost:5000/v1/chat/completions `
  -Method Post `
  -Body '{"model":"neuro-route","messages":[{"role":"user","content":"Hello"}],"max_tokens":32}' `
  -ContentType "application/json"

# Streaming
Invoke-RestMethod -Uri http://localhost:5000/v1/chat/completions `
  -Method Post `
  -Body '{"model":"neuro-route","messages":[{"role":"user","content":"Hello"}],"stream":true,"max_tokens":32}' `
  -ContentType "application/json" | ForEach-Object { Write-Host $_.choices[0].delta.content -NoNewline }

# Health check
Invoke-RestMethod -Uri http://localhost:5000/v1/health
```

### Python

```python
import httpx

response = httpx.post(
    "http://localhost:5000/v1/chat/completions",
    json={
        "model": "neuro-route",
        "messages": [{"role": "user", "content": "Hello"}],
        "max_tokens": 32,
        "stream": True,
    },
)
for chunk in response.iter_lines():
    if chunk.startswith("data: ") and chunk != "data: [DONE]":
        print(chunk[6:])
```
