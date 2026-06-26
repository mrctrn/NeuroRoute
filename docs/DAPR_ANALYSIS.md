# Dapr Integration Analysis for NeuroRoute

> **Status**: Research / Planning
>
> Analyzed: 2026-06-25
> Dapr version: v1.18
> Sources: [docs.dapr.io/developing-ai/](https://docs.dapr.io/developing-ai/)

---

## 1. What is Dapr?

[Dapr](https://dapr.io) (Distributed Application Runtime) is a portable, event-driven runtime that provides building blocks for distributed applications: service invocation, state management, pub/sub, workflows, actors, and observability. It runs as a sidecar process alongside each application.

The AI capabilities (v1.0 GA as of Dapr 1.18) specifically target LLM-powered systems with three tiers:

### 1.1 Dapr Agents (Python-only)

A Python framework for building `DurableAgent` instances — autonomous LLM-powered agents with tools, workflows, human-in-the-loop hooks, and Dapr-backed state persistence.

> **Key constraint**: Python-only. Cannot directly run .NET agents.

### 1.2 Agent Integrations

Dapr provides durable execution, state management, pub/sub, and identity to third-party agent frameworks:

| Framework | Integration type |
|-----------|-----------------|
| CrewAI | Durable workflows via Dapr Workflow |
| LangGraph | State management + service invocation |
| OpenAI Agents SDK | Durable execution + state |
| Tuning Engines | Governed AI endpoint via Dapr Workflow |

### 1.3 MCP (Model Context Protocol) Support

Dapr governs MCP traffic between agents and MCP servers using:
- mTLS + SPIFFE workload identity (on by default)
- Access control policies (App-ID gating + OPA per-tool)
- HTTP middleware (OAuth2 bearer validation)
- Built-in observability (logs, metrics, traces)

Two paths:
1. **Service invocation** — standard MCP clients work unchanged, Dapr sidecar handles governance
2. **`MCPServer` resource** — YAML-declared MCP servers, each tool becomes a durable workflow (requires Dapr Workflow client, incompatible with off-the-shelf MCP clients)

---

## 2. How Dapr Could Apply to NeuroRoute

### 2.1 Current Architecture (simplified)

```
[Client] → NeuroRoute Service → GpuClient (HTTP) → GPU Server
                               → NpuModel (ONNX/FLM) → NPU Inference
```

Single process, single machine, direct HTTP calls.

### 2.2 Dapr-Augmented Architecture (hypothetical)

```
[Client] → NeuroRoute Service → dapr sidecar
                                  ├── Service Invocation → GPU Server (governed HTTP)
                                  ├── State Store → conversation context / metrics
                                  └── Pub/Sub → async event bus for routing decisions
```

Each backend (GPU, NPU) could run as a Dapr-enabled service with its own sidecar, enabling:

| Dapr Building Block | How NeuroRoute Could Use It |
|--------------------|----------------------------|
| **Service Invocation** | Governed HTTP to GPU backends with mTLS, retries, access control |
| **State Management** | Persist conversation context, routing history, metrics snapshots |
| **Pub/Sub** | Async routing event bus (e.g., "request classified" → "GPU worker picks up") |
| **Workflows** | Durable multi-step routing with compensation (e.g., GPU timeout → retry on another backend) |
| **Observability** | Distributed tracing across NPU → GPU → storage with OpenTelemetry |
| **MCP** | Expose NeuroRoute routing decisions as MCP tools for agent frameworks |
| **Secrets** | Store GPU API keys, model paths securely |

### 2.3 MCP Integration Path

The most immediately useful Dapr capability for NeuroRoute is **MCP governance**. If NeuroRoute exposed its routing API as MCP tools:

```
LangGraph Agent → dapr sidecar → [mTLS + auth + OPA] → NeuroRoute
                                                         ├── /classify (MCP tool)
                                                         ├── /generate (MCP tool)
                                                         └── /health (MCP tool)
```

This would allow any MCP-compatible agent framework (LangGraph, CrewAI, OpenAI Agents) to call NeuroRoute's routing pipeline as governed tools.

---

## 3. Pros

### 3.1 Production-Grade Governance
- mTLS between all services (default on)
- Per-tool authorization via OPA policies
- Bearer token validation via HTTP middleware
- No custom auth code needed in NeuroRoute

### 3.2 Distributed Tracing Out of the Box
- OpenTelemetry across all service boundaries
- Trace every routing decision end-to-end (client → NPU classify → GPU generate)
- Zap traces to Jaeger, Zipkin, or any OTel collector

### 3.3 Stateful Conversations
- Dapr State Store could persist conversation history across restarts
- Would enable memory-aware routing (the "planned" feature in VISION.md)
- Pluggable backends: Redis, PostgreSQL, CosmosDB, etc.

### 3.4 Resilient Multi-Backend Routing
- Dapr Workflows could orchestrate complex routing policies:
  - Try NPU → if classification confidence < 0.7 → escalate to GPU
  - Attempt GPU A → timeout after 30s → fall back to GPU B
  - Compensation actions on failure
- Workflows are durable (survive process restarts)

### 3.5 MCP Ecosystem
- NeuroRoute could become an MCP server, callable by any MCP client
- Opens the door to agent frameworks using NeuroRoute as a "smart router" tool
- Dapr sidecar handles auth, observability, rate limiting

---

## 4. Cons

### 4.1 Massive Overhead for a Local Service

| Dimension | Current | With Dapr |
|-----------|---------|-----------|
| Processes | 1 (NeuroRoute) | 1 + 1 sidecar per service |
| RAM | <40 MB idle | +50–100 MB per sidecar |
| Setup | `sc create NeuroRoute` | `dapr init`, component YAMLs, sidecar config |
| Complexity | Single .csproj | Multi-app, multi-YAML, Docker/containers on Windows |

NeuroRoute targets **consumer Windows PCs** (NPU-equipped laptops). Adding a sidecar runtime significantly increases the deployment barrier.

### 4.2 Dapr Agents is Python-Only

The most relevant AI feature — `DurableAgent` — requires Python. NeuroRoute is .NET C#. This means:
- Cannot use `DurableAgent` directly
- Would need a Python agent bridge, or use Dapr only for infrastructure (service invocation, state, pub/sub)
- The .NET SDK lacks the agent-specific abstractions

### 4.3 Windows Sidecar Limitations

- Dapr sidecars run as separate processes (not Windows Services by default)
- On Windows, sidecars use process-level isolation (not Docker containers unless Docker Desktop is installed)
- `dapr init` on Windows requires either Docker Desktop or manual binary setup
- Adds significant friction to the "single EXE deploy" principle

### 4.4 Single-Machine Diminishes Value

Dapr's value proposition (distributed systems, multi-service, cloud-native) is strongest when:
- Multiple services on multiple machines
- Need for service discovery, mTLS across hosts, pub/sub across processes
- Complex topology changes frequently

NeuroRoute is a **local routing proxy** — all inference happens on the same machine. Many Dapr features solve problems that don't exist at this scale.

### 4.5 YAML Configuration Burden

Dapr components are configured via YAML files:
```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: statestore
spec:
  type: state.redis
  version: v1
```

Every feature (state, pub/sub, workflows) needs its own component YAML plus deployment YAML. This adds cognitive overhead for what is currently a single `appsettings.json`.

### 4.6 Debugging Complexity

- Debugging requires both NeuroRoute and its sidecar to be running
- Sidecar logs are separate from application logs
- Distributed tracing is valuable but overkill for single-machine debugging

---

## 5. Comparison Matrix

| Capability | Current NeuroRoute | NeuroRoute + Dapr |
|-----------|-------------------|-------------------|
| **Routing logic** | Built-in (NpuPlanner, Router) | Same (no Dapr replacement) |
| **Classification** | INpuBackend (ONNX/FLM) | Same |
| **GPU communication** | HttpClient with retry | Dapr Service Invocation + resiliency policies |
| **State/persistence** | None (in-memory only) | Dapr State Store (Redis, PostgreSQL) |
| **Conversation memory** | Not implemented | Dapr State Store could enable it |
| **Observability** | Activity + Meter + middleware | Dapr-managed OpenTelemetry (sidecar) |
| **Multi-backend failover** | Manual retry (3 attempts) | Dapr Workflow with compensation |
| **Auth/access control** | None | mTLS + OPA + bearer middleware |
| **MCP compatibility** | Manually implement endpoints | Declarative via Dapr sidecar |
| **Agent framework integration** | None | MCP tools → LangGraph/CrewAI/OpenAI |
| **Deployment complexity** | Single EXE + sc.exe | Dapr init + sidecar + YAMLs + EXE |
| **RAM footprint** | ~40 MB | ~100–150 MB (+sidecar) |
| **Windows-native** | Yes (Windows Service) | Partial (sidecar as process) |

---

## 6. Recommendation

### Do Not Adopt Dapr as a Full Platform for v1.x

NeuroRoute's current architecture is appropriate for its domain:
- Single Windows Service
- Single-machine deployment
- Lightweight footprint (<40 MB)
- Zero external runtime dependencies

Adding Dapr would:
- Triple the deployment complexity
- Double the memory footprint
- Contradict the "single EXE" principle
- Solve problems NeuroRoute doesn't have (distributed communication across hosts)

### Consider Targeted Integration Points for v2.x

| Feature | Dapr Mechanism | When to Revisit |
|---------|---------------|-----------------|
| **Conversation state** | Dapr State Store | When memory-aware routing is implemented |
| **Multi-GPU failover** | Dapr Workflow | When supporting multiple GPU backends |
| **MCP tool exposure** | Dapr MCP service invocation | When agent frameworks need to call NeuroRoute |
| **Distributed tracing** | Dapr + OpenTelemetry | When running in production with observability infra |

### MCP Path: Lowest Friction, Highest Value

If any part of Dapr is adopted first, it should be **MCP governance via Dapr service invocation**. This:
- Preserves NeuroRoute as a standalone EXE
- Adds a Dapr sidecar only when agent integration is needed
- No changes to NeuroRoute's code — just Dapr component YAMLs
- Allows any MCP-compatible agent to call NeuroRoute as governed tools

---

## 7. Decision

**For v1.x (current):** No Dapr integration. Focus on:
- NPU-first routing (complete)
- FLM backend (complete)
- Metrics + observability (complete)
- Blazor dashboard (complete)
- Integration tests (when HW available)

**For v2.x planning:** Re-evaluate Dapr MCP integration when:
1. Agent framework integration is a requirement (not just a "nice to have")
2. Multi-machine deployment is needed
3. Conversation memory across restarts is implemented

The Semantic Model Cascade pattern (CONCEPTS.md) is the correct abstraction — Dapr infrastructure would implement the communication layer, not change the routing logic.
