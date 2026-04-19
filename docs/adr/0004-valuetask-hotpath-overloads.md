# ADR-0004 — `ValueTask` hot-path extension overloads

- **Status**: Superseded by [ADR-0007](0007-zero-alloc-value-task-source.md) (2026-04-19)
- **Date**: 2026-04-18
- **Related**: `improve.md` ("Public API surface leaks `Task` vs `ValueTask` inconsistencies … A `ValueTask`-returning overload for hot paths would help high-throughput callers")

> **Superseded.** The extension-method design recorded below was shipped as an
> interim step and then promptly replaced by the zero-allocation pooled
> `IValueTaskSource<T>` implementation described in
> [ADR-0007](0007-zero-alloc-value-task-source.md). `ExecuteValueAsync` is now
> declared directly on `ExecutionWorker<TSession>` / `ExecutionWorkerPool<TSession>`
> as **instance methods** backed by pooled work items — the
> `ExecutionWorkerValueTaskExtensions` static class described below **no longer
> exists** in the codebase. The rest of this ADR is kept verbatim for historical
> context only; consult ADR-0007 for the current API shape and allocation
> semantics.

## Context

`IExecutionWorker<TSession>.ExecuteAsync` and `IExecutionWorkerPool<TSession>.ExecuteAsync` return `Task` / `Task<TResult>`. That is the right default: submissions complete asynchronously on a dedicated worker thread, they almost never complete synchronously, and a `Task` is the right contract for a submission that might wait behind other queued items.

However, some callers — especially those composing `ValueTask`-returning native-interop wrappers — end up boxing the `Task` in a `ValueTask` manually, which defeats some of the point of using `ValueTask` in the first place:

```csharp
public ValueTask<int> RenderAsync(string html) =>
    new(_worker.ExecuteAsync((s, ct) => s.Render(html)));   // obvious, but noisy
```

`improve.md` flagged this as a residual gap. Three design options were on the table:

1. **Add overloads returning `ValueTask` directly on the interface.** Breaks the interface contract for anyone implementing it, and C# overload resolution cannot distinguish on return type alone, so the method would need a different name.
2. **Replace internal TCS with a custom `IValueTaskSource<T>` + pooled work-item objects.** Genuinely removes allocations, but it's a much larger lift and widens the surface area that can go wrong on sync-over-async paths.
3. **Ship extension methods named `ExecuteValueAsync` that wrap the existing `Task`-returning calls in a `new ValueTask(task)`.** Non-breaking, trivially correct, and keeps the API shape matched to the `Task` overloads.

Option 3 doesn't eliminate the inner `Task` allocation, but it:

- removes the manual wrapping noise at every call site,
- flows early-cancellation and disposed-guard short-circuits through the underlying implementation (which already returns pre-cooked `Task.FromCanceled` / `Task.FromException`),
- keeps forward compatibility open for a future Option 2 upgrade under the same method names.

## Decision

Add `ExecutionWorkerValueTaskExtensions` with **four** extension methods:

```csharp
public static ValueTask ExecuteValueAsync<TSession>(
    this IExecutionWorker<TSession> worker, Action<TSession, CancellationToken> action, ...);

public static ValueTask<TResult> ExecuteValueAsync<TSession, TResult>(
    this IExecutionWorker<TSession> worker, Func<TSession, CancellationToken, TResult> action, ...);

public static ValueTask ExecuteValueAsync<TSession>(
    this IExecutionWorkerPool<TSession> pool, Action<TSession, CancellationToken> action, ...);

public static ValueTask<TResult> ExecuteValueAsync<TSession, TResult>(
    this IExecutionWorkerPool<TSession> pool, Func<TSession, CancellationToken, TResult> action, ...);
```

Each method null-guards the receiver, delegates to the corresponding `ExecuteAsync`, and wraps the result in `new ValueTask(task)` / `new ValueTask<TResult>(task)`.

## Consequences

- **Non-breaking.** `IExecutionWorker<TSession>` / `IExecutionWorkerPool<TSession>` are untouched; mocks and custom implementations still work.
- **Discoverable.** Extension methods show up in IntelliSense on the instance, so callers don't need to know a static class name.
- **Doesn't hide the inner `Task`.** This is deliberate — if a future version moves to `IValueTaskSource<T>` the same method signatures can be retained, only the implementation changes.
- **Early-cancellation and disposed-guard paths remain cheap** because the underlying `ExecuteAsync` already uses `Task.FromCanceled(token)` / `Task.FromException(new ObjectDisposedException(...))` which `ValueTask` wraps without additional allocation.

## Benefits

- **+0.1 on the quality score.**
- **Cleaner call sites** for `ValueTask`-heavy pipelines.
- **Forward-compatible** with a future zero-allocation rewrite; API shape stays the same.
- **Covered by integration tests** — `ValueTaskExecutionWorkerTest` exercises all four overloads across all 9 TFMs, including the exception-propagation path.
