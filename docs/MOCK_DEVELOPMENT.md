# Mock Backends & Playwright Testing

## Overview

The mock backend system lets you run NeuroRoute without real NPU/GPU hardware. It provides programmable fake implementations of `INpuBackend` and `IGpuClient` controlled via HTTP admin endpoints.

**Design doc:** [`docs/superpowers/specs/2026-06-26-mock-backend-integration-testing-design.md`](./superpowers/specs/2026-06-26-mock-backend-integration-testing-design.md)

## Quick Start

```pwsh
# Run with mock backends
$env:NeuroRoute__UseMockBackends = "true"
dotnet run --project NeuroRoute.Service

# Program the fakes
Invoke-RestMethod http://localhost:5000/v1/admin/mock/scenario -Method Post `
  -Body '{"needsGpu":true}' -ContentType "application/json"

# Test the chat endpoint
Invoke-RestMethod http://localhost:5000/v1/chat/completions -Method Post `
  -Body '{"model":"neuro-route","messages":[{"role":"user","content":"Hello"}],"max_tokens":32}' `
  -ContentType "application/json"
```

## Mock Scenario Control

| Admin Endpoint | Purpose |
|----------------|---------|
| `GET /v1/admin/mock/scenario` | Inspect current state |
| `POST /v1/admin/mock/scenario` | Partial update (JSON body) |
| `POST /v1/admin/mock/scenario/reset` | Restore defaults |

### Programmable Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `npuAvailable` | bool | true | NPU health status |
| `npuBackend` | string | "mock" | Reported backend name |
| `npuModel` | string | "mock-npu-model-v1" | Reported model name |
| `taskType` | string | "simple_chat" | Classification result |
| `needsGpu` | bool | false | Whether NPU escalates to GPU |
| `routingCase` | string | "C" | Routing case (A/B/C/D) |
| `npuResponseText` | string | "Hello from mock NPU!" | NPU generation output |
| `gpuResponseText` | string | "Complex reasoning from mock GPU!" | GPU generation output |
| `gpuAvailable` | bool | true | GPU health status |
| `gpuModel` | string | "mock-gpu-model-v1" | Reported GPU model |
| `simulatedLatencyMs` | int | 50 | Simulated inference delay |
| `streamDelayMs` | int | 10 | Delay between streamed tokens |

## Playwright Integration Tests

The `NeuroRoute.Tests.Integration` project runs Playwright tests against a full Service + Dashboard stack.

### Test Scenarios

| File | Scenarios |
|------|-----------|
| `DashboardHealthTests.cs` | Both healthy (green), NPU down (red), GPU down (red), both down (unhealthy) |
| `DashboardAdminTests.cs` | Restart NPU, Reload Config buttons |
| `DashboardMetricsTests.cs` | Total requests, NPU/GPU counters, task type breakdown |

### Running Tests

```pwsh
dotnet test NeuroRoute.Tests.Integration
```

Tests require Chromium Playwright browser (`playwright install chromium`).
