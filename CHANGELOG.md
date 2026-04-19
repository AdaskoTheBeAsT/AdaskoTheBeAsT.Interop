# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### `AdaskoTheBeAsT.Interop.Execution` (core)

- **Zero-allocation `ExecuteValueAsync` hot path** on `NET8_0_OR_GREATER`.
  `ExecutionWorker<TSession>` and `ExecutionWorkerPool<TSession>` now expose
  instance `ExecuteValueAsync` overloads backed by pooled
  `IValueTaskSource<TResult>` / `IValueTaskSource` work items
  (`ManualResetValueTaskSourceCore<T>`). The existing
  `ExecuteValueAsync` extension methods automatically dispatch to the
  zero-alloc instance methods when the receiver is a concrete worker/pool;
  on older TFMs and for custom `IExecutionWorker<TSession>` implementations
  the extensions keep wrapping the returned `Task` in a `ValueTask`.
  See [`docs/adr/0007-zero-alloc-value-task-source.md`](docs/adr/0007-zero-alloc-value-task-source.md).
- **`ExecutionWorkerPool<TSession>` single-factory constructors.** Two new
  overloads accept a single `IExecutionSessionFactory<TSession>` that is
  shared across every pool worker, eliminating the `Func<int, factory>`
  boilerplate at call sites that do not need per-worker isolation. The
  original per-index factory constructors remain for callers that do.
  See [`docs/adr/0008-uniform-snapshot-surface.md`](docs/adr/0008-uniform-snapshot-surface.md).
- **Unified `Name` / `GetSnapshot` observability surface** on both worker
  and pool:
  - `IExecutionWorker<TSession>.Name` and `IExecutionWorkerPool<TSession>.Name`;
  - `ExecutionWorkerSnapshot` (public `readonly struct`) with `Name`,
    `QueueDepth`, `IsFaulted`, `Fault`;
  - `ExecutionWorkerPoolSnapshot` (public `readonly struct`) with `Name`,
    `WorkerCount`, aggregate `QueueDepth`, `IsAnyFaulted`, and an
    index-aligned `IReadOnlyList<ExecutionWorkerSnapshot> Workers`;
  - `IExecutionWorker<TSession>.GetSnapshot()` and
    `IExecutionWorkerPool<TSession>.GetSnapshot()` capture all fields in a
    single call so dashboards cannot observe inconsistent mixes of values.
- **Scoped `ExecutionDiagnostics`** public class replaces the previous
  process-wide static diagnostics class. `ExecutionDiagnostics.Shared` is a
  lazy singleton that continues to emit under
  `ExecutionDiagnosticNames.SourceName`, so every existing OpenTelemetry /
  `MeterListener` / `ActivityListener` subscriber by that name is unaffected.
  New overloads on both `ExecutionWorkerOptions` and
  `ExecutionWorkerPoolOptions` accept a custom `ExecutionDiagnostics` scope;
  workers wired to a custom scope do not contribute measurements to
  `Shared`, removing the parallel-test-host race on the previously shared
  `Counter<long>` / `ObservableGauge<int>` state.
  See [`docs/adr/0009-scoped-execution-diagnostics.md`](docs/adr/0009-scoped-execution-diagnostics.md).

### Fixed

#### `AdaskoTheBeAsT.Interop.Execution` (core)

- **`WorkerFaulted` / `IsFaulted` publication ordering.** `ExecutionWorker<TSession>.SetFatalFailure`
  now dispatches the `WorkerFaulted` event *before* writing the fault through
  the volatile `_fatalFailure` field. This establishes a happens-before
  chain so any observer that sees `IsFaulted == true` (or
  `IsAnyFaulted == true` on a pool) has also seen every `WorkerFaulted`
  subscriber complete. The previous ordering made it possible for a caller
  polling `IsFaulted` to read `true` during the narrow window between the
  volatile write and the event dispatch, and assert on subscriber state
  that had not yet been produced. `WorkerFaultedEventArgs` still carries
  the exception, so event handlers do not rely on `IsFaulted` / `Fault`
  inside their own scope.

## [1.0.0] - TBD

Initial public release of the `AdaskoTheBeAsT.Interop.Execution` family.

### Added

#### `AdaskoTheBeAsT.Interop.Execution` (core)

- `ExecutionWorker<TSession>`: single dedicated background `Thread` that owns
  one `TSession` and serializes submitted work in FIFO order over a
  multi-writer / single-reader `System.Threading.Channels.Channel<T>`.
