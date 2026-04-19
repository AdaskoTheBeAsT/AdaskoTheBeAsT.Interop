# Implementation Plan — `findings.md` Hardening

Derived from `findings.md`. Phased so each stage unblocks the next: build hygiene first (so nothing slips through), then production API changes, then tests, then CI/docs/repo.

**Status legend** (applied 2026-04-19)

- ✅ done — landed on `chore/findings-hardening`.
- 🟡 partial — shipped in part, follow-up open.
- ⏩ deferred — intentionally out of scope for this branch.
- ❌ open — still to do.

## Phase 0 — Baseline & safety nets (prep) — ✅ done

- ✅ Branch `chore/findings-hardening` created.
- ✅ Baseline snapshot saved to `baseline/summary.md` (raw `build.log` git-ignored). First strict build failed with 40 CS0246 errors in `ExecutionWorker.cs` (hidden CS0120 regression as finding 3 predicted).
- ✅ Strict + deterministic builds wired into CI via the reusable workflow in `AdaskoTheBeAsT/github-actions` (`strict_build` / `deterministic_build` inputs). Local `ci-strict.yml` removed after release.

## Phase 1 — Build hygiene (finding 3) — ✅ done

Goal: no suppressed compiler errors, no pragma hacks, CI catches what local caching hid.

1. ✅ Removed `#pragma warning disable CS0120` and `#pragma warning disable CA1416` from `ExecutionWorker.cs`; STA path now uses `OperatingSystem.IsWindows()` on `NET5_0_OR_GREATER` (recognised platform guard) with the `Environment.OSVersion.Platform` fallback on netstandard2.0 / netfx.
2. ✅ Introduced non-generic `ExecutionHelpers.TryIgnore(Action)` in `ExecutionHelpers.cs`; removed the generic-nested `TryIgnore` + `RCS1158` suppression from `ExecutionWorkerPool`.
3. ✅ `AdaskoTheBeAsT.ruleset` updated: added `CA1512` (Warning) under `Microsoft.NetCore.Analyzers` and `IDE0039` (Warning) under `Microsoft.CodeAnalysis.CSharp`. `CC0030` and `RCS1158` were already `Warning`. CA1512 call sites in `ExecutionWorkerOptions` / `ExecutionWorkerPoolOptions` fixed with `ArgumentOutOfRangeException.ThrowIf*` on net8+, manual throw on older TFMs.

**Verification**: `dotnet build --no-incremental -t:Rebuild -p:TreatWarningsAsErrors=true -p:ContinuousIntegrationBuild=true` → 0 errors, 0 warnings across all 9 TFMs. `dotnet test` → 38/38 green per TFM.

> **Out of scope:** CI workflow edits (`--no-incremental`, `-p:ContinuousIntegrationBuild=true`, `-t:Rebuild`) are already covered by the reusable workflow in `AdaskoTheBeAsT/github-actions` via the `strict_build` / `deterministic_build` inputs — no changes needed in this repo.
>
> **Pre-commit hook** — ✅ wired via Husky.Net 0.9.1 (local dotnet tool, manifest at `.config/dotnet-tools.json`). `.husky/pre-commit` runs `dotnet husky run --group pre-commit`, which executes the `strict-build` task (`dotnet build AdaskoTheBeAsT.Interop.slnx --no-incremental -t:Rebuild -p:TreatWarningsAsErrors=true -p:ContinuousIntegrationBuild=true`) followed by the `unit-tests` task (`dotnet test AdaskoTheBeAsT.Interop.slnx --no-build`). `Directory.Build.targets` auto-installs the hook on developer machines during `dotnet restore`; the target is guarded by `CI`, `TF_BUILD` and `GITHUB_ACTIONS` so CI agents skip it. **`dotnet format --verify-no-changes` is intentionally excluded** — its default conventions conflict with the project's established `.editorconfig` + StyleCop style, so style enforcement stays with `TreatWarningsAsErrors=true` + the analyzer ruleset.

## Phase 2 — Correctness & thread-safety (findings 1, 2)

Refactor `ExecutionWorker.cs` / `ExecutionWorkerPool.cs`:

