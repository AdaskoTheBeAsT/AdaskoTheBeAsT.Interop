# ADR-0013 — Synchronous `BeforeThreadStart` startup ordering hook

- **Status**: Accepted
- **Date**: 2026-11-16
- **Related**: [`docs/plan-2026-11-14.md`](../plan-2026-11-14.md) (`EX-02`, `EX-06`), [ADR-0010](0010-worker-completion-and-lifecycle.md)

## Context

When hosting STA COM servers or native DLLs in Windows Desktop and Windows Nano
Server containers, certain process-wide or apartment-prerequisite steps — such
as activating registration-free COM manifests, configuring native DLL search
directories (`SetDefaultDllDirectories` / `AddDllDirectory`), or validating
catalog files — must finish **before** dedicated worker `Thread`s are created,
configured via `Thread.SetApartmentState(ApartmentState.STA)`, and started.

Two design constraints shaped how this hook fits into the existing lifecycle:

1. **Preserve eager session startup (`EX-02`).** `ExecutionWorker.Process`
   already creates its `TSession` eagerly on the dedicated worker thread before
   completing `_startupTask`, and `ExecutionWorkerPool.InitializeAsync` already
   starts all workers in parallel (`Task.WhenAll`). Adding a default-false
   `WarmUpSessionsOnStart` flag would regress existing eager readiness semantics.
2. **Zero platform or COM package coupling.** `AdaskoTheBeAsT.Interop.Execution`
   must remain free of Windows-specific COM or P/Invoke dependencies so cross-
   platform callers pay no dependency cost.

## Decision

Add an optional `Action? BeforeThreadStart` property to both
`ExecutionWorkerOptions` and `ExecutionWorkerPoolOptions`:

```csharp
public Action? BeforeThreadStart { get; set; }
```

- **Option snapshot propagation:** `ExecutionWorkerOptions.Snapshot()` and
  `ExecutionWorkerPoolOptions.Snapshot()` copy `BeforeThreadStart` so post-
  construction mutations on caller option instances cannot alter live workers or
  pools, while `IOptions<T>` configuration delegates in DI and Hosting flow
  through naturally.
- **Standalone worker ordering:** inside `ExecutionWorker<TSession>`, when the
  first startup or cold submission triggers thread creation, `BeforeThreadStart`
  is invoked **synchronously on the caller thread** before `new Thread(Process)`,
  `ConfigureThread` (`SetApartmentState(ApartmentState.STA)`), and
  `workerThread.Start(...)`. If `BeforeThreadStart` throws, no `Thread` is
  allocated or started and the exception propagates directly to the caller,
  leaving the worker unstarted so a subsequent attempt may retry if desired.
- **Pool single-invocation gate (`BeforeThreadStartGate`):**
  `ExecutionWorkerPool<TSession>` wraps the pool-level `BeforeThreadStart`
  callback in an internal thread-safe gate shared across its child workers.
  During `pool.Initialize()`, `pool.InitializeAsync()`, or concurrent cold
  `ExecuteAsync` / `ExecuteValueAsync` submissions, the pool callback executes
  **at most once** before the first worker thread is created; concurrent callers
  synchronize on the gate, and if the callback throws, the gate resets cleanly
  without starting any worker threads.

## Consequences

- **`BeforeThreadStart` runs on the caller thread, not the worker thread.**
  Thread-affine per-session initialization still belongs in
  `IExecutionSessionFactory<TSession>.CreateSession` (or
  `IContextualExecutionSessionFactory<TSession>.CreateSession`), which runs on
  the dedicated STA/MTA worker thread after `SetApartmentState`.
- **Blocking work in `BeforeThreadStart` delays thread creation.** Callbacks
  should perform fast, deterministic process/bootstrap preparation rather than
  long asynchronous I/O.
- **Existing eager startup (`EX-02`) is unchanged.** Once `BeforeThreadStart`
  succeeds and the worker thread starts, `InitializeAsync` still awaits eager
  session creation on every worker before reporting readiness.

## Benefits

- Eliminates startup races between process-global COM/DLL preparation and
  parallel STA worker thread creation without introducing platform-specific
  dependencies or changing existing eager startup behavior.
