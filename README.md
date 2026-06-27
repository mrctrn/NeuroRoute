# NeuroRoute

**Hybrid NPU→GPU Routing Gateway for Local LLM Execution**

NeuroRoute is a .NET 10 Windows Service that bridges Neural Processing Units (NPU) with traditional GPU backends for efficient local LLM inference. Every request starts on the NPU — GPU escalation is a deliberate, content-aware decision.

## How It Works

```
User Request → Token Counter → NPU Classifier → [NPU generates | GPU escalates]
```

1. **Count** tokens (Layer 0 — free)
2. **Classify** the request on NPU (Layer 1 — ~10–50ms, <1W)
3. **Generate** on NPU for simple tasks (Layer 2), or **escalate** to GPU for complex tasks (Layer 3)

## Features

- **OpenAI-compatible API** — use any existing LLM client library
- **SSE streaming** — real-time token-by-token output
- **4-case routing** — handles short/long prompts, compression, and notes
- **Configurable context window** — NpuLimit up to 128k tokens
- **Health monitoring** — component-level `/v1/health` endpoint
- **Metrics endpoint** — routing ratios, task type distribution, latency
- **Admin endpoints** — stop, restart backend, reload config, view logs
- **Blazor Dashboard** — live health + metrics web UI (5s auto-refresh)
- **System Tray app** — WinForms NotifyIcon with health status + actions
- **Mock backends** — run without real NPU/GPU for dev and testing
- **Windows Service** — self-contained EXE, installs via `sc.exe`
- **Model-agnostic** — swap models in `appsettings.json`, no code changes

## Quickstart

```pwsh
git clone <repo-url>
cd NeuroRoute

# Build
dotnet publish .\NeuroRoute.Service\NeuroRoute.Service.csproj `
  -c Release -r win-x64 --self-contained -o .\publish

# Configure
# Edit publish\appsettings.json (NPU model path, GPU endpoint, etc.)

# Install as service
sc.exe create NeuroRoute binPath="C:\full\path\to\publish\NeuroRoute.Service.exe" start=auto
sc.exe start NeuroRoute

# Test
Invoke-RestMethod -Uri http://localhost:5000/v1/chat/completions `
  -Method Post `
  -Body '{"model":"neuro-route","messages":[{"role":"user","content":"Hello"}],"max_tokens":32}' `
  -ContentType "application/json"
```

## Documentation

| Doc | Description |
|-----|-------------|
| [VISION.md](docs/VISION.md) | Project goals, principles, and roadmap |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | System architecture and data flow |
| [SPECIFICATION.md](docs/SPECIFICATION.md) | Full API spec, config, routing rules |
| [API.md](docs/API.md) | Detailed API reference |
| [CONCEPTS.md](docs/CONCEPTS.md) | Semantic Model Cascade pattern explained |
| [DEPLOYMENT.md](docs/DEPLOYMENT.md) | Windows Service install, config, troubleshooting |
| [FLM_INTEGRATION.md](docs/FLM_INTEGRATION.md) | FastFlowLM backend reference (AMD NPU) |
| [DAPR_ANALYSIS.md](docs/DAPR_ANALYSIS.md) | Dapr AI integration analysis (pro/contra/flow) |
| [MOCK_DEVELOPMENT.md](docs/MOCK_DEVELOPMENT.md) | Mock backends & Playwright testing design |

## License

MIT
