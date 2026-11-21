# ADR-0014 — Execution deadline extension overloads and linked-token lifetime

- **Status**: Accepted
- **Date**: 2026-11-16
- **Related**: [`docs/plan-2026-11-14.md`](../plan-2026-11-14.md) (`EX-03`), [ADR-0010](0010-worker-completion-and-lifecycle.md)

## Context

Every downstream HTTP/gRPC service dispatching work to
`IExecutionWorker<TSession>` or `IExecutionWorkerPool<TSession>` needed per-call
execution deadlines (`TimeSpan timeout`) that:

1. Bound the **entire caller wait** (cold startup, channel queue wait, and
   session execution).
2. Distinguish **caller cancellation** (propagate `OperationCanceledException`
   carrying the caller's exact `CancellationToken`, e.g. HTTP `499`) from
   **deadline expiration** (throw `TimeoutException`, e.g. HTTP `504`).
3. Skip queued work items whose deadline expires before the worker dequeues them
   so stale load is shed immediately instead of burning native solver time.
4. Return control promptly to the awaiting caller if a deadline fires while
   synchronous native/COM work is already running — **without** preempting the
   worker thread (which is impossible to do safely in managed/native interop)
   and **without** disposing or resetting a `CancellationTokenSource` whose
   `CancellationToken` can still be inspected by the in-flight native delegate.

Adding new abstract `ExecuteAsync(..., TimeSpan timeout, ...)` methods directly
to `IExecutionDispatcher<TSession>` was ruled out: `.NET Framework 4.7.2`–`4.8.1`
does not support default interface implementations, so any new interface member
would be a source- and binary-breaking change for custom mocks, decorators, and
fakes implementing `IExecutionDispatcher<TSession>`, `IExecutionWorker<TSession>`,
or `IExecutionWorkerPool<TSession>`.

## Decision

### 1. Extension methods on `IExecutionDispatcher<TSession>`

Ship `public static class ExecutionDispatcherExtensions` in
`AdaskoTheBeAsT.Interop.Execution` with **eight** overloads covering `Task`,
`Task<TResult>`, `ValueTask`, and `ValueTask<TResult>`, each with and without
`ExecutionRequestOptions`:

```csharp
public static Task ExecuteAsync<TSession>(
    this IExecutionDispatcher<TSession> dispatcher,
    Action<TSession, CancellationToken> action,
    TimeSpan timeout,
    CancellationToken cancellationToken = default)
    where TSession : class;

public static Task<TResult> ExecuteAsync<TSession, TResult>(
    this IExecutionDispatcher<TSession> dispatcher,
    Func<TSession, CancellationToken, TResult> action,
    TimeSpan timeout,
    CancellationToken cancellationToken = default)
    where TSession : class;

// + ExecutionRequestOptions overloads and matching ExecuteValueAsync overloads
```

### 2. Boundary validation and fast paths

- `dispatcher is null` or `action is null` throws `ArgumentNullException`
  synchronously.
- `timeout < TimeSpan.Zero` (except `Timeout.InfiniteTimeSpan`) or
  `timeout.TotalMilliseconds > int.MaxValue` throws `ArgumentOutOfRangeException`.
- `timeout == Timeout.InfiniteTimeSpan` forwards directly to the underlying
  `IExecutionDispatcher<TSession>` method with zero wrapper overhead.
- If `cancellationToken.IsCancellationRequested` on entry, caller cancellation
  wins immediately (`OperationCanceledException(cancellationToken)`), even when
  `timeout == TimeSpan.Zero`.
- Otherwise `timeout == TimeSpan.Zero` validates worker/pool readiness first
  (propagating `ObjectDisposedException` or a latched terminal `Fault` if not
  submittable) and immediately faults with `TimeoutException` without enqueueing
  or invoking `action`.

### 3. Lock-free 3-state settlement and deferred CTS cleanup

For finite positive timeouts (`0 < timeout <= int.MaxValue` ms):

1. Rent a deadline `CancellationTokenSource` from an internal bounded lock-free
   `DeadlineCtsPool` (using `TryReset()` reuse on `.NET 6+` when the timer did
   not fire, and `CancelAfter(timeout)`). When `callerToken.CanBeCanceled` is
   `true`, link `callerToken` and `deadlineCts.Token`; when `false`
   (`CancellationToken.None`), pass `deadlineCts.Token` directly without
   allocating a linked `CancellationTokenSource`.
2. Wrap `action` with `ThrowIfExpiredBeforeStart(token)` and submit to the
   underlying dispatcher using `EffectiveToken`:
   - If the deadline or caller token fires **while queued**, the worker's
     existing pre-execution cancellation check (plus the wrapper guard) skips
     `action` without touching the native session.
3. Coordinate completion between the inner worker operation, caller cancellation,
   and deadline expiry with an atomic `Interlocked.CompareExchange` state
   machine (`StatePending` -> `StateCompletedByInner` | `StateAbandonedByWaiter`):
   - If the inner operation completes first, its result, fault, or cancellation
     wins cleanly (`CompletedByInner`), even if the deadline timer fires
     concurrently.
   - If `callerToken` cancels while the inner operation is still pending, the
     caller's returned `Task` / `ValueTask` transitions immediately to Canceled
     with the **exact `callerToken`**.
   - If `deadlineCts` expires while `callerToken` is not canceled, the caller's
     returned `Task` / `ValueTask` faults immediately with `TimeoutException`.
4. **Lifetime safety invariant:** `_callerRegistration`, `_deadlineRegistration`,
   `_linkedCts`, and `_deadlineCts` are disposed/returned **only in the inner
   operation's completion continuation** (`CleanupResources`), never when the
   caller abandons the wait early. A running synchronous delegate continues
   occupying its worker thread and session until it returns, and can safely
   inspect its `CancellationToken` for the entire duration of execution.

## Consequences

- **Non-preemptive native execution remains explicit.** Returning
  `TimeoutException` unblocks the *caller*; the dedicated worker thread remains
  busy until the synchronous native call returns (cooperatively via `ct` or
  naturally). Subsequent items queued to that worker wait behind the finishing
  call, and hard kill semantics still require out-of-process isolation.
- **Additional async coordination state on finite deadlines.** Finite-deadline
  calls allocate a lightweight state coordinator (`TaskCompletionSource` +
  token registrations) so caller abandonment can decouple safely from an
  in-flight pooled `ValueTask` source without violating `IValueTaskSource`
  single-consumer ownership ([ADR-0010](0010-worker-completion-and-lifecycle.md)).
  `Timeout.InfiniteTimeSpan` bypasses this coordinator completely.
- **Full interface compatibility.** `IExecutionDispatcher<TSession>`,
  `IExecutionWorker<TSession>`, and `IExecutionWorkerPool<TSession>` gain zero
  abstract members.

## Benefits

- Replaces bespoke per-service timeout/CTS-pool wrappers with one battle-tested
  implementation across all five TFMs.
- Guarantees no use-after-dispose or premature `TryReset()` races against
  in-flight native delegates observing `CancellationToken`.
