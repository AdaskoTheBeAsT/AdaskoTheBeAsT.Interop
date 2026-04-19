# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### `AdaskoTheBeAsT.Interop.Execution` (core)

- **Zero-allocation `ExecuteValueAsync` hot path on every supported TFM.**
  `ExecutionWorker<TSession>` and `ExecutionWorkerPool<TSession>` expose
  instance `ExecuteValueAsync` overloads backed by pooled
  `IValueTaskSource<TResult>` / `IValueTaskSource` work items
  (`ManualResetValueTaskSourceCore<T>`). The `IValueTaskSource` primitives
  ship natively from `net8.0+` and are available on `net462`..`net481`
  through the `System.Threading.Tasks.Extensions` NuGet facade (pulled
  in transitively by `System.Threading.Channels`), so the same zero-alloc
  code path runs on all nine TFMs — no `Task` → `ValueTask` wrapper
  fallback anywhere, and no public `ExecutionWorkerValueTaskExtensions`
  static class to duplicate the API surface.
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

### Quality

- **SonarCloud PR sweep — 123 INFO-severity findings addressed.** All
  open code-smells reported on the pull request were either fixed or
  intentionally suppressed with explanatory comments. Highlights:
  - **Async-dispose preference (60 findings: `MA0042` + `RCS1261`).**
    Converted `using var worker = new ExecutionWorker<T>(...)` and
    `using var workerPool = new ExecutionWorkerPool<T>(...)` to
    `await using` in every async test method. The two intentional
    `Dispose()` timeout tests keep sync disposal under file-scoped
    pragmas (`MA0042`, `RCS1261`, `IDISPxxx`, `VSTHRD103`, `S6966`,
    `S125`) because they specifically exercise the synchronous
    timeout branch.
  - **Primary constructors (`IDE0290`, 11 findings).** `ExecutionWorkerHostedService`,
    `ExecutionWorkerPoolHostedService`, `ExecutionWorker.ExecutionWorkItem<T>`
    nested class, `ExecutionWorkerRegistration`, `ExecutionWorkerSnapshot`,
    `WorkerFaultedEventArgs`, plus test sessions (`IntegrationSession`,
    `DiTestSession`, `DiSecondTestSession`, `HostedTestSession`,
    `MeterSnapshot.RecordedMeasurement`) all moved to primary
    constructors with field-style initializers that preserve every
    `ArgumentNullException` guard.
  - **Auto-properties (`RCS1085`, 3 findings).** `ExecutionDiagnostics`
    converted `_activitySource`, `_operationsCounter`, and
    `_sessionRecyclesCounter` private fields into auto-implemented
    properties.
  - **FluentAssertions style (`FAA0001`, 9 findings).** Replaced
    `result.Should().BeGreaterThan(0)` with `result.Should().BePositive()`
    and replaced `observedSessionIds[i].Should().Be(x)` with
    `observedSessionIds.Should().HaveElementAt(i, x)`.
  - **Collection initializers (`IDE0028` / `IDE0300` / `IDE0301`,
    8 findings).** `Array.Empty<T>()` and explicit `new[] { ... }`
    occurrences in scheduler tests, `ExecutionWorkerPoolSnapshot`'s
    `EmptyWorkers` field, and `MeterSnapshot._measurements` switched
    to the `[]` collection-expression syntax.
  - **`ValueTask` discards (`CA2012`, 5 findings).** Pragma-suppressed
    around the explicit-throw assertions in
    `ExecuteValueAsync_ShouldThrowWhenActionDelegateIsNull`,
    `ExecuteValueAsync_ShouldThrowObjectDisposedExceptionAfterDispose`,
    and the foreign-scheduler / null-scheduler pool tests, where
    the test contract is that the call throws synchronously and
    no usable `ValueTask` is ever produced.
  - **Named arguments (`MA0003`, 3 findings).** `throw new ArgumentException(...)`
    in `ExecutionWorker.Process`, `new ExecutionWorkerSnapshot(...)`
    in scheduler test stubs, and `new ManualResetEventSlim(false)`
    in dispose-timeout tests now spell parameters by name.
  - **Polyfill in canonical namespace (`IDE0130`, `MA0182`, `RCS1251`).**
    `Polyfills/IsExternalInit.cs` MUST live in
    `System.Runtime.CompilerServices` for the C# compiler to
    recognize it as the init-only marker on legacy `net46x` / `net47x` /
    `net48x` TFMs; the three rules are pragma-suppressed at file
    scope with an inline justification comment.
  - **Documentation langword (`MA0154`, 2 findings).** `ExecutionWorkerOptions`
    and `ExecutionWorkerPoolOptions` use `<see langword="new"/>`
    instead of `<c>new</c>` in their summaries.
  - **Misc fixes:** unused `workerIndex` parameter in
    `ExecutionWorkerServiceCollectionExtensions` lambda renamed to
    `_` (`RCS1163`); cancellation overload added to
    `TaskCompletionSource.TrySetCanceled` in `ExecutionHelpers`
    polyfill (`MA0040`); culture-invariant `int.ToString` in
    interpolated assertion message (`MA0076`); `if/else if` ladder
    in `Initialize_ShouldDisposeAlreadyStartedWorkers...` test
    rewritten as a `switch` (`CC0019`); single-statement lambdas in
    multi-threaded / value-task tests collapsed to expression-bodied
    form (`RCS1021`); `IDE0042` deconstructed `requiredTags` foreach
    in `MeterSnapshot.MatchesTags`; `RCS1118` upgraded
    `expectedSessionCount` to a `const` in
    `SessionRecyclingExecutionWorkerTest`; `RCS1205` reordered
    named arguments in `MultiThreadedExecutionWorkerTest`; `CA1806`
    discarded `new ExecutionDiagnostics(...)` in
    `ScopedDiagnosticsTest` lambdas (with `IDISP004` co-suppressed
    because the constructor throws before producing a disposable).
