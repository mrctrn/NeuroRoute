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

## [1.1.0] - 2026-06-26

### Added
- Mock backend system (`UseMockBackends` config flag)
- `MockNpuBackend : INpuBackend` — programmable fake NPU
- `MockGpuClient : IGpuClient` — programmable fake GPU
- `MockScenario` singleton — full state control via admin endpoints
- `IGpuClient` interface extraction for mockability
- Admin mock scenario endpoints (`GET/POST /v1/admin/mock/scenario`)
- `appsettings.Development.json` with dev mode defaults

### Testing
- `NeuroRoute.Tests.Integration` — Playwright E2E project
- Dashboard health card state tests (green/yellow/red)
- Dashboard admin button tests (restart, reload)
- Dashboard metrics display tests
- `PlaywrightFixture` — manages Service + Dashboard processes for E2E

### Documentation
- Mock backend design doc (`docs/superpowers/specs/2026-06-26-mock-backend-integration-testing-design.md`)
- Dev mode guide in deployment docs
- Admin mock endpoint reference in API docs
