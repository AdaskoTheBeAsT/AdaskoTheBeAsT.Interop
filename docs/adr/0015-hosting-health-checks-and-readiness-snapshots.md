# ADR-0015 — Hosting `IHealthCheck` integration and additive readiness snapshots

- **Status**: Accepted
- **Date**: 2026-11-16
- **Related**: [`docs/plan-2026-11-14.md`](../plan-2026-11-14.md) (`EX-02`, `EX-04`, `EX-05`), [ADR-0008](0008-uniform-snapshot-surface.md), [ADR-0010](0010-worker-completion-and-lifecycle.md)

## Context

[ADR-0008](0008-uniform-snapshot-surface.md) introduced `ExecutionWorkerSnapshot`
(`Name`, `QueueDepth`, `IsFaulted`, `Fault`) and `ExecutionWorkerPoolSnapshot`
(`Name`, `WorkerCount`, `QueueDepth`, `IsAnyFaulted`, `Workers`). When wiring
ASP.NET Core `/readyz` and `/healthz` endpoints across microservices using
`AdaskoTheBeAsT.Interop.Execution.Hosting`, three gaps remained:

1. **Snapshots could not distinguish "uninitialized" or "disposed" from "ready
   and idle".** Before `ExecutionWorkerHostedService` or
   `ExecutionWorkerPoolHostedService` finishes eager session creation (`EX-02`),
   `IsFaulted` is `false` and `QueueDepth` is `0` — identical to a healthy,
   warmed-up worker! Likewise, after graceful disposal (`Fault == null`),
   snapshots still reported `IsFaulted == false`.
2. **Pool snapshots lacked pre-aggregated worker readiness and fault counts.**
   Evaluating partial worker loss (`Degraded`) vs total worker loss (`Unhealthy`)
   required every host to iterate `snapshot.Workers` manually.
3. **Health-check dependencies belong in `Hosting`, not `Execution` or `DI`.**
   Core interop consumers and console/desktop DI apps should not pull in
   `Microsoft.Extensions.Diagnostics.HealthChecks`.

## Decision

### 1. Additive readiness and disposal fields on existing snapshot structs

Extend `ExecutionWorkerSnapshot` and `ExecutionWorkerPoolSnapshot` in
`AdaskoTheBeAsT.Interop.Execution` while retaining the original constructors for
source and binary compatibility:

```csharp
public readonly struct ExecutionWorkerSnapshot
{
    public string? Name { get; }
    public int QueueDepth { get; }
    public bool IsFaulted { get; }
    public Exception? Fault { get; }
    public bool IsInitialized { get; }
    public bool IsDisposed { get; }
}

public readonly struct ExecutionWorkerPoolSnapshot
{
    public string? Name { get; }
    public int WorkerCount => Workers.Count;
    public int QueueDepth { get; }
    public bool IsAnyFaulted { get; }
    public int InitializedWorkerCount { get; }
    public int FaultedWorkerCount { get; }
    public bool IsInitialized { get; }
    public bool IsDisposed { get; }
    public IReadOnlyList<ExecutionWorkerSnapshot> Workers { get; }
}
```

- `ExecutionWorker<TSession>` sets `_startupCompleted = true` via
  `Volatile.Write` only after eager session creation succeeds on the dedicated
  worker thread, exposing `IsInitialized = _startupCompleted && !isDisposed && !isFaulted`.
- `ExecutionWorkerPool<TSession>.GetSnapshot()` computes `QueueDepth`,
  `InitializedWorkerCount`, `FaultedWorkerCount`, and `IsAnyFaulted` in a single
  pass over the captured `ExecutionWorkerSnapshot[]` array so aggregate fields
  and `Workers` remain mutually consistent.

### 2. Built-in `IHealthCheck` implementations in `AdaskoTheBeAsT.Interop.Execution.Hosting`

Add `Microsoft.Extensions.Diagnostics.HealthChecks` (`[11.0.0, 12.0.0)` on
`net11.0` / `.NET Framework`, `[10.0.15, 11.0.0)` on `net10.0`) **only** to
`AdaskoTheBeAsT.Interop.Execution.Hosting`, shipping:

- `ExecutionHealthCheckOptions` (`int? QueueDepthWarningThreshold`, validated
  `> 0` when specified).
- `ExecutionWorkerHealthCheck<TSession> : IHealthCheck`
- `ExecutionWorkerPoolHealthCheck<TSession> : IHealthCheck`
- `IHealthChecksBuilder` extensions `AddExecutionWorkerHealthCheck<TSession>(...)`
  and `AddExecutionWorkerPoolHealthCheck<TSession>(...)`.

Health state transitions derive from a single `GetSnapshot()` call:

| Condition | Worker status | Pool status |
| --- | --- | --- |
| `IsDisposed` | `Unhealthy` | `Unhealthy` |
| Worker `IsFaulted` / Pool `WorkerCount == 0` or `FaultedWorkerCount >= WorkerCount` | `Unhealthy` (attaches `Fault`) | `Unhealthy` (attaches first worker `Fault`) |
| `!IsInitialized` (eager startup still in progress or not started) | `Unhealthy` | `Unhealthy` |
| `0 < FaultedWorkerCount < WorkerCount` (partial worker loss) | N/A | `Degraded` (attaches first worker `Fault`) |
| `QueueDepthWarningThreshold is int t && QueueDepth >= t` | `Degraded` | `Degraded` |
| Otherwise | `Healthy` | `Healthy` |

Each `HealthCheckResult.Data` dictionary includes structured snapshot metrics
(`queue_depth`, `is_initialized`, `is_disposed`, plus `worker_count`,
`initialized_worker_count`, `faulted_worker_count` for pools, and `name` when
non-empty).

## Consequences

- **No competing snapshot type.** [ADR-0008](0008-uniform-snapshot-surface.md)
  remains the single snapshot surface; pool snapshots continue to allocate one
  `ExecutionWorkerSnapshot[]` per `GetSnapshot()` call (no false zero-allocation
  claim is made).
- **Readiness gates wait for eager session creation.** Kubernetes / container
  `/readyz` probes stay `Unhealthy` until `InitializeAsync` finishes creating
  native/COM sessions on the worker threads, and flip immediately to `Unhealthy`
  once host shutdown (`StopAsync` / `DisposeAsync`) begins.
- **Clean dependency layering.** `AdaskoTheBeAsT.Interop.Execution` and
  `AdaskoTheBeAsT.Interop.Execution.DependencyInjection` take zero new NuGet
  dependencies.

## Benefits

- Eliminates duplicated health-check classes across ASP.NET Core host services.
- Surfaces partial pool degradation and queue build-up (complementing bounded
  `QueueCapacity` admission) through standard `Microsoft.Extensions.Diagnostics.HealthChecks`
  pipelines.
