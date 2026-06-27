# MudBlazor Dashboard Redesign

**Date:** 2026-06-27
**Status:** Draft
**Approach:** MudBlazor component library, Material Design, Blazor Server (InteractiveServer)

## 1. Goal

Replace the current Bootstrap 5 dashboard with a professional, themeable Blazor dashboard using MudBlazor. Provide:

- **Live health monitoring** — component status with animated indicators
- **Metrics visualization** — charts for routing ratio, latency, case distribution
- **Dark/light theme** — user-toggleable, persisted in local storage
- **Admin controls** — restart backend, reload config, stop service with confirmation dialogs
- **Log viewer** — searchable, filterable admin log page
- **Responsive layout** — sidebar navigation, app bar, main content area

Zero server-side changes. All work is confined to `NeuroRoute.Dashboard`.

## 2. Architecture

```
Current:
┌─────────────────────────────────────────────┐
│  App.razor (Bootstrap CSS CDN)              │
│  MainLayout.razor (Bootstrap grid + sidebar)│
│  NavMenu.razor (Bootstrap nav)              │
│  Home.razor (Bootstrap cards + alerts)      │
│  wwwroot/app.css (custom bootstrap overrides)│
└─────────────────────────────────────────────┘

Target:
┌─────────────────────────────────────────────┐
│  App.razor (MudBlazor CSS + JS)             │
│  MudThemeProvider                            │
│  MainLayout.razor (MudLayout + MudDrawer)   │
│  NavMenu.razor (MudNavMenu)                 │
│  Home.razor (MudCard + MudChart + MudTable) │
│  Logs.razor (MudTable + MudTextField)       │
│  wwwroot/app.css REMOVED                    │
│  wwwroot/lib/bootstrap/ REMOVED             │
└─────────────────────────────────────────────┘
```

### Data Flow (unchanged from current)

```
NeuroRouteApiClient ─GET→ /v1/health ──→ HealthStatus? ──→ StatusBanner + ComponentCard
                    ─GET→ /v1/metrics ─→ MetricsSnapshot? ──→ 5 chart components + TaskTypeTable
                    ─GET→ /v1/admin/logs ─→ List<AdminLogEntry> ─→ Logs table
                    ─POST→ /v1/admin/restart-backend ──→ MudSnackbar "success" or "failed"
                    ─POST→ /v1/admin/reload-config ──→ MudSnackbar "success" or "failed"
                    ─POST→ /v1/admin/stop ──→ MudSnackbar "service stopping"
```

### Theme Configuration

```csharp
new MudTheme
{
    PaletteLight = new PaletteLight
    {
        Primary = "#3F51B5",       // Indigo
        Secondary = "#FF4081",     // Pink accent
        AppbarBackground = "#303F9F", // Darker indigo
        DrawerBackground = "#1A237E"  // Deep indigo
    },
    PaletteDark = new PaletteDark
    {
        Primary = "#7986CB",
        Secondary = "#FF80AB",
        AppbarBackground = "#121212",
        DrawerBackground = "#1E1E1E"
    },
    Typography = new Typography
    {
        Default = new Default
        {
            FontFamily = ["Segoe UI", "Helvetica Neue", "Arial", "sans-serif"]
        }
    }
}
```

## 3. Page Structure

### Navigation

```
┌──────────────────────┐
│  NeuroRoute          │  ← app bar title
├──────────────────────┤
│  █ Dashboard         │  ← active page
│  ⊕ Logs              │
├──────────────────────┤
│  🌙 Dark mode        │  ← toggle in drawer footer
└──────────────────────┘
```

### Dashboard Page Layout — `/`

