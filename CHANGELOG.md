# Changelog

All notable changes to this project will be documented in this file.

## [1.0.0] - 2026-06-25

### Added
- OpenAI-compatible chat completions endpoint (`POST /v1/chat/completions`)
- SSE streaming support with `data: [DONE]` termination
- NPU classification with configurable system prompt
- 4-case routing engine (A/B/C/D) based on token count and NPU plan
- ONNX GenAI backend via `OnnxSessionFactory` (thread-safe)
- GPU client with 3-attempt auto-retry and exponential backoff
- Component-level health endpoint (`GET /v1/health`)
- Windows Service integration via `Microsoft.Extensions.Hosting.WindowsServices`
- Configurable context window via `NeuroRouteOptions` (NpuLimit / NpuSlice)
- ONNX backend via `OnnxBackend : INpuBackend`
- FLM backend via `FlmBackend`, `FlmClient`, `FlmProcessManager` (FastFlowLM AMD NPU)
- Backend selection via `NpuBackend` config key (`"onnx"` or `"flm"`)
- Approximate tokenizer for dev/test use
- Model-agnostic configuration via `appsettings.json`
- Full test suite with 9 passing tests (Router, Planner, Tokenizer, PromptBuilder)

### Documentation
- Project vision and roadmap (`VISION.md`)
- System architecture and data flow (`ARCHITECTURE.md`)
- Full specification, config reference, routing rules (`SPECIFICATION.md`)
- Semantic Model Cascade pattern explanation (`CONCEPTS.md`)
- Windows deployment guide (`DEPLOYMENT.md`)
- FastFlowLM integration plan (`FLM_INTEGRATION.md`)
- API reference (`API.md`)

## [Planned] - 1.1.0

### Planned
- FLM backend (FastFlowLM AMD NPU support)
- Structured logging and tracing (`System.Diagnostics.Activity`)
- Metrics endpoint with token counts, routing ratios, latency percentiles
- Integration/E2E test project
