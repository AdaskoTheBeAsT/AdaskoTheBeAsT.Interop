# ADR-0008 — Uniform `Name` / `GetSnapshot` surface and single-factory pool constructor

- **Status**: Accepted
- **Date**: 2026-04-19
- **Related**: `improve.md` ("`Name` / `PendingCount`-style read-only surface is present on the worker but uneven on the pool side … no uniform `Snapshot` type"; "`ExecutionWorkerPool` still takes `Func<int, IExecutionSessionFactory<TSession>>` — the DI helper papers over it but the raw API is a minor ergonomic wart").

## Context

Two closely-related ergonomic complaints from the quality-score review:

1. **Observability surface was uneven.** `ExecutionWorker<TSession>` exposed `QueueDepth`, `IsFaulted`, `Fault`, and (internally) the display name. The pool exposed `WorkerCount`, aggregate `QueueDepth`, `IsAnyFaulted`, and `WorkerFaults` but no `Name`, no per-worker detail, and no atomic snapshot — so a dashboard reading three properties back-to-back could observe torn values (e.g. aggregate `QueueDepth` from *before* a submission combined with `IsAnyFaulted` from *after* it).
2. **Pool construction was noisier than it needed to be.** `ExecutionWorkerPool<TSession>` only accepted `Func<int, IExecutionSessionFactory<TSession>>`. The common case — one shared factory used by every worker — forced callers to write `_ => factory`. The DI helper papered over this internally, but hand-wired callers paid the noise.

## Decision

### Uniform snapshot surface

Two new public `readonly struct` types:

```csharp
public readonly struct ExecutionWorkerSnapshot
{
    public string? Name { get; }
    public int QueueDepth { get; }
    public bool IsFaulted { get; }
    public Exception? Fault { get; }
}

public readonly struct ExecutionWorkerPoolSnapshot
{
    public string? Name { get; }
    public int WorkerCount => Workers.Count;
    public int QueueDepth { get; }           // aggregate across Workers
    public bool IsAnyFaulted { get; }        // aggregate across Workers
    public IReadOnlyList<ExecutionWorkerSnapshot> Workers { get; }
}
```

Added on the interfaces:

```csharp
string? Name { get; }
ExecutionWorkerSnapshot GetSnapshot();          // on IExecutionWorker<TSession>
ExecutionWorkerPoolSnapshot GetSnapshot();      // on IExecutionWorkerPool<TSession>
```

Implementations capture every field within one call, so readers observe a coherent point-in-time view. The pool's aggregate `QueueDepth` / `IsAnyFaulted` are computed **from** the per-worker snapshots contained in the same return value, so the aggregate cannot drift out of sync with the detail.

Existing properties (`QueueDepth`, `IsFaulted`, `Fault`, `WorkerFaults`, …) are retained for continuity.

### Single-factory pool constructor

Two new overloads on `ExecutionWorkerPool<TSession>`:

```csharp
public ExecutionWorkerPool(
    IExecutionSessionFactory<TSession> sessionFactory,
    ExecutionWorkerPoolOptions options);

public ExecutionWorkerPool(
    IExecutionSessionFactory<TSession> sessionFactory,
    ExecutionWorkerPoolOptions options,
    IWorkerScheduler<TSession>? scheduler);
```

Internally these wrap the single factory in a `_ => factory` provider and call the existing per-index constructor. The `Func<int, IExecutionSessionFactory<TSession>>` overloads remain — they are still the right shape when each worker loads an isolated native library set — so the change is **strictly additive**.

The DI helper (`AddExecutionWorkerPool<TSession>`) is intentionally left unchanged so that transient `IExecutionSessionFactory<TSession>` registrations continue to yield a fresh instance per worker, which the shared-factory overload would silently collapse to one shared instance.

## Consequences

- **Dashboards and health checks get a coherent point-in-time view** without having to snapshot several properties and hope the sampler was atomic.
- **Custom `IExecutionWorker<TSession>` / `IExecutionWorkerPool<TSession>` implementations must add `Name` + `GetSnapshot`.** This is a public interface addition; on an unreleased 1.0 it is acceptable and is recorded in `CHANGELOG.md`.
- **Per-call allocation is limited to the returned struct plus a single `ExecutionWorkerSnapshot[]` inside the pool snapshot.** Nothing is retained beyond the caller's stack frame.
- **Single-factory constructor removes the `_ =>` boilerplate** for the common case while keeping the per-index factory overload available for workloads that genuinely need per-worker isolation.

## Benefits

- Aligns `IExecutionWorker` and `IExecutionWorkerPool` on a common observability vocabulary (`Name` + `GetSnapshot`).
- Cuts the noisiest line out of the hand-wired pool-construction pattern without surrendering the isolated-factory scenario.
- Feeds directly into the BenchmarkDotNet harness planned next: benchmarks can capture before / after snapshots without composing reads by hand.