```
▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀
  ● healthy     v0.1.0     Uptime: 1:23:45     [🌙]
  MudAlert with Icon, color reacts to status

┌────────── NPU Backend ──────────┐ ┌────────── GPU Backend ──────────┐
│  ● ○○                           │ │  ● ○○                          │
│  Mock NPU backend running       │ │  Mock GPU available            │
│  Backend: mock                  │ │  Model: mock-gpu-model-v1      │
│  Model: mock-npu-model-v1       │ │  Endpoint: http://mock-gpu:8080│
│  Model loaded: ✓                │ │  Model loaded: ✓               │
│  MudCard with animated pulse    │ │  MudCard with animated pulse   │
└─────────────────────────────────┘ └────────────────────────────────┘

┌─── Routing Ratio ───┐ ┌──── Latency (ms) ────┐ ┌─ Streaming Ratio ─┐
│     ╭─────╮         │ │    ██████████████     │ │    ╭─────╮        │
│    ╱ NPU  ╲        │ │    ██████████████     │ │   ╱ 90%  ╲       │
│   │  75%  │        │ │    ██████████████     │ │  │stream │       │
│    ╲ GPU  ╲        │ │    ██████████████     │ │   ╲      ╱       │
│     ╰─────╯         │ │ ──── ────            │ │    ╰─────╯        │
│  MudChart Donut     │ │ MudChart Bar         │ │ MudChart Donut    │
│  NpuHandled vs      │ │ Duration min/max     │ │ Streaming vs      │
│  GpuEscalated       │ │                      │ │ Non-streaming     │
└─────────────────────┘ └──────────────────────┘ └───────────────────┘

┌─── Case Distribution ──┐ ┌─── Task Types ─────────────────────────┐
│    ╭─────╮             │ │ Type              │ Count              │
│   ╱ABCDE ╲            │ │────────────────────│───────────────────│
│  │  C 73% │            │ │ simple_chat        │ 128               │
│   ╲      ╱            │ │ code_gen           │ 42                │
│    ╰─────╯             │ │ summarization      │ 15                │
│  MudChart Pie          │ │ MudTable sorted desc by count         │
│  ByCase distribution   │ │ MudTableDensity.Compact               │
└────────────────────────┘ └─────────────────────────────────────────┘

┌─── Administration ──────────────────────────────────────────────────┐
│  [Restart NPU Backend] [Reload Configuration] [Stop Service]       │
│  MudButton Variant.Filled with MudDialog confirmation              │
│  MudSnackbar toasts on success/failure                             │
└─────────────────────────────────────────────────────────────────────┘
```

### Logs Page Layout — `/logs`

```
┌─────────────────────────────────────────────────────────────────────┐
│  Logs                                                [🔍 Search...] │
│                                                       [All Levels ▼]│
│                                                                     │
│  ┌──────┬───────────┬──────────────────────────────────────────┐    │
│  │ Time │ Level     │ Message                                  │    │
│  ├──────┼───────────┼──────────────────────────────────────────┤    │
│  │12:34 │ ● Info    │ Request completed in 42ms                │    │
│  │12:33 │ ● Warning │ GPU health check timed out               │    │
│  │12:32 │ ● Error   │ Failed to connect to GPU endpoint        │    │
│  │12:31 │ ● Info    │ NPU classification: simple_chat          │    │
│  └──────┴───────────┴──────────────────────────────────────────┘    │
│                                                                     │
│  [<]  1  2  3 ... 12  [>]          Auto-refresh [●]                │
└─────────────────────────────────────────────────────────────────────┘
```

## 4. Components

### Shared / Library

| Component | Type | Purpose |
|-----------|------|---------|
| `StatusBanner.razor` | Component | `MudAlert` showing overall health, version, uptime. Color = green/yellow/red. Icon reacts. |
| `ComponentCard.razor` | Component | Reusable `MudCard` for NPU/GPU. Props: `Title`, `Status`, `Backend`, `Model`, `Endpoint`, `ModelLoaded`, `Message`. Green pulse dot animation when healthy, red when down. |
| `PulseIndicator.razor` | Component | Animated CSS dot (green pulsing = alive, red static = dead). Used inside ComponentCard. |
| `ConfirmationDialog.razor` | Dialog | `MudDialog` with title, message, confirm/cancel buttons. Used by admin actions. |

### Chart Components (Dashboard page)

| Component | Chart Type | Data Source | Render Condition |
|-----------|-----------|-------------|------------------|
| `RoutingRatioChart.razor` | Donut | `MetricsSnapshot.NpuHandled`, `.GpuEscalated` | TotalRequests > 0, else "No data yet" empty state |
| `StreamingRatioChart.razor` | Donut | `.StreamingRequests` vs Total | TotalRequests > 0 |
| `LatencyChart.razor` | Bar (2 bars) | `.DurationMs.Min`, `.DurationMs.Max` | Has duration data |
| `CaseDistributionChart.razor` | Pie | `.ByCase` dictionary | Has entries |
| `TaskTypeTable.razor` | Table | `.ByTaskType` dictionary | Has entries |

