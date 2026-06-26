# NeuroRoute System Tray & Dashboard Plan

> Design document for the NeuroRoute.Tray system tray application and related service enhancements.

## 1. Architecture

### Two-Process Split

```
Session 0 (Windows Service):
  NeuroRoute.Service.exe          ← existing, unchanged architecture
  ├── Kestrel (localhost:5000)
  │   ├── POST /v1/chat/completions
  │   ├── GET  /v1/health
  │   ├── GET  /v1/metrics
  │   └── POST /v1/admin/*        ← new admin endpoints
  └── NPU Backend (ONNX/FLM)

User Session (Desktop):
  NeuroRoute.Tray.exe             ← new WinForms app
  ├── NotifyIcon (system tray)
  ├── ContextMenuStrip (right-click menu)
  ├── Timer (poll /v1/health every 5s)
  └── HttpClient → localhost:5000
```

### Key Properties

| Property | Detail |
|----------|--------|
| No visible window | Tray starts hidden (WindowState=Minimized, ShowInTaskbar=false) |
| Auto-start | Adds itself to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` on first launch |
| Health polling | `GET /v1/health` every 5s → icon color reflects overall status |
| IPC | Existing REST API on localhost:5000 — no new protocol |
| Exit behavior | Closing tray does NOT stop the service — only removes the icon |

---

## 2. Context Menu

```
┌──────────────────────────────────────┐
│ Open Dashboard                 Ctrl+D│   → http://localhost:5000 (Blazor UI)
│ Open GPU Backend GUI                 │   → http://localhost:1234 (LM Studio) or configurable
│──────────────────────────────────────│
│ Status: ● Healthy                    │   ← live from /v1/health
│ NPU: ● Online (ONNX — gemma-4)      │   │
│ GPU: ● Online (LM Studio — llama-3) │   ┘ per-component detail
│──────────────────────────────────────│
│ Restart NPU Backend                  │   → POST /v1/admin/restart-backend
│ Reload Configuration                 │   → POST /v1/admin/reload-config
│──────────────────────────────────────│
│ View Logs                            │   → open Event Viewer or log file
│──────────────────────────────────────│
│ Stop Service                         │   → POST /v1/admin/stop
│ Exit NeuroRoute Tray                 │   → close tray (service unaffected)
└──────────────────────────────────────┘
```

### New fields on /v1/health

Current response:
```json
{
  "status": "healthy",
  "components": {
    "npu": { "status": "healthy" },
    "gpu": { "status": "healthy" }
  }
}
```

Proposed expanded response:
```json
{
  "status": "healthy",
  "uptime": "2h 15m 30s",
  "version": "1.0.0.0",
  "components": {
    "npu": {
      "status": "healthy",
      "backend": "onnx",
      "model": "gemma-4-int4",
      "model_loaded": true
    },
    "gpu": {
      "status": "healthy",
      "endpoint": "http://localhost:8080",
      "backend_type": "lm-studio",
      "model": "llama-3.1-8b-instruct",
      "model_loaded": true
    }
  }
}
```

This gives the tray enough info to show detailed status lines, not just a traffic light.

### Getting GPU backend metadata

`GET /v1/health` is the aggregation point. The service currently calls `GET /v1/models` on the GPU backend. We enhance the GPU health check to:
1. `GET /v1/models` — returns available models (standard OpenAI)
2. Parse the model ID from the response
3. Optionally: make a tiny test request (1 token) to confirm the model is actually loaded and warm

Result is cached in `HealthService` for 30s — no per-request overhead.

### GUI backend URL

The "Open GPU Backend GUI" menu item opens the GPU backend's own UI in a browser. This is configurable:

```json
{
  "NeuroRoute": {
    "GpuGuiUrl": "http://localhost:1234"
  }
}
```

Defaults:
- LM Studio: `http://localhost:1234`
- Can be overridden via `NeuroRoute:GpuGuiUrl` in `appsettings.json`