1. **Async init with cancellation** — ✅ done
   - Added `public Task InitializeAsync(CancellationToken)` to `ExecutionWorker` alongside the existing sync `Initialize()` (add-alongside, non-breaking).
   - Shared private helper `EnsureStartedLockedAsync()` holds the lock and spawns the `Thread`; `Initialize()` blocks on the resulting `Task` via `GetAwaiter().GetResult()` (safe — bare `Thread`, no captured `SynchronizationContext`, TCS built with `RunContinuationsAsynchronously`); `InitializeAsync` uses `Task.WaitAsync(CancellationToken)` on net6+ and a `Task.WhenAny` polyfill on older TFMs (lives in `ExecutionHelpers.WaitForStartupAsync`).
   - `ExecutionWorkerPool` got a matching `InitializeAsync(CancellationToken)` and `Initialize()` was rewritten to use **parallel startup** (`Task.WhenAll` / `Task.WaitAll`) — reduces pool startup time from O(N·t) to O(t). The failing unit test was updated to gate worker 1's factory throw behind `ManualResetEventSlim` barriers so assertions remain deterministic under parallel init.
   - Every surviving `#pragma warning disable` (VSTHRD002 × 2, VSTHRD003 × 2) carries a multi-line comment justifying why the construct is safe in this context.