- **Quality Gate: OK.** All 6 conditions passed; 100 % new-code
  coverage; A ratings on Reliability, Maintainability, and Security.

### Tests

- **Coverage raised further from ~92% to ~96.5% overall** by adding a second
  round of 17 tests targeting the remaining defensive branches:
  - `ExecutionWorker.InitializeAsync` pre-cancelled token fast-path
    (`Task.FromCanceled`).
  - `ExecutionWorker.SetFatalFailure` double-fire idempotency — the
    `WorkerFaulted` event raises exactly once thanks to the CAS in
    `RaiseFaultedOnce`.
  - `ExecutionWorker.ProcessWorkItem` success + failure paths under a
    registered `ActivityListener` (both `Activity.Status = Ok` and
    `Activity.Status = Error` branches of the `SetStatus` calls).
  - Synchronous `ExecutionWorker.Dispose` + `ExecutionWorkerPool.Dispose`
    timeout branches: a blocking `IExecutionSessionFactory` wedges the
    worker thread; `Dispose` abandons the wait after
    `DisposeTimeout` without throwing.
  - `ExecutionWorkerPool.IsAnyFaulted` returns `false` for a healthy pool
    (post-loop `return false` branch).
  - `ExecutionWorkerPool.SetWorkerThreadForTesting` argument-null guard.
  - Pool `ExecuteAsync` and `ExecuteValueAsync` both throw
    `InvalidOperationException("The worker scheduler returned null.")`
    when a custom scheduler violates the contract.
  - Direct unit tests on internal pooled work items covering:
    explicit non-generic `IValueTaskSource` interface on
    `PooledValueExecutionWorkItem<,>` (`GetStatus` / `OnCompleted` /
    `GetResult`), `TrySetCanceled` → cancelled `ValueTask(<T>)`, the
    stale-token guard in `GetResult` (`token != Version` mismatch), the
    defensive `_action is null` branch (rented with a null delegate),
    and the `Options` + `CancellationToken` property accessors.
- **Coverage raised from ~86% to ~92% overall** by adding ~45 focused tests:
  - `TrivialCoverageTest` and `DiagnosticsAndHelpersTest` cover all the
    small defensive branches in `ExecutionWorkerOptions` /
    `ExecutionWorkerPoolOptions` (negative `DisposeTimeout`, undefined
    `SchedulingStrategy`, fresh-instance `Default`),
    `WorkerFaultedEventArgs` (null-exception guard), `ExecutionHelpers`
    (null-action guard), `ExecutionWorkerRegistration` (null-accessor
    guard), `ExecutionDiagnostics` (null/whitespace source name,
    idempotent Dispose, no-op Dispose on `Shared`, null-registration
    guards), `ExecutionWorkerPoolSnapshot` (null-workers fallback,
    aggregation), and both `RoundRobinWorkerScheduler` /
    `LeastQueuedWorkerScheduler` (null / empty / single-worker /
    all-faulted / healthy-lowest-depth paths).
  - New `ExecutionWorkerHostingExtensionsTest` covers the two
    `services is null` guards.
  - New null / pre-cancelled / post-dispose / channel-closed tests for
    `ExecutionWorker.ExecuteValueAsync` (void + `TResult`).
  - Pool-level tests now exercise the shared-factory constructor
    (with and without an explicit scheduler), `ExecuteValueAsync` void
    and `TResult` routing through `SelectConcreteWorker`, and the
    foreign-worker `InvalidOperationException` branch via a custom
    `IExecutionWorker<T>` stub that is deliberately not an
    `ExecutionWorker<T>`.
  - DI-level tests now cover the `IOptionsFactory<T>` fallback branch
    (by removing the open-generic `IOptionsMonitor<>` descriptor
    before building the provider), the unnamed-`IOptions<T>` fallback
    when no configure delegate is supplied, and the
    `ExecutionWorkerOptions.Default` fallback when nothing at all is
    registered. Same coverage is added for the pool pipeline.

