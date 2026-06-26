# NeuroRoute — Project Vision

## Why NeuroRoute?

Running large language models locally is expensive in RAM. A single 35B parameter model in BF16 consumes ~70 GB of VRAM. Most consumer hardware can't run it. Meanwhile, nearly every modern PC has an NPU (Neural Processing Unit) that sits idle.

NeuroRoute bridges this gap: use the NPU for lightweight, bounded tasks and the GPU only for heavy reasoning — drastically reducing hardware requirements for local LLM inference.

## Principles

- **NPU-first routing** — every request starts on NPU; GPU is a deliberate escalation
- **OpenAI-compatible API** — works with any existing LLM client library
- **Model-agnostic** — swap any ONNX-compatible NPU model and any OpenAI-compatible GPU backend via configuration
- **Low RAM** — idle <40 MB, NPU loaded <200 MB total system RAM
- **Windows-native** — ships as a Windows Service, single EXE deploy

## Features

| Feature | Status |
|---------|--------|
| POST /v1/chat/completions (JSON + SSE streaming) | ✅ Done |
| GET /v1/health (component-level health check) | ✅ Done |
| NPU classification with system prompt | ✅ Done |
| 4-case routing (A/B/C/D) | ✅ Done |
| Configurable context window (NpuLimit / NpuSlice) | ✅ Done |
| GPU auto-retry with exponential backoff | ✅ Done |
| ONNX GenAI backend | ✅ Done |
| FLM (FastFlowLM) backend | ✅ Done |
| Structured logging & tracing | 🔜 Planned |
| Metrics endpoint | 🔜 Planned |

## Model Support

| Role | Format | Constraint |
|------|--------|------------|
| NPU classifier | ONNX (via ONNX Runtime GenAI) | Any GenAI-compatible model |
| GPU reasoner | OpenAI-compatible HTTP API | Any server (LM Studio, Lemonade Server, Unsloth Studio, llama.cpp, vLLM, etc.) |

Both are configured in `appsettings.json` — no code changes needed to swap models.

## Future Extensions

- Multi-GPU routing (shard across GPUs by context window)
- Multi-NPU routing (multiple ONNX models for different classifiers)
- RAG integration (NPU-indexed retrieval augmented generation)
- Agent-aware routing (NPU plans agent steps, GPU executes)
- Memory-aware routing (NPU tracks conversation state, GPU handles working memory)
- Model caching (warm NPU/GPU models across requests)
