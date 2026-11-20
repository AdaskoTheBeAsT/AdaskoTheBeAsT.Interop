# ADR-0020 — Configurable worker `MaxStackSize` and worker-thread-affine `OnWorkerThreadStart` / `CreateWorkerThreadScope`

- **Status**: Accepted
- **Date**: 2026-11-17
- **Related**: [`docs/plan-2026-11-17.md`](../plan-2026-11-17.md) (`EX-11`), [ADR-0010](0010-worker-completion-and-lifecycle.md), [ADR-0013](0013-before-thread-start-hook.md)

## Context

Legacy 32-bit native and COM engines (Compaq Visual Fortran, Borland Delphi,
PowerBASIC, VB6) running on `ExecutionWorker<TSession>` and
`ExecutionWorkerPool<TSession>` inside `win-x86` processes exposed two
thread-level requirements that [ADR-0013](0013-before-thread-start-hook.md)
(`BeforeThreadStart`) intentionally did not cover:

1. **Explicit dedicated-thread stack sizing (`MaxStackSize`).** Inside a 2–4 GiB
   32-bit virtual address space, some Fortran/Delphi solvers allocate large
   stack frames requiring > 1 MiB stacks, whereas multi-worker hosts with shallow
   native calls benefit from bounded stack reservations via
   `new Thread(ParameterizedThreadStart, int maxStackSize)`.
2. **Worker-thread-affine initialization and teardown across session recyclings.**
   `BeforeThreadStart` runs on the *caller* thread before `Thread.Start()`.
   Thread-affine setup — such as CoreCLR STA message-window pre-warming, thread
   culture / Win32 `LCID` scopes, native FPU/error-mode guards, or a
   worker-lifetime COM activation context — must execute **once on the dedicated
   worker thread itself** (after `ApartmentState.STA` is active), survive
   `MaxOperationsPerSession` and fault-triggered session recyclings, and dispose
   its `IDisposable` scope **on that exact same worker thread** after the final
   `DisposeSession()` during worker shutdown.

## Decision

### 1. `MaxStackSize` on `ExecutionWorkerOptions` and `ExecutionWorkerPoolOptions`

Add `public int MaxStackSize { get; set; }` (`0` default = runtime/PE default
stack size) to both options types:

- `Validate()` throws `ArgumentOutOfRangeException(nameof(MaxStackSize))` when
  `MaxStackSize < 0`.
- `ExecutionWorker<TSession>` creates its thread via
  `_options.MaxStackSize > 0 ? new Thread(Process, _options.MaxStackSize) : new Thread(Process)`.
- `ExecutionWorkerPool<TSession>` copies `MaxStackSize` into each child worker's
  `ExecutionWorkerOptions` and preserves it in `Snapshot()`.

### 2. `OnWorkerThreadStart` and `CreateWorkerThreadScope`

Add worker-thread lifecycle callbacks to `ExecutionWorkerOptions` and
worker-slot-indexed callbacks (`0..WorkerCount - 1`) to
`ExecutionWorkerPoolOptions`:

```csharp
// ExecutionWorkerOptions
public Action? OnWorkerThreadStart { get; set; }
public Func<IDisposable?>? CreateWorkerThreadScope { get; set; }

// ExecutionWorkerPoolOptions
public Action<int>? OnWorkerThreadStart { get; set; }
public Func<int, IDisposable?>? CreateWorkerThreadScope { get; set; }
```

Execution order on the dedicated worker thread inside `ExecutionWorker.Process`:

1. `BeforeThreadStart` runs synchronously on the caller thread before
   `new Thread(...)` / `thread.Start(...)` ([ADR-0013](0013-before-thread-start-hook.md)).
2. On the dedicated worker thread (with `ApartmentState.STA` already applied when
   `UseStaThread = true`), `OnWorkerThreadStart` runs **once**.
3. Immediately afterward — before `EnsureSessionCreated` —
   `workerThreadScope = CreateWorkerThreadScope?.Invoke()` runs **once** and
   stays active across all session recyclings (`MaxOperationsPerSession` and
   `RecycleSessionOnFailure`).
4. During worker teardown (`TeardownWorkerLoop`), **strictly after**
   `DisposeSession()` finishes, `workerThreadScope?.Dispose()` runs **once** on
   the same dedicated worker thread before `_workerExitCompletionSource`
   completes.
5. Any exception thrown by `OnWorkerThreadStart`, `CreateWorkerThreadScope`, or
   `workerThreadScope.Dispose()` still cleans up any created scope, latches
   terminal `Fault` via `SetFatalFailure`, and settles startup/pending items
   without crashing the process with an unhandled thread exception.

## Consequences

- **Platform semantics of `new Thread(..., maxStackSize)` apply.** Values smaller
  than the OS minimum stack size are clamped upward by the runtime/OS per
  standard .NET `Thread` behavior; `0` uses the runtime default unchanged.
- **Clear separation of hook responsibilities.** `BeforeThreadStart` handles
  caller-side process preparation before thread spawn, `OnWorkerThreadStart` /
  `CreateWorkerThreadScope` own once-per-thread setup and teardown on the
  dedicated thread, and `IContextualExecutionSessionFactory<TSession>` owns
  per-generation session creation and disposal.

## Benefits

- Enables deterministic stack sizing for 32-bit native/COM worker pools.
- Allows thread-affine STA, locale, activation, and native guard `IDisposable`
  scopes from sibling interop libraries to span an entire worker thread's
  lifetime with guaranteed same-thread disposal after final session teardown.