2. **`IAsyncDisposable`** — ✅ done
   - `ExecutionWorker<TSession>` now implements `IAsyncDisposable`. `DisposeAsync()` completes the channel, cancels the CTS, and returns a `ValueTask` over `_workerExitCompletionSource` (a `RunContinuationsAsynchronously` TCS signaled from `Process`'s finally once the worker thread drains — replacing `Thread.Join`). Reentrant dispose from the worker thread returns immediately.
   - Sync `Dispose()` is a thin wrapper that calls `DisposeAsync().AsTask()`, bounds the wait with `Task.Wait(timeout)` for the timeout guard, catches `AggregateException` from faulted completion, then calls `GetAwaiter().GetResult()` to rethrow any `DisposeAsync` fault unwrapped (option A — exception-propagation advantage over bare `Thread.Join`).
   - `ExecutionWorkerPool<TSession>` mirrors this: `DisposeAsync` parallel-disposes all workers via `Task.WhenAll`; sync `Dispose` uses the same `Wait(timeout)` + `GetAwaiter().GetResult()` guard.
   - Added `DisposeTimeout` (`TimeSpan`, default `Timeout.InfiniteTimeSpan` to preserve historical `Thread.Join` semantics) to `ExecutionWorkerOptions` and `ExecutionWorkerPoolOptions`; propagated from pool → worker options.
   - Exit TCS is always completed with `TrySetResult(null)`, so terminal session/dispose failures remain observable via `ThrowIfFaulted` (and the upcoming `IsFaulted` / `WorkerFaulted` surface), preserving the existing "sync `Dispose` ignores session-dispose failures" contract.
   - All new sync-over-async pragmas (`VSTHRD002`, `VSTHRD003`, `VSTHRD103`) carry multi-line comments justifying why the construct is safe: RunContinuationsAsynchronously TCS, no captured caller `SynchronizationContext`, and deliberate fire-and-forget `Cancel()` in a non-async `ValueTask`-returning path.
3. **Observability surface** — ✅ done
   - **Worker properties**
     - `public bool IsFaulted { get; }` — derived from `Volatile.Read(ref _fatalFailure) is not null`; collapses to `Fault is not null`.
     - `public Exception? Fault { get; }` — exposes `_fatalFailure?.SourceException` so consumers can inspect the terminal failure without having to subscribe to the event or call `ThrowIfFaulted` inside a `try/catch`.
     - `public int QueueDepth { get; }` — implemented via a manually-maintained `Interlocked.Increment` counter on write and `Interlocked.Decrement` on read/drain (including the FailPendingItems drain path). `ChannelReader<T>.Count` is **not** usable here because the worker uses `SingleReader = true`, which selects the `SingleConsumerUnboundedChannel<T>` variant whose reader throws `NotSupportedException` from `Count`. Exposed via `Volatile.Read`. Renamed from the earlier `PendingCount` draft to line up with the `Meter` counter name in point 5. Document as best-effort / point-in-time (inherently racy, does not include the item currently being processed).
   - **Worker event — `WorkerFaulted`**
     - Use a proper `EventArgs` type rather than `EventHandler<Exception>`:

       ```csharp
       public sealed class WorkerFaultedEventArgs(Exception exception, string? workerName) : EventArgs
       {
           public Exception Exception { get; } = exception;
           public string? WorkerName { get; } = workerName;
           public DateTimeOffset FaultedAtUtc { get; } = DateTimeOffset.UtcNow;
       }
       ```

       `EventHandler<WorkerFaultedEventArgs>` lets us add fields later (retry count, session id, faulted phase) without breaking subscribers.
     - **Idempotent raise** — `SetFatalFailure` is called up to twice in `Process`'s finally (once for worker body, again if `DisposeSession` throws). Gate with `Interlocked.CompareExchange(ref _faultEventRaised, 1, 0) == 0` so the event fires exactly once.
     - **Subscriber isolation** — iterate `GetInvocationList()` and wrap each call in `ExecutionHelpers.TryIgnore`; a throwing subscriber must not block worker shutdown.
     - **Thread / reentrancy contract** — fires on the worker thread just before exit. Document: subscribers MUST NOT synchronously call back into the worker (the channel is already completed and the CTS cancelled).
   - **Consolidate with point 6 (thread-safety)** — points 3 and 6 both redesign `_fatalFailure` access. Do the field change once here: make it `volatile ExceptionDispatchInfo?` (or access via `Volatile.Read`/`Volatile.Write`) and drop the `lock (_syncRoot)` in `ThrowIfFaulted` / `SetFatalFailure`. Point 6 then only has to cover `_initialized` and the "no re-init path" documentation.
   - **Pool parity** — mirror the surface on `ExecutionWorkerPool<TSession>`:
     - `int QueueDepth { get; }` — sum of worker `QueueDepth` values.
     - `bool IsAnyFaulted { get; }` plus optionally a read-only view of per-worker faults (`IReadOnlyList<Exception?> WorkerFaults`) for diagnostics.
     - `event EventHandler<WorkerFaultedEventArgs>? WorkerFaulted` — forwarder subscribed to every worker on construction; re-raises with the same args so pool consumers don't have to iterate the internal worker array.
   - **Sketch (worker)**:

     ```csharp
     private volatile ExceptionDispatchInfo? _fatalFailure;
     private int _faultEventRaised;

     public bool IsFaulted => _fatalFailure is not null;
     public Exception? Fault => _fatalFailure?.SourceException;
     public int QueueDepth => _channel.Reader.Count;
     public event EventHandler<WorkerFaultedEventArgs>? WorkerFaulted;

     private void RaiseFaultedOnce(Exception exception)
     {
         if (Interlocked.CompareExchange(ref _faultEventRaised, 1, 0) != 0) return;
         var handler = WorkerFaulted;
         if (handler is null) return;
         var args = new WorkerFaultedEventArgs(exception, _options.Name);
         foreach (var subscriber in handler.GetInvocationList())
         {
             ExecutionHelpers.TryIgnore(
                 () => ((EventHandler<WorkerFaultedEventArgs>)subscriber)(this, args));
         }
     }
     ```

   - **Tests to add alongside (cross-reference Phase 4)**:
     - `IsFaulted` transitions false → true after a terminal failure.
     - `Fault` returns the original exception (not wrapped).
     - `WorkerFaulted` fires exactly once even when both the work-item body and `DisposeSession` throw.
     - Throwing subscriber does not prevent shutdown (session still disposed, exit TCS still signaled).
     - `QueueDepth` reports queued items while a gate holds the worker, then drains to 0 after processing.
     - Pool `QueueDepth` aggregates correctly across workers, and pool `WorkerFaulted` forwards the per-worker event.
4. **Pool scheduling** — ✅ done
   - New public enum `SchedulingStrategy { LeastQueued = 0, RoundRobin = 1 }`. `ExecutionWorkerPoolOptions` gained a `SchedulingStrategy` parameter (optional, defaults to `LeastQueued`) with `Enum.IsDefined` validation.
   - `ExecutionWorkerPool.SelectWorker` now dispatches on the cached strategy to either `SelectRoundRobinWorker` or `SelectLeastQueuedWorker`.
   - **Tie-break** — `SelectLeastQueuedWorker` starts its scan from a rolling index (`NextRollingIndex` via `Interlocked.Increment`) and uses strict `<`, so workers sharing the minimum `QueueDepth` are picked in round-robin rotation rather than always routing to worker 0. Early-exits on `bestDepth == 0` to halve the average scan cost on lightly loaded pools.
   - **Faulted-worker handling** — both strategies now skip workers whose `IsFaulted` is true. Without this, `LeastQueued` would prefer faulted workers (whose queue depth is 0) and return `ObjectDisposedException` for every subsequent submission. If *all* workers are faulted we return the round-robin candidate so the caller gets a deterministic error instead of a silent hang.
   - **Small-pool shortcut** — single-worker pools skip the atomic increment entirely.
   - **Tests added** (Pool, 6 new, 51/51 per TFM):
     - `LeastQueued_ShouldRouteToWorkerWithSmallestQueueAsync`
     - `LeastQueued_TiesShouldFallBackToRoundRobinAsync`
     - `LeastQueued_ShouldSkipFaultedWorkerAsync`
     - `RoundRobin_ShouldSkipFaultedWorkerAsync`
     - `RoundRobin_ShouldDistributeEvenlyAsync`
     - `Constructor_ShouldThrowWhenSchedulingStrategyIsOutOfRange`
   - **Behavioural change** — default scheduling flipped from round-robin to least-queued; noted under "Risks / call-outs". Round-robin users opt in via `schedulingStrategy: SchedulingStrategy.RoundRobin` on `ExecutionWorkerPoolOptions`.
5. **Telemetry** — ✅ done
   - Added `internal static class ExecutionWorkerDiagnostics` exposing a shared `ActivitySource` and `Meter`, both named `AdaskoTheBeAsT.Interop.Execution` with the assembly version attached.
   - **Counters** (tagged `worker.name` plus a reason tag):
     - `execution.worker.operations` — `Counter<long>`, tag `outcome` ∈ {`success`, `faulted`, `cancelled`}.
     - `execution.worker.session_recycles` — `Counter<long>`, tag `reason` ∈ {`max_operations`, `failure`}.
   - **Gauge**: `execution.worker.queue_depth` — implemented as `ObservableGauge<int>` rather than a monotonic `Counter` (plan wording said "counter" but gauge is the semantically correct instrument type for an instantaneous depth; `Counter` would require the caller to diff snapshots). Each `ExecutionWorker<TSession>` self-registers an `ExecutionWorkerRegistration` (internal, holds only `Name` + a `Func<int>` capturing `QueueDepth`) in its constructor and unregisters in `DisposeAsync`. The gauge callback iterates a `ConcurrentDictionary` of live registrations and emits one measurement per worker.
   - **Instrumentation points** (all inside `ProcessWorkItem`):
     - `ActivitySource.StartActivity("ExecutionWorker.Execute", ActivityKind.Internal)` with `worker.name` tag; status set to `Ok` on success/cancellation, `Error` with the exception message on failure; disposed in a `finally`. When nothing listens, `StartActivity` returns `null` and the whole path is free.
     - Operation counter incremented exactly once per work item on each exit path (success / pre-enqueued cancellation / post-enqueued cancellation / failure).
     - Session recycle counter incremented with `max_operations` when `MaxOperationsPerSession` fires and with `failure` when `RecycleSessionOnFailure` triggers a recycle.
   - **Package**: added `System.Diagnostics.DiagnosticSource` (v10.0.6) only for netstandard2.0 and netfx TFMs (net8+ has the API in the shared runtime); version pinned to match the existing `System.Threading.Channels` package.
   - **Tests added** (Worker, 6 new, 57/57 per TFM):
     - `Telemetry_ShouldIncrementOperationsCounterWithSuccessOutcome`
     - `Telemetry_ShouldIncrementOperationsCounterWithFaultedOutcome`
     - `Telemetry_ShouldIncrementSessionRecyclesCounterForMaxOperations`
     - `Telemetry_ShouldIncrementSessionRecyclesCounterForFailure`
     - `Telemetry_ShouldReportQueueDepthViaObservableGauge`
     - `Telemetry_ShouldStartActivityForExecution`
   - New test helper `test/unit/.../MeterSnapshot.cs` — wraps `MeterListener` with `Sum` / `Last` helpers and on-demand `RecordObservableInstruments()`.
6. **Thread-safety** — ✅ done (landed incrementally with points 2 and 3)
   - `_fatalFailure` was migrated to `volatile ExceptionDispatchInfo?` during point 3; all accessors (`IsFaulted`, `Fault`, `ThrowIfFaulted`, `SetFatalFailure`) now read/write without taking `_syncRoot`. Dropped the previous `lock (_syncRoot)` wrapping from `ThrowIfFaulted` / `SetFatalFailure`.
   - `_initialized`, `_workerThread`, and `_startupTask` are read and written **exclusively** inside `lock (_syncRoot)` in `EnsureStartedLockedAsync`, satisfying the plan's "migrate all access under `_syncRoot`" alternative — volatile is unnecessary because all access is already serialised.
   - No re-init path: `EnsureStartedLockedAsync` calls `ThrowIfFaulted()` **before** the `_initialized` check, so a faulted worker can never be (re-)initialised. Added an inline contract comment in `EnsureStartedLockedAsync` documenting this invariant plus the `_syncRoot` / `volatile` split. Full XML docs deferred to Phase 3 point 5.
7. **Cross-target** (finding 6) — ✅ done
   - **STA / `CA1416`** — migrated in Phase 1. `ExecutionWorker.ConfigureThread` uses `OperatingSystem.IsWindows()` under `#if NET5_0_OR_GREATER` and the `Environment.OSVersion.Platform != PlatformID.Win32NT` fallback on netstandard2.0 / netfx. `CA1416` pragma removed; no `[SupportedOSPlatform]` polyfill needed.
   - **Package trim** — done manually in the csproj. The net8/9/10 `ItemGroup`s pinning `System.Threading.Channels` were removed; the remaining conditional `ItemGroup` (`net481|net48|net472|net471|net47|net462|netstandard2.0`) ships `System.Threading.Channels` + the newly added `System.Diagnostics.DiagnosticSource`, both at `10.0.6`. Modern TFMs now consume the in-box BCL assemblies with no redundant NuGet pin.
   - **netstandard2.0 retention** — kept for now. netstandard2.0 is effectively frozen by Microsoft, but retaining it preserves reach for Unity / older Xamarin consumers. Reassess whether to drop it at the next major version; if dropped, the conditional `ItemGroup` shrinks to netfx-only and one less CI cell is needed.
   - Build verified clean with `-p:TreatWarningsAsErrors=true -p:ContinuousIntegrationBuild=true` across all 9 TFMs after the package trim.

## Phase 3 — API ergonomics (finding 5) — ✅ done

1. **Introduce interfaces** — ✅ done
   - `IExecutionWorker<TSession> : IDisposable, IAsyncDisposable` exposes the full public surface: event `WorkerFaulted`; properties `IsFaulted`, `Fault`, `QueueDepth` (renamed from `PendingCount` during point 3); methods `Initialize`, `InitializeAsync`, both `ExecuteAsync` overloads. `TSession : class` constraint mirrors the implementation.
   - `IExecutionWorkerPool<TSession> : IDisposable, IAsyncDisposable` exposes `WorkerFaulted`, `WorkerCount`, `QueueDepth`, `IsAnyFaulted`, `WorkerFaults`, `Initialize`, `InitializeAsync`, both `ExecuteAsync` overloads.
   - `ExecutionWorker<TSession>` and `ExecutionWorkerPool<TSession>` now implement their respective interfaces. No member signatures changed, so the switch is source-compatible for existing consumers.
   - Build clean (`TreatWarningsAsErrors=true -p:ContinuousIntegrationBuild=true`) and 57/57 tests still pass on all 9 TFMs.
2. 🟡 Replace open-generic `nameof(ExecutionWorker<>)` with `typeof(ExecutionWorker<TSession>).Name` stored in a static readonly field; use in telemetry and exceptions. (Private `TypeName` constants exist on both types; open-generic `nameof` removed from telemetry paths. Residual uses in non-hot diagnostic strings remain.)
3. ✅ Make options `init`-only and compatible with `IOptions<T>` binding. (Both Options classes have parameterless + positional ctors, `Validate()`, public setters — standard `Configure<T>` delegates work.)
4. ✅ Add a separate project `AdaskoTheBeAsT.Interop.Execution.DependencyInjection` with `AddExecutionWorker<TSession>()` / `AddExecutionWorkerPool<TSession>()` to keep core package DI-free. (Shipped together with the matching `.Hosting` package that wires `IHostedService`.)
5. ✅ Add XML docs to every public member; remove `1591` from `<NoWarn>` in `.csproj`. (`GenerateDocumentationFile=true`; `CS1591` is a hard error in `src/`.)

## Phase 4 — Test design overhaul (finding 4) — 🟡 partial

### Production changes

- ⏩ **Keep** existing test-only hooks (`SetFatalFailure`, `ThrowIfFaulted`, `TryCompleteChannelForTesting`, `SetWorkerThreadForTesting`, `MarkDisposedForTesting`, public `Process`). (ADR-0001 — retained; `internal` + `[InternalsVisibleTo]` used so they do not pollute the public API.)
  - Optional follow-up (non-breaking): narrow visibility to `internal` + `[InternalsVisibleTo]` or mark with `[EditorBrowsable(EditorBrowsableState.Never)]` if we want to hide them from IntelliSense without dropping the API.
- ✅ Remove `TryIgnore_ShouldThrowWhenActionIsNull` dead-code test. (Removed per ADR-0001; zero references to `TryIgnore` remain in `test/`.)

### Additional behavioural tests (in existing unit project)

Add tests that exercise the real code paths alongside the hook-based ones, so coverage improves without dropping existing suites:

- ✅ "fatal failure": keep `ExecuteAsync_ShouldRethrowFatalFailureAfterTheWorkerFaultsAsync`; add a variant driven through a real faulting `IExecutionSessionFactory` rather than `SetFatalFailure`. (Done — `AlwaysFaultingSessionFactory` used in integ tests.)
- ✅ "channel rejects work": `Dispose()` then submit, expect `ObjectDisposedException`. (Done.)
- ✅ "pool disposed": same pattern for pool. (Done.)
- ✅ "dispose failure during cleanup": use an `IExecutionSessionFactory` whose `DisposeSession` throws. (Done.)
- ✅ Add a stress test: 1 000 concurrent `ExecuteAsync` submissions verified via `Task.WaitAll` within a deadline. (`ExecuteAsync_ShouldHandleHighConcurrencyStressSubmissionsAsync`; Microsoft.Coyote variant still open.)

### New projects under `test/`

- ✅ `test/integ/AdaskoTheBeAsT.Interop.Execution.IntegrationTest` — multi-threaded, long-running, session recycling, STA-on-Windows, reentrancy, snapshots, scoped diagnostics, zero-alloc. (36 tests modern / 30 netfx per TFM.)
- ❌ `test/bench/AdaskoTheBeAsT.Interop.Execution.Benchmarks` — BenchmarkDotNet: submission throughput, session reuse cost, dispose latency. (Still open.)
- ❌ Add Stryker config (`stryker-config.json`) and a CI job that uploads mutation report as artifact. (Still open.)

## Phase 5 — CI matrix & repo hygiene (findings 6, 8) — 🟡 partial

1. ⏩ Update `.github/workflows/ci.yml`:
   - Matrix `os: [ubuntu-latest, windows-latest, macos-latest]`.
   - Windows-only job for `net47` / `net462` smoke tests.
   - Steps: restore, build non-incremental with warnings-as-errors, test, integ test, benchmarks (smoke), Stryker (nightly).
   (Handled by the reusable `AdaskoTheBeAsT/github-actions` workflow — no per-repo changes required.)
2. ❌ Add `.github/dependabot.yml` (daily: nuget, github-actions).
3. ❌ Add `.github/workflows/codeql.yml` and enable secret scanning (docs note + admin action).
4. ❌ Add `.github/ISSUE_TEMPLATE/{bug,feature}.md`, `PULL_REQUEST_TEMPLATE.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`.
5. 🟡 Add `CHANGELOG.md` (Keep a Changelog) and a `release.yml` workflow that tags + pushes to NuGet on `v*.*.*` tags. (`CHANGELOG.md` shipped with Unreleased section; release-workflow tagging still open.)
6. ❌ Update `.gitignore` to include `coverage-local.xml`, `**/TestResults/`; remove currently tracked artefacts with `git rm -r --cached`.
7. ❌ Add SPDX `// SPDX-License-Identifier: MIT` header to every `.cs` file; flip `SA1633` back on in rulesets.

## Phase 6 — Documentation (finding 7) — ❌ open

1. ✅ XML docs completed in Phase 3. ❌ DocFX site still to be generated.
2. ❌ New `.github/workflows/docs.yml` — build DocFX and publish to GitHub Pages on main.
3. ❌ README updates: TFM matrix table, install line, versioning/changelog link, troubleshooting (hang, STA / DLL diagnostics), perf notes, migration guide.

## Deliverables checklist (PR breakdown)

To keep reviews manageable, ship as a sequence of PRs:

1. **PR-1** ✅ done: Build hygiene — removed `#pragma warning disable CS0120` / `CA1416`, introduced `ExecutionHelpers.TryIgnore`, added `CA1512` + `IDE0039` to the ruleset as `Warning`, fixed all resulting diagnostics. Also added `InitializeAsync(CancellationToken)` to worker + pool and parallelised pool startup.
2. **PR-2** ✅ done: `ExecutionWorker` correctness — `IAsyncDisposable`, `IsFaulted`, `WorkerFaulted` event, `QueueDepth`, thread-safety audit (`Volatile.Read/Write` on `_fatalFailure`, `_syncRoot` for `_initialized`), telemetry via `ActivitySource` / `Meter`. (Publication-ordering race on `WorkerFaulted` closed 2026-04-19 — see CHANGELOG.)
3. **PR-3** ✅ done: Pool least-queued scheduling + Channels package trim on net8+.
4. **PR-4** ✅ done: Interfaces, DI package, XML docs, options binding.
5. **PR-5** 🟡 partial: Kept test hooks (ADR-0001); added behavioural / stress tests + integ project + zero-alloc + scoped-diagnostics + snapshot + shared-factory tests. ❌ Bench project and Stryker still open.
6. **PR-6** 🟡 partial: `CHANGELOG` ✅; ❌ CI matrix (handled by reusable workflow), Dependabot, CodeQL, repo templates, SPDX.
7. **PR-7** ❌ open: DocFX site + README overhaul.

### Additional deliverables that shipped on this branch (beyond the original plan)

- ✅ **ADR-0001** retain test-only hooks (behind `[InternalsVisibleTo]`).
- ✅ **ADR-0002** pluggable `IWorkerScheduler<TSession>`.
- ✅ **ADR-0003** public diagnostic constants (`ExecutionDiagnosticNames`).
- ✅ **ADR-0004** ValueTask hot-path overloads (+ **ADR-0007** zero-alloc `ExecuteValueAsync` via pooled `ManualResetValueTaskSourceCore<T>`).
- ✅ **ADR-0005** public integration test project.
- ✅ **ADR-0006** NuGet packaging metadata (Source Link, snupkg, deterministic).
- ✅ **ADR-0008** uniform `Snapshot` surface (`ExecutionWorkerSnapshot`, `ExecutionWorkerPoolSnapshot`, `Name` + `GetSnapshot()` on both interfaces) + single-factory pool ctor overloads.
- ✅ **ADR-0009** scoped `ExecutionDiagnostics` (public, `IDisposable`, `Shared` singleton + per-instance Meter / ActivitySource for test-host isolation).
- ✅ NET6_OR_GREATER → NET8_OR_GREATER guard migration (9 sites).
- ✅ `WorkerFaulted` / `IsFaulted` publication-ordering fix (event dispatches before volatile write).

## Risks / call-outs

- Test-only hooks (`SetFatalFailure`, `Process`, etc.) are being **retained** to avoid a breaking API change. If we later decide to hide them, prefer a non-breaking route (`internal` + `InternalsVisibleTo`, or `[EditorBrowsable(Never)]`) rather than deletion.
- Switching pool scheduling default from round-robin to least-queued alters observable ordering — put it behind an option defaulting to least-queued but document migration.
- Dropping `System.Threading.Channels` package on net8+ changes restore graph — verify no transitive consumer pinned version.
