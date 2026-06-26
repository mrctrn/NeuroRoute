# NeuroRoute — Concepts & Patterns

## The Pattern: Semantic Model Cascading

NeuroRoute implements a **Semantic Model Cascade** — a layered inference architecture where progressively more expensive models are only invoked when necessary.

### Layers

```
Layer 0: Lightweight Token Counter (ApproximateTokenizer)
  Cost: ~0 ms, zero memory
  Job: Estimate token length, determine NPU context fit

Layer 1: NPU Classifier (small on-device model)
  Cost: 10–50 ms, ~50 MB (on NPU)
  Job: Classify task type, decide if GPU escalation is needed

Layer 2: NPU Generator (same small model)
  Cost: depends on output length
  Job: Generate short answers for simple tasks

Layer 3: GPU Model (large remote model)
  Cost: high (VRAM, latency, power)
  Job: Full generation for complex/long tasks
```

Each layer acts as a **guard**: if the current layer can handle the request, deeper layers are never invoked. This yields the best latency/power tradeoff per request.

### Why this works

| Request type | Layers used | Latency | Power |
|-------------|-------------|---------|-------|
| "Hello" | 0 → 1 → 2 (NPU only) | ~50 ms | < 1 W |
| "Write a sorting algorithm in Rust" | 0 → 1 → 3 (GPU) | seconds | high |
| "Summarize this 50-page document" | 0 → 3 (GPU, truncation rule) | seconds | high |

The NPU classifier (Layer 1) isn't free, but it is _so much cheaper_ than GPU inference that the overhead is negligible — even when every request ends up on GPU.

---

## Related Patterns

### AI Gateway / LLM Gateway
Standard network-level routing (e.g., Portkey, ML Gateway, OpenRouter) — routes by API key, rate limit, or model name. NeuroRoute differs by routing based on _content semantics_, not just request metadata.

### Mixture of Agents (MoA)
Multiple models produce and critique answers. NeuroRoute does **a single inference pass** — no back-and-forth between models.

### Speculative Decoding
Small model proposes tokens, large model verifies. NeuroRoute routes at the _request level_, not the token level.

### Cascade / Tiered Inference
Common in cost optimization — start cheap, escalate when confidence is low. NeuroRoute is a concrete implementation of this with NPU-first routing.

---

## The Classification Prompt

Layer 1 (NPU Classifier) uses a carefully designed system prompt that defines:

1. **Task taxonomy** — what types of requests exist (`simple_chat`, `summarize`, `classify`, `code`, `deep_reasoning`)
2. **Escalation rules** — when the NPU must pass to GPU (coding, long context, multi-step reasoning)
3. **Output schema** — strict JSON format including optional `compressed_prompt` and `notes_for_gpu`

The prompt acts as the _contract_ between the NPU model and the Router. It must be precise because:
- False positive (NPU answers when it shouldn't) → incorrect responses
- False negative (passes to GPU when NPU could handle it) → wasted power/latency

### Routing Rules Summary

```
                 ┌──────────────────────┐
                 │   User sends prompt   │
                 └──────────┬───────────┘
                            │
                    ┌───────▼────────┐
                    │ Count tokens    │
                    │ (Layer 0)       │
                    └───┬────────┬───┘
                        │        │
           ≤NpuLimit  │        │ >NpuLimit
                        │        │
                ┌───────▼──┐   ┌─▼──────────┐
                │ Classify │   │ Truncate   │
                │ (Layer 1)│   │ to NpuSlice│
                └───┬──┬───┘   │ & classify │
                    │  │       └───┬────────┘
              needs │  │ no       │
              GPU?  │  │          │ always needs_gpu=true
                    │  │          │
                    ▼  └──┐       ▼
              ┌────────┐  │ ┌──────────┐
              │ GPU     │  │ │ NPU      │
              │ (L3)    │  │ │ answers  │
              └────────┘  │ │ (L2)     │
                          │ └──────────┘
                          │
                    ┌─────▼──────┐
                    │ Optional   │
                    │ compressed │
                    │ prompt /   │
                    │ notes      │
                    └────────────┘
```

### Compression & Notes

The NPU classifier can output two optional fields that modify GPU behavior:

- **`compressed_prompt`** — a shortened version of the user's prompt. GPU receives this instead of the full text. Useful when the NPU can distill a long query into its essence.

- **`notes_for_gpu`** — instructions or hints prepended to the GPU prompt. Example: "The user is asking about Python. Focus on code examples." This transfers NPU understanding to the GPU without the GPU having to re-classify.

These fields implement **cross-tier context enrichment** — information extracted at the NPU tier flows downstream to the GPU tier, reducing redundant computation.

---

## Configurable Context Window

The `NpuLimit` (default 65536) and `NpuSlice` (default 2048) are read from `appsettings.json` at startup via the typed `NeuroRouteOptions` class.

### Selecting the right NpuLimit

Set `NpuLimit` to match the NPU model's max context window. Examples:

| Model | Max Context | Recommended NpuLimit |
|-------|-------------|---------------------|
| Gemma 4 E2B/E4B via FLM | 128k | 65536 |
| Gemma 3 4B via FLM | 128k | 65536 |
| Gemma 3 1B via FLM | 32k | 32768 |
| Any ONNX GenAI model | varies | model-specific |

### Auto-Detection (FLM backend)

When the FLM backend is active, `FlmClient` queries `/v1/models` on startup and attempts to extract `max_context_length` from the response. If detected, this value can override the configured `NpuLimit`. If the server does not report context length, the configured value is used as fallback.

For the ONNX backend, context window must always be configured manually (no standard auto-detection mechanism for model files).
