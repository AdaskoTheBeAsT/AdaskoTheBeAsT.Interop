# Implementation Plan — `findings.md` Hardening

Derived from `findings.md`. Phased so each stage unblocks the next: build hygiene first (so nothing slips through), then production API changes, then tests, then CI/docs/repo.

## Phase 0 — Baseline & safety nets (prep)

- Create a working branch `chore/findings-hardening`.
- Snapshot current analyzer warning counts (`dotnet build -p:TreatWarningsAsErrors=false /p:ReportAnalyzer=true`) so regressions are measurable.
- Enable `--no-incremental` builds in CI to expose hidden errors (finding 3).

## Phase 1 — Build hygiene (finding 3)

Goal: no suppressed compiler errors, no pragma hacks, CI catches what local caching hid.

1. Grep for and remove `#pragma warning disable CS0120` and `CA1416` across `src/` and replace with proper fixes.
2. Introduce `ExecutionHelpers.TryIgnore(Action)` (non-generic, internal static class) and delete the existing generic `TryIgnore` / RCS1158 suppression.
3. In `AdaskoTheBeAsT.ruleset` (the single source of truth for analyzer severity — wired globally via `Directory.Build.props` / `<CodeAnalysisRuleset>`), promote `CA1512`, `IDE0039`, `CC0030`, `RCS1158` to `Warning` (`CC0030` and `RCS1158` are already `Warning` — just add the missing `CA1512` and `IDE0039` entries under their respective `AnalyzerId` blocks). Since CI runs with `TreatWarningsAsErrors=true`, `Warning` effectively becomes a build break there. Then fix all call sites.

> **Out of scope:** CI workflow edits (`--no-incremental`, `-p:ContinuousIntegrationBuild=true`, `-t:Rebuild`) are already covered by the reusable workflow in `AdaskoTheBeAsT/github-actions` via the new `strict_build` / `deterministic_build` inputs — no changes needed in this repo.
>
> **Also out of scope:** pre-commit hook via `husky.net` + `dotnet format --verify-no-changes` — the `dotnet format` conventions conflict with the project's established style. Will rely on `TreatWarningsAsErrors=true` in CI + the ruleset severities above to enforce quality.

## Phase 2 — Correctness & thread-safety (findings 1, 2)

Refactor `ExecutionWorker.cs` / `ExecutionWorkerPool.cs`:

1. **`Initialize(CancellationToken)`**: add CT overload; honor cancellation before thread spin-up, clean up on `OperationCanceledException`.
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
   - Annotate STA code with `[SupportedOSPlatform("windows")]`, drop `CA1416` pragma.
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

1. **PR-1**: Build hygiene — remove `#pragma warning disable CS0120` / `CA1416`, introduce `ExecutionHelpers.TryIgnore`, add `CA1512` + `IDE0039` to the ruleset as `Warning`, fix all resulting diagnostics. (CI flags live in the reusable workflow; no husky / dotnet format hook.)
2. **PR-2**: `ExecutionWorker` correctness (CT, `IAsyncDisposable`, `IsFaulted`, events, telemetry).
3. **PR-3**: Pool least-queued scheduling + STA / CA1416 cleanup + Channels package trim.
4. **PR-4**: Interfaces, DI package, XML docs, options binding.
5. **PR-5**: Remove test hooks, add behavioural / stress tests, add integ + bench projects, Stryker.
6. **PR-6**: CI matrix (OS + TFM), Dependabot, CodeQL, repo templates, `CHANGELOG`, SPDX.
7. **PR-7**: DocFX site + README overhaul.

## Risks / call-outs

- Removing `SetFatalFailure` / `Process` public test hooks is a **breaking API change** — requires a major version bump.
- Switching pool scheduling default from round-robin to least-queued alters observable ordering — put it behind an option defaulting to least-queued but document migration.
- Dropping `System.Threading.Channels` package on net8+ changes restore graph — verify no transitive consumer pinned version.