- `ExecutionWorkerPool<TSession>`: fan-out pool of workers, each with a
  private session and private queue.
- `IExecutionWorker<TSession>` and `IExecutionWorkerPool<TSession>` interfaces
  (both `IDisposable` + `IAsyncDisposable`) so callers can mock and DI can
  inject.
- `IExecutionSessionFactory<TSession>` seam for session create / dispose,
  always invoked on the dedicated worker thread.
- `ExecutionWorkerOptions` and `ExecutionWorkerPoolOptions` with a
  parameterless constructor plus positional constructor, `Validate()`
  invariants, and public setters so both bind via
  `Microsoft.Extensions.Options.IOptions<T>`.
- `ExecutionRequestOptions` per-call knob (`RecycleSessionOnFailure`).
- Pluggable `IWorkerScheduler<TSession>` with two built-ins:
  `RoundRobinWorkerScheduler<TSession>` and
  `LeastQueuedWorkerScheduler<TSession>` (default); faulted workers are
  skipped while at least one healthy worker remains.
- STA opt-in on Windows via `ExecutionWorkerOptions.UseStaThread`; silently
  ignored on non-Windows.
- Cooperative cancellation for both pending and in-flight work items.
- Session recycling: on opted-in failure
  (`ExecutionRequestOptions.RecycleSessionOnFailure`) and on reaching
  `MaxOperationsPerSession`.
- Terminal faulting model: `IsFaulted`, `Fault`, and a one-shot
  `WorkerFaulted` event aggregated at the pool level
  (`IsAnyFaulted`, `WorkerFaults`).
- `ValueTask` hot-path extensions (`ExecuteValueAsync`) for callers already
  living in the `ValueTask` domain.
- Synchronous `Dispose()` bounded by `DisposeTimeout`; asynchronous
  `DisposeAsync()` always waits for full drain. Reentrant `Dispose()` from
  inside a work item is safe.
- Observability via `System.Diagnostics.ActivitySource` and
  `System.Diagnostics.Metrics.Meter`:
  - activity `ExecutionWorker.Execute`
  - counters `execution.worker.operations` and
    `execution.worker.session_recycles`
  - observable gauge `execution.worker.queue_depth`
  - stable public `ExecutionDiagnosticNames` constants so telemetry
    consumers never hardcode strings.
- Multi-target matrix: `net10.0`, `net9.0`, `net8.0`, `net481`, `net48`,
  `net472`, `net471`, `net47`, `net462`, `netstandard2.0`. Every TFM is
  exercised in CI.

#### `AdaskoTheBeAsT.Interop.Execution.DependencyInjection`

- `IServiceCollection.AddExecutionWorker<TSession>(Action<ExecutionWorkerOptions>?)`
  extension.
- `IServiceCollection.AddExecutionWorkerPool<TSession>(Action<ExecutionWorkerPoolOptions>?)`
  extension.
- Options bound via `IOptions<ExecutionWorkerOptions>` /
  `IOptions<ExecutionWorkerPoolOptions>`.
- Same TFM matrix as the core package.

#### `AdaskoTheBeAsT.Interop.Execution.Hosting`

- `ExecutionWorkerHostedService<TSession>` / `ExecutionWorkerPoolHostedService<TSession>`
  `IHostedService` wrappers that call `InitializeAsync` on host start and
  `DisposeAsync` on host stop so the dedicated worker thread drains before
  the host exits.
- `AddExecutionWorkerHostedService<TSession>()` /
  `AddExecutionWorkerPoolHostedService<TSession>()` registration helpers.
- Same TFM matrix as the core package.

### Build / packaging

- `TreatWarningsAsErrors=true`, `GenerateDocumentationFile=true`,
  `ContinuousIntegrationBuild=true`, and `Deterministic=true` in CI for all
  three packages.
- Symbols shipped as `.snupkg` with Source Link and `EmbedUntrackedSources`,
  so consumers can step into library code from any debugger.
- Analyzer posture: StyleCop, SonarAnalyzer, Roslynator, Meziantou,
  Microsoft.CodeAnalysis.NetAnalyzers, Microsoft.VisualStudio.Threading,
  IDisposableAnalyzers, SecurityCodeScan, Puma.Security, AsyncFixer,
  Asyncify, CodeCracker, ConcurrencyLab.ParallelChecker, ReflectionAnalyzer;
  every suppression carries a justifying comment.

[Unreleased]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/AdaskoTheBeAsT/AdaskoTheBeAsT.Interop/releases/tag/v1.0.0
