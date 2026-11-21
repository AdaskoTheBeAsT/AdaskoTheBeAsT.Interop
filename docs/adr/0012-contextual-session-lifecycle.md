# ADR-0012 — Contextual session lifecycle and worker-indexed DI/Hosting registration

- **Status**: Accepted
- **Date**: 2026-11-16
- **Related**: [`docs/plan-2026-11-14.md`](../plan-2026-11-14.md) (`EX-01`), [ADR-0008](0008-uniform-snapshot-surface.md), [ADR-0010](0010-worker-completion-and-lifecycle.md)

## Context

`IExecutionSessionFactory<TSession>` intentionally started minimal:

```csharp
public interface IExecutionSessionFactory<TSession>
    where TSession : class
{
    TSession CreateSession(CancellationToken cancellationToken);
    void DisposeSession(TSession session);
}
```

In production services running `ExecutionWorkerPool<TSession>` over native x86/x64
DLLs and STA COM engines, two gaps forced every host application to maintain
fragile external state:

1. **Worker slot identity was lost on session recycle and in DI.** Native
   isolation layers that load per-worker binary-patched DLL copies (for example
   `engine_00.dll`..`engine_03.dll`, where fixed-width two-digit suffixes keep PE
   import string lengths invariant) need a **bounded, stable `WorkerIndex`
   (`0 <= WorkerIndex < WorkerCount`)** across the entire life of each worker.
   `ExecutionWorkerServiceCollectionExtensions.AddExecutionWorkerPool<TSession>`
   discarded the pool's `workerIndex` parameter (`_ => sp.GetRequiredService<...>()`),
   and manual `Interlocked.Increment` counters inside custom factories drifted
   above `WorkerCount` whenever `MaxOperationsPerSession` or
   `RecycleSessionOnFailure` recycled a worker's session.
2. **Session teardown had no matching generation or recycle-reason metadata.**
   Diagnostic logging, temporary directory cleanup, and native handle auditing
   could not correlate `DisposeSession` with the exact `SessionGeneration` that
   created the session or inspect why the previous session was recycled, nor
   distinguish failed replacement creation from an active generation advance.

Mutating `IExecutionSessionFactory<TSession>` directly would break every
existing implementation on `.NET Framework 4.7.2`–`4.8.1` (where C# default
interface implementations are unsupported by the runtime).

## Decision

### 1. Contextual lifecycle contract in `AdaskoTheBeAsT.Interop.Execution`

Introduce `SessionRecycleReason`, `SessionLifecycleContext`, a derived
`IContextualExecutionSessionFactory<TSession>` interface, and an abstract
convenience base class `ContextualExecutionSessionFactory<TSession>`:

```csharp
public enum SessionRecycleReason
{
    MaxOperationsReached = 0,
    OperationFaulted = 1,
}

public readonly struct SessionLifecycleContext
{
    public int WorkerIndex { get; init; }
    public int WorkerCount { get; init; }
    public string? WorkerName { get; init; }
    public long SessionGeneration { get; init; }
    public SessionRecycleReason? PreviousRecycleReason { get; init; }
    public int ManagedThreadId { get; init; }
}

public interface IContextualExecutionSessionFactory<TSession>
    : IExecutionSessionFactory<TSession>
    where TSession : class
{
    TSession CreateSession(
        SessionLifecycleContext context,
        CancellationToken cancellationToken);

    void DisposeSession(
        TSession session,
        SessionLifecycleContext context);
}
```

`ContextualExecutionSessionFactory<TSession>` implements explicit
non-contextual `IExecutionSessionFactory<TSession>` members by forwarding a
default standalone context (`WorkerIndex = 0`, `WorkerCount = 1`,
`ManagedThreadId = Environment.CurrentManagedThreadId`) so subclasses only
implement the two contextual methods.

### 2. Slot and generation invariants in `ExecutionWorker<TSession>` and `ExecutionWorkerPool<TSession>`

- Standalone `ExecutionWorker<TSession>` instances pass `WorkerIndex = 0` and
  `WorkerCount = 1`. `ExecutionWorkerPool<TSession>` passes each worker's fixed
  slot `0..WorkerCount - 1` and total `WorkerCount`, naming workers
  `"{PoolName}-{i}"` when the pool is named.
- If `_sessionFactory is IContextualExecutionSessionFactory<TSession>`, the
  worker invokes contextual `CreateSession(context, cancellationToken)` and
  `DisposeSession(session, context)` on the owning worker thread; otherwise it
  falls back to `IExecutionSessionFactory<TSession>`.
- **Generation pairing invariant:** a session created with
  `SessionGeneration == g` and `PreviousRecycleReason == r` is always disposed
  with the **exact same** `SessionGeneration == g` and
  `PreviousRecycleReason == r`.
- **Success-only increment:** `_nextSessionGeneration` advances from `g` to
  `g + 1` **only after** `CreateSession` returns a non-null `TSession`. If
  `CreateSession` throws, cancels, or returns `null`, no generation is consumed
  and `DisposeSession` is not called for the uncreated session.
- Generation and recycle-reason fields use atomic `Interlocked` / `Volatile`
  operations so static concurrency analysis (`ParallelChecker`) and memory
  visibility stay race-free.

### 3. Worker-indexed DI and Hosting registration

- Standard `AddExecutionWorker<TSession>` and `AddExecutionWorkerPool<TSession>`
  resolve `IContextualExecutionSessionFactory<TSession>` from `IServiceProvider`
  first, falling back to `IExecutionSessionFactory<TSession>`.
- New overloads on `ExecutionWorkerServiceCollectionExtensions` and
  `ExecutionWorkerHostingExtensions` accept
  `Func<IServiceProvider, int, IExecutionSessionFactory<TSession>>` so per-slot
  factory instances can be constructed with full access to both `IServiceProvider`
  and `workerIndex`.

## Consequences

- **100% backward-compatible.** Existing `IExecutionSessionFactory<TSession>`
  implementations and DI registrations continue to work without recompilation or
  adapter glue across all five TFMs (`net11.0`, `net10.0`, `net481`, `net48`,
  `net472`).
- **Bounded slot identity across unlimited recycles.** Worker `k` always reports
  `WorkerIndex == k` and `WorkerCount == N` regardless of how many times its
  session is recycled.
- **Deterministic lifecycle pairing.** Creation and disposal telemetry or native
  slot cleanup can key on `(WorkerIndex, SessionGeneration)` without off-by-one
  ambiguity.

## Benefits

- Removes custom slot-allocation counters and recycle-tracking wrappers from
  downstream native/COM adapters.
- Verified across standalone workers, DI/Hosting containers, creation/disposal
  fault paths, and multi-pool stress tests running 50+ count- and fault-triggered
  recycles per slot concurrently.