All chart components receive `MetricsSnapshot?` as `[Parameter]`, render an "Awaiting data" empty state when null.

### Admin

| Component | Type | Purpose |
|-----------|------|---------|
| `AdminPanel.razor` | Component | MudButton row + MudDialog triggers + MudSnackbar notifications |

### Logs Page

| Component | Type | Purpose |
|-----------|------|---------|
| `Logs.razor` | Page | `MudTable` with timestamp, MudChip level badge, message |
| `LogFilter.razor` | Component | `MudTextField` search + `MudSelect` level filter |

## 5. Charts Implementation

MudBlazor's `MudChart` supports `ChartType.Pie`, `ChartType.Donut`, and `ChartType.Bar`. It uses SVG rendering — no JavaScript dependency.

### Routing Ratio (Donut)

```
Input Values: [NpuHandled, GpuEscalated]
Input Labels: ["NPU", "GPU"]
Input Colors: ["#4CAF50", "#FF9800"]
```

### Streaming Ratio (Donut)

```
Input Values: [StreamingRequests, TotalRequests - StreamingRequests]
Input Labels: ["Streaming", "Non-Streaming"]
```

### Latency (Bar)

```
Input Values: [DurationMs.Min, DurationMs.Max]
Input Labels: ["Min", "Max"]
Chart type: Bar (2 bars side by side)
```

### Case Distribution (Pie)

```
Input Values: [ByCase["case_A"], ByCase["case_B"], ByCase["case_C"], ByCase["case_D"]]
Input Labels: ["Case A", "Case B", "Case C", "Case D"]
```

### Task Type Table

```
MudTable, Items = ByTaskType.Select(kvp => new { Type = kvp.Key, Count = kvp.Value })
OrderedBy: Count descending
Density: Compact
```

## 6. Error / Empty States

| State | Dashboard | Logs |
|-------|-----------|------|
| Service unreachable | `StatusBanner` shows red `MudAlert` "Cannot reach NeuroRoute service at localhost:5000" | Red banner, table hidden |
| Health API returns null | Same as unreachable | N/A |
| Metrics API returns null | Charts show "Awaiting data..." skeleton | N/A |
| No requests yet (Total=0) | Charts show "No requests processed yet" empty state with icon | N/A |
| Logs API returns null | N/A | Empty table, "No logs available" |
| Logs search yields no results | N/A | MudTable empty, "No matching entries" |

## 7. Polling & Auto-Refresh

Same pattern as current, no change to the C# code-behind:

```csharp
// Exact same logic as today, just new markup
protected override async Task OnInitializedAsync()
{
    await Refresh();
    _timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
    _ = PollAsync(_cts.Token);
}
```

Logs page adds an `MudSwitch` to toggle auto-refresh on/off.

## 8. Files

### Modified

| File | Change |
|------|--------|
| `NeuroRoute.Dashboard.csproj` | Add `MudBlazor` package reference |
| `Program.cs` | Add `builder.Services.AddMudServices()` |
| `Components/App.razor` | Remove Bootstrap CSS `<link>`, add MudBlazor CSS + `<MudThemeProvider/>` |
| `Components/_Imports.razor` | Add `@using MudBlazor` |
| `Components/Routes.razor` | No change (already uses Layout) |
| `Components/Layout/MainLayout.razor` | Replace Bootstrap grid with `MudLayout` + `MudDrawer` + `MudAppBar` + `MudMainContent` |
| `Components/Layout/NavMenu.razor` | Replace Bootstrap nav with `MudNavMenu` |
| `Components/Pages/Home.razor` | Full rewrite: MudBlazor components instead of Bootstrap. Same `@code` |
| `Services/NeuroRouteApiClient.cs` | Add `GetLogsAsync()` method |

### New

| File | Purpose |
|------|---------|
| `Components/Pages/Logs.razor` | Log viewer page |
| `Components/Shared/StatusBanner.razor` | Overall health alert + version/uptime |
| `Components/Shared/ComponentCard.razor` | Reusable NPU/GPU card with pulse indicator |
| `Components/Shared/PulseIndicator.razor` | Animated health dot (CSS only) |
| `Components/Shared/ConfirmationDialog.razor` | Reusable `MudDialog` for admin confirmations |
| `Components/Chart/RoutingRatioChart.razor` | Donut: NPU vs GPU |
| `Components/Chart/StreamingRatioChart.razor` | Donut: streaming vs non-streaming |
| `Components/Chart/LatencyChart.razor` | Bar: min/max duration |
| `Components/Chart/CaseDistributionChart.razor` | Pie: ByCase |
| `Components/Chart/TaskTypeTable.razor` | Table: ByTaskType |
| `Components/Admin/AdminPanel.razor` | Admin action buttons + dialogs |
| `Components/Admin/LogFilter.razor` | Search + level filter inputs |