The health response also reports the model name detected from the backend. Example: `qwen3.6-35b-a3b-mtp` loaded in LM Studio.

---

## 3. Icon States

| State | Icon | Condition |
|-------|------|-----------|
| Healthy | 🟢 Green | Both NPU + GPU responding, models loaded |
| Degraded | 🟡 Yellow | One backend down or model not loaded |
| Unhealthy | 🔴 Red | Both down, or service unreachable |
| Stopped | ⚪ Gray | Service killed / not responding |

Icon is a single `.ico` file with multiple color variants (generated at build time or swapped at runtime).

---

## 4. New Admin Endpoints

All added to `NeuroRoute.Service`:

| Endpoint | Method | Action |
|----------|--------|--------|
| `/v1/admin/stop` | POST | Graceful shutdown — stops FLM, flushes metrics, calls `StopApplication()` |
| `/v1/admin/restart-backend` | POST | Re-initializes the NPU backend (reload model, restart FLM process) |
| `/v1/admin/reload-config` | POST | Hot-reload `appsettings.json` without restarting the service |
| `/v1/admin/logs` | GET | Returns recent log entries as JSON (tail of EventLog or file) |

Admin endpoints bound to localhost only. Optional `X-NeuroRoute-Admin-Key` header check (see §7 Security).

---

## 5. Tray App Implementation

**Project:** `NeuroRoute.Tray` — .NET 10 WinForms app targeting `net10.0-windows`

**Key files:**
- `Program.cs` — entry point, starts hidden, creates NotifyIcon
- `TrayContext.cs` — manages NotifyIcon, context menu, icon swapping
- `HealthPoller.cs` — background timer polling `/v1/health`
- `ServiceClient.cs` — typed HttpClient wrapper for all API calls
- `appsettings.json` — tray-specific settings (poll interval, GpuGuiUrl, admin key)

**Behavior:**
- Starts minimized, no taskbar entry
- Creates NotifyIcon with context menu
- On first launch: offers to add to Startup (`HKCU\...\Run`)
- Tooltip: `"NeuroRoute — ● Healthy"` (updated with status)
- Double-click: opens Dashboard in browser
- Left-click: could show status popup (future)

---

## 6. Implementation Phases

### Phase 1: Service enhancements
1. Expand `GET /v1/health` response with detailed component info
2. Add GPU model detection via `GET /v1/models` parsing
3. Add `/v1/admin/stop` endpoint
4. Add `/v1/admin/restart-backend` endpoint
5. Add `/v1/admin/reload-config` endpoint

### Phase 2: Tray application
1. Scaffold `NeuroRoute.Tray` WinForms project
2. Implement `NotifyIcon` + `ContextMenuStrip` with all items
3. Implement `HealthPoller` with icon state switching
4. Implement `ServiceClient` for all API calls
5. Wire up each menu action (open browser, call admin API, etc.)
6. Add auto-start option
7. Handle "service not running" state gracefully

### Phase 3: Dashboard (Blazor) enhancement
1. Surface GPU backend status on the existing dashboard page
2. Show loaded model names for both NPU and GPU
3. Add admin actions (restart backend, reload config buttons)

---

## 7. Security

- Admin endpoints are bound to localhost only (not exposed to network)
- **Optional admin key**: `NeuroRoute:AdminKey` in `appsettings.json`
  - If unset/empty — no auth required (default, your use case)
  - If set — `X-NeuroRoute-Admin-Key` header must match
- Tray reads key from its own config and sends it on admin calls when configured
- Future: add optional mTLS for Tailscale/WireGuard networks

---

## 8. Non-Goals (for this iteration)

- No named pipe IPC — HTTP is sufficient for polling and admin actions
- No real-time push / WebSocket — polling every 5s is acceptable for status
- No custom-drawn menus — WinForms native ContextMenuStrip follows Windows theme
- No embedded WebView in tray — dashboard opens in browser
