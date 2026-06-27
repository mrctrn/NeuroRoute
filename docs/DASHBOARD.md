# NeuroRoute Dashboard

The NeuroRoute Dashboard is a Blazor Server web application that provides real-time status monitoring, metrics visualization, and administration controls for the NeuroRoute routing gateway.

## Quick Start

```pwsh
# Terminal 1: Service (mock mode, no hardware needed)
$env:NeuroRoute__UseMockBackends = "true"
dotnet run --project NeuroRoute.Service

# Terminal 2: Dashboard
dotnet run --project NeuroRoute.Dashboard --urls http://localhost:5001
```

Open `http://localhost:5001` in your browser.

See [Dev Mode](DEPLOYMENT.md#9-dev-mode--no-hardware-required) for more options.

## Pages

### Dashboard (`/`)

The main overview page, auto-refreshing every 5 seconds.

```
┌──────────────────────────────────────────────────────────────────┐
│  ● healthy     v0.1.0     Uptime: 1:23:45                      │
│  Overall service status — color reflects health state            │
├─────────────────────────┬────────────────────────────────────────┤
│  NPU Backend            │  GPU Backend                           │
│  ● Running              │  ● Available                           │
│  Backend: onnx/flm/mock │  Model: gemma-4-8b                     │
│  Model: gemma-4-int4    │  Endpoint: http://localhost:8080       │
│                         │                                        │
│  (Green pulse = alive)  │  (Red static = down)                   │
├─────────────────────────┴────────────────────────────────────────┤
│  Routing Ratio (donut)  │  Latency (bar)      │  Streaming Ratio │
│  NPU: 75%  GPU: 25%    │  Min 42ms / Max 3.2s│  Stream: 80%     │
├─────────────────────────┼────────────────────────────────────────┤
│  Case Distribution (pie)│  Task Types (table)                     │
│  Case A ██ 15%          │  simple_chat        │ 128              │
│  Case B ████ 30%        │  code_gen           │ 42               │
│  Case C ██████ 55%      │  summarization      │ 15               │
│  Case D █ 2%            │  translation        │ 7                │
├─────────────────────────┴────────────────────────────────────────┤
│  Administration                                                  │
│  [Restart NPU] [Reload Config] [Stop Service]                   │
│  Each action shows a confirmation dialog before executing.       │
│  A toast notification reports success or failure.                 │
└──────────────────────────────────────────────────────────────────┘
```

#### Sections

| Section | Content | Behavior |
|---------|---------|----------|
| **Status Banner** | Health status, version, uptime | Green = all healthy, Yellow = degraded (one component down), Red = unhealthy (both down or unreachable) |
| **NPU Card** | Backend type, model name, availability | Green pulse animation when alive, red static when down. Shows model loaded badge. |
| **GPU Card** | Model name, endpoint URL, availability | Same pulse indicator. Shows "Model loaded" or "No model loaded". |
| **Routing Ratio** | Donut chart | NPU handled vs GPU escalated. Only visible after at least one request. |
| **Latency** | Bar chart | Min and max request duration in milliseconds. |
| **Streaming Ratio** | Donut chart | Streaming vs non-streaming request distribution. |
| **Case Distribution** | Pie chart | Routing case breakdown (A, B, C, D). See [Routing Rules](SPECIFICATION.md#4-routing-rules). |
| **Task Types** | Table | Count per task type, sorted descending. |
| **Admin Panel** | Action buttons | Each triggers a confirmation dialog, then calls the admin API. |

#### Empty States

- **No requests yet**: Chart sections show "Awaiting data..." until the first request is processed.
- **Service unreachable**: Red banner with "Cannot reach NeuroRoute service at localhost:5000".
- **Component down**: The affected card shows red status with the error message.

### Logs (`/logs`)

View recent admin log entries with search and level filtering.

| Feature | Description |
|---------|-------------|
| **Search** | Filter logs by message text (client-side) |
| **Level filter** | Show All, Info, Warning, or Error entries |
| **Auto-refresh** | Toggle to automatically refresh every 5 seconds |
| **Level badges** | Color-coded MudChip: Info (blue), Warning (yellow), Error (red) |

### Dark Mode

Toggle dark/light theme from the moon/sun icon in the app bar. Theme changes immediately and persists for the session.

## Status Indicators

### Health States

| State | Color | Meaning |
|-------|-------|---------|
| `healthy` | Green | All components responding, models loaded |
| `degraded` | Yellow | One backend down or unreachable |
| `unhealthy` | Red | All backends down, or service unreachable |

### Component Pulse Dots

Each NPU/GPU card has a circular indicator:
- **Green pulsing** — component is alive and reporting healthy
- **Red static** — component is down or unreachable

The pulse animation signals active health monitoring (not a static snapshot).

## Charts

All charts use MudBlazor's SVG chart components — no JavaScript dependencies.

| Chart | Type | Data | Note |
|-------|------|------|------|
| Routing Ratio | Donut | `NpuHandled` vs `GpuEscalated` | Shows what fraction of requests stayed on NPU |
| Latency | Bar | `DurationMs.Min`, `DurationMs.Max` | Full request round-trip time |
| Streaming Ratio | Donut | `StreamingRequests` vs `TotalRequests` | SSE vs JSON response distribution |
| Case Distribution | Pie | `ByCase` (A/B/C/D) | Which routing case was triggered |
| Task Types | Table | `ByTaskType` dictionary | Raw counts, sorted by volume |

Charts appear only after at least one request has been processed. Before that, they show an "Awaiting data" empty state.

## Administration

### Actions

| Button | API Call | Effect |
|--------|----------|--------|
| **Restart NPU Backend** | `POST /v1/admin/restart-backend` | Re-initializes NPU backend (reloads model, restarts FLM process if applicable) |
| **Reload Configuration** | `POST /v1/admin/reload-config` | Hot-reloads `appsettings.json` without restarting the service |
| **Stop Service** | `POST /v1/admin/stop` | Graceful shutdown of the NeuroRoute service |

Each action:
1. Shows a confirmation dialog ("Are you sure you want to restart the NPU backend?")
2. Calls the admin API
3. Shows a success/failure toast notification
4. Refreshes the dashboard

### Confirmation Dialogs

| Action | Dialog Message |
|--------|---------------|
| Restart NPU | "Are you sure you want to restart the NPU backend? This will interrupt any running NPU requests." |
| Reload Config | "Are you sure you want to reload the configuration? Some changes may require a full restart." |
| Stop Service | "Are you sure you want to stop the NeuroRoute service? The dashboard and API will become unavailable." |

## Configuration

The Dashboard reads from `appsettings.json` in `NeuroRoute.Dashboard`:

```json
{
  "NeuroRouteApi": {
    "BaseUrl": "http://localhost:5000",
    "TimeoutSeconds": 5
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `BaseUrl` | `http://localhost:5000` | NeuroRoute Service URL |
| `TimeoutSeconds` | `5` | HTTP timeout for API calls |

## Architecture

```
Browser (user)
    │ WebSocket (SignalR)
    ▼
NeuroRoute Dashboard (Blazor Server) — :5001
    │ HTTP
    ▼
NeuroRoute Service (Kestrel) — :5000
    │
    ├── /v1/health        → HealthService + MockScenario (optional)
    ├── /v1/metrics       → MetricsService (in-memory counters)
    ├── /v1/admin/logs    → AdminController (recent log entries)
    ├── /v1/admin/restart-backend
    ├── /v1/admin/reload-config
    └── /v1/admin/stop
```

The Dashboard polls the Service every 5 seconds. No database, no persistent storage. All state is in-memory on the Service side.

## Technology Stack

| Component | Technology |
|-----------|------------|
| UI Framework | Blazor Server (InteractiveServer render mode) |
| Component Library | [MudBlazor](https://mudblazor.com) v9.x — Material Design |
| Charts | MudBlazor `MudChart` (SVG, no JS) |
| HTTP Client | `IHttpClientFactory` + typed `NeuroRouteApiClient` |
| Real-time | SignalR (Blazor Server circuit) |

## Troubleshooting

### Dashboard shows "Cannot reach NeuroRoute service"

1. Ensure the Service is running: `dotnet run --project NeuroRoute.Service`
2. Check the port: the Service defaults to `:5000`
3. If using a different port, update `appsettings.json` in `NeuroRoute.Dashboard`
4. Check for port conflicts: `netstat -ano | findstr :5000`

### Charts show "Awaiting data" even with requests

Metrics are collected in-memory and reset when the service restarts. Send at least one request to `POST /v1/chat/completions` to populate metrics.

### Dark mode doesn't persist after refresh

Dark mode is session-only by design. Future versions may persist the preference in `localStorage`.

### Logs page is empty

- Ensure the Service is running and accessible
- The `/v1/admin/logs` endpoint returns recent entries only (in-memory buffer)
- If the service was just started, there may be no entries yet
