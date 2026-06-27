# NeuroRoute Architecture

## Overview

```
┌──────────────────────────────────────────────────────┐
│              NeuroRoute.Service                       │
│       (.NET 10 Windows Routing Gateway)               │
└───────────────────────┬──────────────────────────────┘
                        │
          ┌─────────────┼─────────────┐
          ▼             ▼             ▼
  ┌──────────────┐ ┌──────────┐ ┌──────────────┐
  │ ChatController│ │ Health   │ │AdminController│
  │ /v1/chat/     │ │Controller│ │ /v1/admin/*   │
  │ completions   │ │ /v1/health│ │ stop/restart   │
  │ SSE streaming │ └──────────┘ │ reload/logs   │
  └───────┬───────┘              │ mock/scenario  │
          │                      └───────────────┘
          ▼
    ┌──────────────┐
    │    Router     │
    │  orchestration│
    └──────┬───────┘
           │
 ┌─────────┼──────────┐
 ▼         ▼          ▼
[ITokenizer] [NpuPlanner] [IGpuClient]
 (counting)   (4 routing    ├── GpuClient (HTTP real)
               cases)       └── MockGpuClient (fake)
                │
                ▼
          ┌──────────────┐
          │   NpuModel    │
          │ → INpuBackend  │
          └──────┬───────┘
                 │
         ┌───────┼────────┐
         ▼       ▼        ▼
  ┌──────────┐ ┌──────┐ ┌──────────────────┐
  │ OnnxBack │ │ Flm  │ │ MockNpuBackend    │
  │ (ONNX)   │ │(HTTP)│ │ (programmable)    │
  └──────────┘ └──────┘ └──────────────────┘
```

Backend selection is driven by `NeuroRoute:NpuBackend` in `appsettings.json` (`"onnx"` or `"flm"`).

## Routing Logic

```
NPU_LIMIT = configurable (default 65536 tokens)
NPU_SLICE = configurable (default 2048 tokens)

Case A: fullTokens ≤ NPU_LIMIT
  → NPU sees full prompt, classifies:
    simple_chat/summarize/classify → NPU answers
    code/deep_reasoning/needs_gpu  → GPU answers

Case B: fullTokens > NPU_LIMIT
  → NPU sees only last NPU_SLICE tokens
  → NPU MUST NOT answer
  → GPU receives full prompt

Case C: NPU returns compressed_prompt
  → GPU receives compressed_prompt instead of full

Case D: NPU returns notes_for_gpu
  → GPU prompt = notes_for_gpu + fullPrompt
```

## Component Responsibilities

| Component | File | Responsibility |
|-----------|------|----------------|
| ChatController | `Controllers/ChatController.cs` | HTTP endpoint, SSE vs JSON dispatch |
| HealthController | `Controllers/HealthController.cs` | Health check endpoint, component status |
| HealthService | `Services/HealthService.cs` | Aggregates NPU/GPU health status |
| Router | `Routing/Router.cs` | Orchestrator — classify, decide, execute |
| NpuPlanner | `Routing/NpuPlanner.cs` | Token counting, routing case logic |
| ITokenizer | `Routing/ITokenizer.cs` | Token counting abstraction |
| ApproximateTokenizer | `Routing/Tokenizer.cs` | Dev-only fast token counter |
| PromptBuilder | `Routing/PromptBuilder.cs` | Chat template formatting |
| NpuModel | `Npu/NpuModel.cs` | NPU inference dispatcher — delegates to INpuBackend |
| INpuBackend | `Npu/INpuBackend.cs` | Abstraction over ONNX, FLM, and Mock inference |
| OnnxBackend | `Npu/OnnxBackend.cs` | Direct ONNX GenAI inference |
| OnnxSessionFactory | `Npu/OnnxSessionFactory.cs` | Thread-safe ONNX session lifecycle |
| FlmBackend | `Npu/FlmBackend.cs` | HTTP client to FastFlowLM server on port 52625 |
| FlmClient | `Npu/FlmClient.cs` | OpenAI-compatible HTTP client for FLM server |
| FlmProcessManager | `Npu/FlmProcessManager.cs` | FLM child process lifecycle (start, health, restart) |
| IGpuClient | `Gpu/IGpuClient.cs` | Abstraction over GPU HTTP client and mock |
| GpuClient | `Gpu/GpuClient.cs` | Implements IGpuClient; HTTP client to external GPU server with auto-retry |
| MockNpuBackend | `Testing/MockNpuBackend.cs` | Programmable fake NPU for dev/test (no hardware) |
| MockGpuClient | `Testing/MockGpuClient.cs` | Programmable fake GPU for dev/test (no hardware) |
| MockScenario | `Testing/MockScenario.cs` | Singleton state controlling mock behavior |
| Worker | `Worker.cs` | Service lifecycle, FLM process startup |

## Data Flow (Non-Streaming)

1. `ChatController.CreateCompletion()` receives JSON body
2. `Router.RouteAsync()` calls `NpuPlanner.CreatePlanAsync()` with classify function
3. Planner counts tokens, runs NPU classification via `NpuModel.ClassifyAsync()`
4. If plan says `needs_gpu=false` → `NpuModel.GenerateAsync()`
5. If plan says `needs_gpu=true` → `GpuClient.SendAsync()` with optional compression
6. Response returned as OpenAI-compatible JSON

## Data Flow (Streaming)

Same as above, but:
- Router yields `ChatCompletionChunk` via `IAsyncEnumerable`
- Controller writes SSE `data: {...}\n\n` chunks with `data: [DONE]` termination

## Health Check

`GET /v1/health` returns:
- Overall status: `healthy`, `degraded`, or `unhealthy`
- Per-component status for NPU (ONNX or FLM backend) and GPU (endpoint reachability)
- Service version and uptime
- 200 for healthy/degraded, 503 for unhealthy

## Model Independence

- NPU backend: selectable via `NeuroRoute:NpuBackend`
  - ONNX: any GenAI-compatible `.onnx` model file
  - FLM: any FastFlowLM model tag via `NeuroRoute:NpuFlmModelTag`
- GPU server: any OpenAI-compatible HTTP API, configured via `GpuClient` base address
- No hardcoded model names in code — all model references are in `appsettings.json`

## Planned Extensions

- Multi-GPU routing (shard across GPUs by context window)
- RAG integration (NPU-indexed retrieval augmented generation)
- Structured logging & metrics
