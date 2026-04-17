# Implementation Plan — `findings.md` Hardening

Derived from `findings.md`. Phased so each stage unblocks the next: build hygiene first (so nothing slips through), then production API changes, then tests, then CI/docs/repo.

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
> **Also out of scope:** pre-commit hook via `husky.net` + `dotnet format --verify-no-changes` — the `dotnet format` conventions conflict with the project's established style. Will rely on `TreatWarningsAsErrors=true` in CI + the ruleset severities above to enforce quality.

## Phase 2 — Correctness & thread-safety (findings 1, 2)

Refactor `ExecutionWorker.cs` / `ExecutionWorkerPool.cs`:

1. **Async init with cancellation** — ✅ done
   - Added `public Task InitializeAsync(CancellationToken)` to `ExecutionWorker` alongside the existing sync `Initialize()` (add-alongside, non-breaking).
   - Shared private helper `EnsureStartedLockedAsync()` holds the lock and spawns the `Thread`; `Initialize()` blocks on the resulting `Task` via `GetAwaiter().GetResult()` (safe — bare `Thread`, no captured `SynchronizationContext`, TCS built with `RunContinuationsAsynchronously`); `InitializeAsync` uses `Task.WaitAsync(CancellationToken)` on net6+ and a `Task.WhenAny` polyfill on older TFMs (lives in `ExecutionHelpers.WaitForStartupAsync`).
   - `ExecutionWorkerPool` got a matching `InitializeAsync(CancellationToken)` and `Initialize()` was rewritten to use **parallel startup** (`Task.WhenAll` / `Task.WaitAll`) — reduces pool startup time from O(N·t) to O(t). The failing unit test was updated to gate worker 1's factory throw behind `ManualResetEventSlim` barriers so assertions remain deterministic under parallel init.
   - Every surviving `#pragma warning disable` (VSTHRD002 × 2, VSTHRD003 × 2) carries a multi-line comment justifying why the construct is safe in this context.
2. **`IAsyncDisposable`**: implement `DisposeAsync()` that completes the channel, awaits a `TaskCompletionSource` the worker signals on exit (replacing `Thread.Join`). Keep sync `Dispose()` as a thin wrapper using `GetAwaiter().GetResult()` with a timeout guard.
3. **Observability surface**:
   - `public bool IsFaulted { get; }` (reads `_fatalFailure` via `Volatile.Read`).
   - `public event EventHandler<Exception>? WorkerFaulted` raised once on terminal failure.
   - `public int PendingCount { get; }` backed by channel reader count.
4. **Pool scheduling**: replace round-robin in `ExecutionWorkerPool.Execute*` with least-queued selection using each worker's `PendingCount` (fall back to round-robin on ties). Keep round-robin behind an `ExecutionWorkerPoolOptions.SchedulingStrategy` enum for opt-in.
5. **Telemetry**: add `internal static class ExecutionWorkerDiagnostics` with `ActivitySource("AdaskoTheBeAsT.Interop.Execution")` and `Meter` counters `Operation`, `QueueDepth`, `SessionRecycles`. Wrap each operation with `Activity?.Start`, increment counters; no-op when not listened.
6. **Thread-safety**:
   - Mark `_initialized` and `_fatalFailure` with `Volatile.Read/Write` or migrate all access under `_syncRoot`.
   - Document worker as terminal once `_fatalFailure` is set (no re-init path) — simplest and safest, matches finding 2 option.
7. **Cross-target** (finding 6):
   - STA path already migrated to `OperatingSystem.IsWindows()` in Phase 1 — `CA1416` pragma gone, no `[SupportedOSPlatform]` polyfill needed for netfx.
   - Drop `System.Threading.Channels` NuGet on `net8.0`+; keep only for netstandard / netfx TFMs.

## Phase 3 — API ergonomics (finding 5)

1. Introduce interfaces `IExecutionWorker<TSession>` and `IExecutionWorkerPool<TSession>` exposing the public members (incl. new `IsFaulted`, `PendingCount`, `WorkerCount`, events, async dispose).
2. Replace open-generic `nameof(ExecutionWorker<>)` with `typeof(ExecutionWorker<TSession>).Name` stored in a static readonly field; use in telemetry and exceptions.
3. Make options `init`-only and compatible with `IOptions<T>` binding.
4. Add a separate project `AdaskoTheBeAsT.Interop.Execution.DependencyInjection` with `AddExecutionWorker<TSession>()` / `AddExecutionWorkerPool<TSession>()` to keep core package DI-free.
5. Add XML docs to every public member; remove `1591` from `<NoWarn>` in `.csproj`.

## Phase 4 — Test design overhaul (finding 4)

### Production changes

