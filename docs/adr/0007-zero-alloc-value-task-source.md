# ADR-0007 — Zero-allocation `ExecuteValueAsync` via pooled `IValueTaskSource<T>`

- **Status**: Accepted (supersedes [ADR-0004](0004-valuetask-hotpath-overloads.md))
- **Date**: 2026-04-19
- **Related**: [`0004-valuetask-hotpath-overloads.md`](0004-valuetask-hotpath-overloads.md), `improve.md` ("`ExecuteValueAsync` is a convenience wrapper — it does not yet avoid the inner `Task` allocation")

## Context

ADR-0004 shipped `ExecuteValueAsync` as extension methods that simply wrap the existing `Task`-returning `ExecuteAsync` in a `new ValueTask(task)`. That covered ergonomics (callers stay in the `ValueTask` domain) but explicitly deferred the allocation story:

> `Task`-returning calls still materialise a `Task<T>` + `TaskCompletionSource<T>` per submission. The `ValueTask` wrapper saves the manual wrapping but not the underlying allocations.

The residual gap was flagged on the quality-score review: `ExecuteValueAsync` should be a **genuinely** zero-allocation hot path, not a cosmetic wrapper. Three options were considered:

1. **Keep only the extension wrappers.** Rejected — does not close the gap.
2. **Replace every internal `ExecutionWorkItem<TResult>` with a pooled `IValueTaskSource<T>` and change `ExecuteAsync` to also return `ValueTask`.** Rejected — breaks `Task`-returning consumers, interface contract, `Task.WhenAll` composition, and legacy netfx / netstandard2.0 semantics.
3. **Add a separate pooled work-item type alongside the existing TCS-backed one.** `ExecuteValueAsync` becomes the instance path that rents a `PooledValueExecutionWorkItem<TSession, TResult>` (or its void sibling) from a static pool; `ExecuteAsync` keeps the `TaskCompletionSource<TResult>` path untouched.

Option 3 was chosen.

## Decision

Two new internal types are added:

- `PooledValueExecutionWorkItem<TSession, TResult>` — implements `IValueTaskSource<TResult>` + `IValueTaskSource`, backed by `System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<TResult>`.
- `PooledVoidExecutionWorkItem<TSession>` — implements `IValueTaskSource` only, backed by `ManualResetValueTaskSourceCore<byte>`. Dedicated type so the void overload does not pay a boxing / delegate-shim tax.

Both are compiled under `#if NET8_0_OR_GREATER` because `ManualResetValueTaskSourceCore<T>` is only reliably available on modern .NET TFMs in-box. Each has:

- a bounded per-closed-generic `ConcurrentQueue<>` pool with `MaxPoolSize = 256` and an `Interlocked`-tracked size;
- `Rent(action, options, cancellationToken)` that either pops an existing instance or allocates a fresh one;
- `Return()` called from `GetResult`'s `finally` that resets `_core`, clears the action / token / options, and either enqueues the instance or — when the pool is full — drops it;
- a **stale-token guard** in `GetResult`: if the observed token does not match `_core.Version` the method delegates to `MRVTSC.GetResult` (which will throw `InvalidOperationException` by spec) **without** returning the instance, so a buggy double-await can never corrupt the pool.

`ExecutionWorker<TSession>` gains two instance methods (also gated `NET8_0_OR_GREATER`):

```csharp
public ValueTask ExecuteValueAsync(
    Action<TSession, CancellationToken> action,
    ExecutionRequestOptions? options = null,
    CancellationToken cancellationToken = default);

public ValueTask<TResult> ExecuteValueAsync<TResult>(
    Func<TSession, CancellationToken, TResult> action,
    ExecutionRequestOptions? options = null,
    CancellationToken cancellationToken = default);
```

The implementations mirror `ExecuteAsync` — early cancellation, dispose / fault guards, channel write, queue-depth increment — but:

- rent a pooled work item instead of constructing an `ExecutionWorkItem<TResult>`;
- return `new ValueTask(workItem, workItem.Version)` / `new ValueTask<TResult>(workItem, workItem.Version)` directly.

`ExecutionWorkerPool<TSession>` gains matching `ExecuteValueAsync` overloads that select a concrete `ExecutionWorker<TSession>` through the configured `IWorkerScheduler<TSession>` and delegate to its pooled hot path. A custom scheduler that returns a foreign worker throws a deterministic `InvalidOperationException`.

The existing `ExecutionWorkerValueTaskExtensions` methods are updated to **dispatch** to these concrete instance methods when the receiver is a built-in worker / pool on a supported TFM, and to keep the `new ValueTask(task)` fallback otherwise. The public extension surface is unchanged, so every existing call site automatically gets the zero-alloc path for free.

## Consequences

- **Zero allocations on the hot path (NET6+).** `ValueTask` is a readonly `struct`; `ManualResetValueTaskSourceCore<T>` is also a `struct` stored directly on the pooled work item. After pool warm-up there is no per-submission heap allocation for either the source or the wrapping task.
- **Spec-compliant single observation.** The returned `ValueTask`/`ValueTask<TResult>` must be observed exactly once, as the framework already requires. The XML doc on both `ExecuteValueAsync` overloads states this explicitly.
- **Non-breaking for every other path.** `ExecuteAsync` still returns `Task` / `Task<TResult>` backed by `TaskCompletionSource<T>`; `IExecutionWorker<TSession>` / `IExecutionWorkerPool<TSession>` are untouched; `Task.WhenAll` and other composition primitives still work.
- **Back-compat on legacy TFMs.** On `net462 … net481` and `netstandard2.0` the `#if NET8_0_OR_GREATER` guard omits the pooled path; the extension falls back to the existing `new ValueTask(ExecuteAsync(...))` wrapper. The surface is identical; only the optimization is absent.
- **Bounded pool.** `MaxPoolSize = 256` per closed generic keeps steady-state memory flat under burst load. Excess instances are released to the GC rather than forced into an unbounded queue.
- **Single code path.** The pool calls through the exact same `ExecutionWorker<TSession>` instance method; there is no second copy of cancellation / dispose / fault handling to keep in sync.

## Benefits

- **Closes the 0.1-point gap flagged on the 9.3/10 review.**
- **Comparable to the patterns used in the .NET socket stack** (pooled `IValueTaskSource<T>` plus a short version guard), so the approach is familiar and well-validated.
- **Path to further gains.** With the pool in place, future work can extend it to batch submission and to the cancellation path without touching call sites.