### Deleted

| File | Reason |
|------|--------|
| `wwwroot/lib/bootstrap/` | Entire directory — no longer needed |
| `wwwroot/app.css` | MudBlazor handles all styling |

## 9. Theme Customization

### CSS Pulse Dot Animation

```css
/* Added inline via MudBlazor's global CSS or a small wwwroot file */
@keyframes pulse {
    0% { box-shadow: 0 0 0 0 rgba(76, 175, 80, 0.7); }
    70% { box-shadow: 0 0 0 6px rgba(76, 175, 80, 0); }
    100% { box-shadow: 0 0 0 0 rgba(76, 175, 80, 0); }
}
.pulse-green { animation: pulse 2s infinite; border-radius: 50%; width: 12px; height: 12px; background: #4CAF50; display: inline-block; }
.pulse-red { border-radius: 50%; width: 12px; height: 12px; background: #F44336; display: inline-block; }
```

### Dark Mode Toggle

The toggle lives in the `MudAppBar` (header). State is stored in `MainLayout.razor`:

```csharp
private bool _isDarkMode;

private async Task ToggleDarkMode()
{
    _isDarkMode = !_isDarkMode;
    await _themeProvider.ToggleDarkModeAsync();
}
```

Dark mode persisted via `MudThemeProvider`'s built-in `ToggleDarkModeAsync()` — no local storage needed for a session-level toggle.

## 10. Logs Data Model

New model in `Models/AdminLogEntry.cs`:

```csharp
public sealed class AdminLogEntry
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("level")]
    public string Level { get; set; } = "Info";  // Info | Warning | Error

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
```

## 11. Implementation Order

1. Add `MudBlazor` package to `NeuroRoute.Dashboard.csproj`
2. Register MudBlazor in `Program.cs`
3. Replace `App.razor` — remove Bootstrap CSS, add MudBlazor CSS + `MudThemeProvider`
4. Replace `MainLayout.razor` — `MudLayout` + `MudDrawer` + `MudAppBar` + dark mode toggle
5. Replace `NavMenu.razor` — `MudNavMenu` with Dashboard + Logs links
6. Delete `wwwroot/lib/bootstrap/` and `wwwroot/app.css`
7. Create shared components: `StatusBanner`, `ComponentCard`, `PulseIndicator`, `ConfirmationDialog`
8. Rewrite `Home.razor` with MudBlazor layout, same `@code` logic
9. Create chart components: `RoutingRatioChart`, `StreamingRatioChart`, `LatencyChart`, `CaseDistributionChart`, `TaskTypeTable`
10. Create `AdminPanel` component
11. Create `AdminLogEntry` model + `GetLogsAsync` API method
12. Create `Logs.razor` page
13. Build and verify 0 errors, 0 warnings
14. Run with `UseMockBackends=true`, verify health + metrics + charts render
15. Verify dark mode toggle works
16. Verify logs page renders

## 12. Risks & Open Questions

- **Charts on first load:** When the service just started and `TotalRequests = 0`, all chart components will show "No data yet" empty states. This is correct behavior.
- **MudChart styling:** MudBlazor's `MudChart` uses inline SVG. Colors are set programmatically via `MudChart.ChartSeries`. No CSS bleeding issues.
- **SignalR reconnection:** Blazor Server's SignalR circuit still applies. If the dashboard disconnects, the MudBlazor reconnect modal (`MudBlazorErrorBoundary`-like) handles it. The existing `ReconnectModal.razor` remains in place.
- **MudBlazor v9.x + .NET 10:** Confirmed compatible as of v9.6.0 (released June 27, 2026). No compatibility concerns.
- **Logs endpoint format:** Assumes `GET /v1/admin/logs` returns `List<AdminLogEntry>`. If the endpoint returns paginated or different format, the model may need adjustment.