- Remove test-only hooks: `SetFatalFailure`, `ThrowIfFaulted` (keep private), `TryCompleteChannelForTesting`, `SetWorkerThreadForTesting`, `MarkDisposedForTesting`, public `Process`.
- Remove `TryIgnore_ShouldThrowWhenActionIsNull` dead-code test.

### Test replacements (in existing unit project)

- "fatal failure": rely on `ExecuteAsync_ShouldRethrowFatalFailureAfterTheWorkerFaultsAsync` only.
- "channel rejects work": `Dispose()` then submit, expect `ObjectDisposedException`.
- "pool disposed": same pattern for pool.
- "dispose failure during cleanup": use an `IExecutionSessionFactory` whose `DisposeSession` throws.
- Add a stress test: 1 000 concurrent `ExecuteAsync` submissions verified via `Task.WaitAll` within a deadline; optionally Microsoft.Coyote systematic test.

### New projects under `test/`

- `test/integ/AdaskoTheBeAsT.Interop.Execution.Integ.Tests` — multi-threaded, long-running, session recycling, STA-on-Windows, reentrancy.
- `test/bench/AdaskoTheBeAsT.Interop.Execution.Benchmarks` — BenchmarkDotNet: submission throughput, session reuse cost, dispose latency.
- Add Stryker config (`stryker-config.json`) and a CI job that uploads mutation report as artifact.

## Phase 5 — CI matrix & repo hygiene (findings 6, 8)

1. Update `.github/workflows/ci.yml`:
   - Matrix `os: [ubuntu-latest, windows-latest, macos-latest]`.
   - Windows-only job for `net47` / `net462` smoke tests.
   - Steps: restore, build non-incremental with warnings-as-errors, test, integ test, benchmarks (smoke), Stryker (nightly).
2. Add `.github/dependabot.yml` (daily: nuget, github-actions).
3. Add `.github/workflows/codeql.yml` and enable secret scanning (docs note + admin action).
4. Add `.github/ISSUE_TEMPLATE/{bug,feature}.md`, `PULL_REQUEST_TEMPLATE.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`.
5. Add `CHANGELOG.md` (Keep a Changelog) and a `release.yml` workflow that tags + pushes to NuGet on `v*.*.*` tags.
6. Update `.gitignore` to include `coverage-local.xml`, `**/TestResults/`; remove currently tracked artefacts with `git rm -r --cached`.
7. Add SPDX `// SPDX-License-Identifier: MIT` header to every `.cs` file; flip `SA1633` back on in rulesets.

## Phase 6 — Documentation (finding 7)

1. XML docs completed in Phase 3 — generate API ref via DocFX (`docs/` site).
2. New `.github/workflows/docs.yml` — build DocFX and publish to GitHub Pages on main.
3. README updates: TFM matrix table, install line, versioning/changelog link, troubleshooting (hang, STA / DLL diagnostics), perf notes, migration guide.

## Deliverables checklist (PR breakdown)

To keep reviews manageable, ship as a sequence of PRs:

1. **PR-1** ✅ merged in spirit on `chore/findings-hardening`: Build hygiene — removed `#pragma warning disable CS0120` / `CA1416`, introduced `ExecutionHelpers.TryIgnore`, added `CA1512` + `IDE0039` to the ruleset as `Warning`, fixed all resulting diagnostics. Also added `InitializeAsync(CancellationToken)` to worker + pool and parallelised pool startup (preview of PR-2 / PR-3 work; landed early because it shared the same refactor window). CI flags live in the reusable workflow; no husky / dotnet format hook.
2. **PR-2**: `ExecutionWorker` correctness — remaining Phase 2 items: `IAsyncDisposable`, `IsFaulted`, `WorkerFaulted` event, `PendingCount`, thread-safety audit (`Volatile.Read/Write` on `_initialized` / `_fatalFailure`), telemetry via `ActivitySource` / `Meter`.
3. **PR-3**: Pool least-queued scheduling + Channels package trim on net8+. (STA / CA1416 cleanup already landed in PR-1.)
4. **PR-4**: Interfaces, DI package, XML docs, options binding.
5. **PR-5**: Remove test hooks, add behavioural / stress tests, add integ + bench projects, Stryker.
6. **PR-6**: CI matrix (OS + TFM), Dependabot, CodeQL, repo templates, `CHANGELOG`, SPDX.
7. **PR-7**: DocFX site + README overhaul.

## Risks / call-outs

- Removing `SetFatalFailure` / `Process` public test hooks is a **breaking API change** — requires a major version bump.
- Switching pool scheduling default from round-robin to least-queued alters observable ordering — put it behind an option defaulting to least-queued but document migration.
- Dropping `System.Threading.Channels` package on net8+ changes restore graph — verify no transitive consumer pinned version.