### Removed

#### `AdaskoTheBeAsT.Interop.Execution` (core)

- **`ExecutionWorkerValueTaskExtensions` public static class** — deleted.
  Its only job was to dispatch `.ExecuteValueAsync(...)` calls on
  `IExecutionWorker<T>` / `IExecutionWorkerPool<T>` to the concrete type's
  instance method when on `net8.0+`, and on older TFMs to wrap
  `ExecuteAsync(...)`'s `Task` in a `ValueTask` as a best-effort ergonomic
  fallback (not zero-alloc). Now that the instance `ExecuteValueAsync`
  overloads are unconditionally available on every TFM, the extension
  added nothing but a second public-API surface and a `Task → ValueTask`
  wrapper on the slow path for third-party `IExecutionWorker<T>`
  implementations — callers can write `new ValueTask(worker.ExecuteAsync(...))`
  themselves with identical semantics.

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
- **`ExecutionWorkerPool<TSession>.Dispose` / `DisposeAsync` now share a
  single in-flight drain `Task`.** Previously a caller whose synchronous
  `Dispose()` timed out via `DisposeTimeout` would have the `_asyncDisposed`
  interlocked flag flipped to 1, so any subsequent `await pool.DisposeAsync()`
  became an instant no-op even though the background drain was still
  running. The pool now caches the dispose `Task` on first invocation
  behind `_disposeLock`, and both the sync and async paths await the same
  instance — callers who await `DisposeAsync()` after a timed-out
  `Dispose()` correctly observe drain completion (or the fault) instead of
  a silent early return.
- **`MeterSnapshot.Last()` determinism.** The test helper used a
  `ConcurrentBag<RecordedMeasurement>` whose enumeration order is
  thread-local-stack-based, not insertion-ordered. `Last()` therefore
  returned whichever thread's local slot happened to be iterated last —
  non-deterministic, and the root cause of intermittent flakes on
  telemetry assertions under parallel test-host load. Replaced with a
  coarse-locked `List<T>` whose reverse-iteration returns the strictly
  last-inserted matching measurement.
- **`ExecutionWorkerOptions.Default` is no longer a mutable shared
  singleton.** Changed from `static ExecutionWorkerOptions Default { get; } = new();`
  to `static ExecutionWorkerOptions Default => new();` so every fallback
  site (null-coalescing path in `ExecutionWorker` ctor; DI extension
  fallback) receives a fresh instance. Prevents accidental global-state
  poisoning via e.g. `ExecutionWorkerOptions.Default.DisposeTimeout = TimeSpan.Zero`.
- **NuGet package metadata mismatch.** All three packages'
  `<Description>` strings, the README TFM matrix, and the DI-test
  conditional `ItemGroup` still mentioned `netstandard2.0`, which was
  dropped from `TargetFrameworks` earlier. Descriptions and metadata are
  now aligned with the actual shipping 9-TFM matrix (`net10.0`,
  `net9.0`, `net8.0`, `net481`, `net48`, `net472`, `net471`, `net47`,
  `net462`).
- **DI: `AddExecutionWorker<TSession>` / `AddExecutionWorkerPool<TSession>`
  no longer leak `configure` delegates across different `TSession` types.**
  Previously the extension called `services.Configure(configure)` which
  pushed the delegate into the single global
  `IOptions<ExecutionWorkerOptions>` (or `...PoolOptions`) pipeline, so
  multiple `AddExecutionWorker<T>(...)` calls for different `TSession`
  types stacked their delegates and produced a single merged options
  instance shared by every worker (last-wins on `Name` + union of the
  other properties). The extension now registers a *named* options entry
  keyed by `typeof(TSession).FullName`, and the factory resolves via
  `IOptionsMonitor<T>.Get(name)` so each `TSession` binding receives its
  own isolated options. Consumers who only call `AddExecutionWorker<T>`
  once (the common case) observe no behavioural change; consumers who
  pre-bind configuration to the unnamed `IOptions<T>` pipeline (i.e. do
  not pass a `configure` delegate) continue to get the existing shared
  behaviour. Two new regression tests
  (`AddExecutionWorker_ShouldIsolateOptionsAcrossDifferentSessionTypesAsync`,
  `AddExecutionWorkerPool_ShouldIsolateOptionsAcrossDifferentSessionTypesAsync`)
  pin the isolation contract.

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
  `net472`, `net471`, `net47`, `net462`. Every TFM is exercised in CI.

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
