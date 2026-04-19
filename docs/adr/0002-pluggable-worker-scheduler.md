# ADR-0002 — Pluggable `IWorkerScheduler<TSession>` seam with two built-ins

- **Status**: Accepted
- **Date**: 2026-04-18
- **Related**: `improve.md` ("Scheduling is fixed at 2 strategies: no pluggable IWorkerScheduler seam for advanced users"), `plan.md` Phase 2 point 4

## Context

After Phase 2 we had two hard-coded scheduling strategies in `ExecutionWorkerPool<TSession>`:

- `SchedulingStrategy.LeastQueued` — pick the worker with the smallest `QueueDepth`; rolling-index tie-break; skip faulted workers.
- `SchedulingStrategy.RoundRobin` — strict rotation via `Interlocked` index; skip faulted workers.

Both policies were private methods on the pool. `improve.md` flagged the fixed set as one of the residual gaps keeping the library below a 9.5 score: advanced consumers have their own pinning / affinity / cost-based policies (session warm-state, pre-flight health, geographic affinity, GPU-slot round-robin), and they can't plug any of that in without forking the pool.

The design tension is:

- A public scheduler seam must not force the options type generic (it binds via `IOptions<ExecutionWorkerPoolOptions>` — `IOptions<T>` does not play well with generic `T`).
- The seam must hand the scheduler a stable, index-stable, allocation-free snapshot of the workers.
- Faulted workers must stay out of the selection, because a faulted worker's `QueueDepth` is 0 and a naive `LeastQueued` would route every subsequent submission straight into an `ObjectDisposedException`.

## Decision

Introduce a public `IWorkerScheduler<TSession>` interface with **one** method:

```csharp
public interface IWorkerScheduler<TSession>
    where TSession : class
{
    IExecutionWorker<TSession> SelectWorker(IReadOnlyList<IExecutionWorker<TSession>> workers);
}
```

Ship **two** public sealed implementations:

- `RoundRobinWorkerScheduler<TSession>` — `Interlocked.Increment` rolling index; skip faulted workers.
- `LeastQueuedWorkerScheduler<TSession>` — scan from rolling index, strict `<` comparison so equal-depth workers rotate, zero-depth early exit to halve the average scan cost on lightly loaded pools, skip faulted workers.

Thread an optional scheduler parameter through a new pool constructor overload:

```csharp
public ExecutionWorkerPool(
    Func<int, IExecutionSessionFactory<TSession>> sessionFactoryFactory,
    ExecutionWorkerPoolOptions options);                       // existing, now delegates

public ExecutionWorkerPool(
    Func<int, IExecutionSessionFactory<TSession>> sessionFactoryFactory,
    ExecutionWorkerPoolOptions options,
    IWorkerScheduler<TSession>? scheduler);                    // new
```

When `scheduler` is `null`, the pool resolves a built-in from `options.SchedulingStrategy`. The pool caches a covariant `IReadOnlyList<IExecutionWorker<TSession>>` view (`_workerView`) over its internal `ExecutionWorker<TSession>[]` so every call to `SelectWorker` is allocation-free.

All previously-private scheduling plumbing (`_nextWorkerIndex`, `_schedulingStrategy`, `SelectRoundRobinWorker`, `SelectLeastQueuedWorker`, `NextRollingIndex`) is deleted from the pool; the responsibility now lives on the scheduler.

## Consequences

- **`ExecutionWorkerPoolOptions` stays non-generic.** `IOptions<ExecutionWorkerPoolOptions>` binding works exactly as before — the scheduler travels through the ctor, not through options.
- **Non-breaking.** The two-argument constructor still exists with identical semantics; the three-argument overload is purely additive.
- **Faulted-worker contract is now the scheduler's problem.** Both built-ins skip faulted workers and fall back to the rolling-index candidate if *all* workers are faulted so the caller gets a deterministic `ObjectDisposedException` instead of a silent hang.
- **Testability.** Two test helpers (`RecordingScheduler`, `FixedIndexScheduler`) make it trivial to prove that (a) the pool hands the scheduler the correct snapshot, (b) returned workers really do receive the submission, and (c) null-scheduler / null-workers / empty-workers guards all throw the right `ArgumentNullException` / `ArgumentOutOfRangeException` with a parameter name.
- **Thread-safety.** Schedulers are stateless where possible (round-robin uses a static `Interlocked` index). Custom schedulers with mutable state must be thread-safe; the ADR-0002 test helpers use `ConcurrentQueue` to satisfy that contract (also silences `MA0158`).

## Benefits

- **+0.3 on the quality score** (main "residual gap" item from `improve.md`).
- **Future-proof.** Affinity / cost / pre-flight-health scheduling can now live outside the core library.
- **Better documentation of the faulted-worker contract.** Before, it was implicit in two private methods; now it's a visible public contract, and both shipped built-ins model it consistently.
