⛬  Here is a concrete, prescriptive checklist per category. Treat it as acceptance criteria for a 10.

**Status legend** (applied 2026-04-19 after the `chore/findings-hardening` work):

- ✅ done — landed in the current main-line branch.
- 🟡 partial — design item addressed, follow-up still open.
- ⏩ deferred — intentionally not tackled here (see note; usually CI-workflow / repo-admin / docs-site scope).
- ❌ open — still to do.

---

   1. Correctness / design → 10
   •  ✅ Add CancellationToken to Initialize() so startup can be aborted without disposing.
      (Phase 2 point 1. `InitializeAsync(CancellationToken)` added to both worker and pool; pool does
      parallel startup via `Task.WhenAll`.)
   •  ✅ Implement IAsyncDisposable and surface an async DisposeAsync() that awaits the worker thread
      instead of Thread.Join(). (Phase 2 point 2. `_workerExitCompletionSource` replaces `Thread.Join`.)
   •  ✅ Expose bool IsFaulted { get; } and event EventHandler<Exception> WorkerFaulted so hosts can
      observe terminal failures. (Phase 2 point 3. `EventHandler<WorkerFaultedEventArgs>`, exactly-once
      dispatch, publication ordering race closed on 2026-04-19 — see CHANGELOG `Fixed` entry.)
   •  ✅ Pool: replace pure round-robin with "least-queued" / work-stealing selection so a slow worker
      does not delay other callers. (Phase 2 point 4 + ADR-0002. `LeastQueued` is the default;
      `RoundRobin` opt-in; pluggable `IWorkerScheduler<TSession>` seam.)
   •  ✅ Add ActivitySource/Meter instrumentation (ExecutionWorker.{Operation,QueueDepth,SessionRecycles})
      — no-op when unused. (Phase 2 point 5 + ADR-0003 + ADR-0009. Scoped `ExecutionDiagnostics` with
      `Shared` singleton; constants exposed via `ExecutionDiagnosticNames`.)

   2. Thread-safety → 10
   •  ✅ Clear _fatalFailure + _initialized atomically on a successful re-start path (or make a faulted
      worker explicitly terminal and document it). (Phase 2 point 6. Terminal-on-fault is now the
      documented contract: `EnsureStartedLockedAsync` calls `ThrowIfFaulted()` before the `_initialized`
      check.)
   •  ✅ Mark _initialized access via Volatile or move every read/write under _syncRoot (today it is set
      under lock but read without). (Phase 2 point 6. All `_initialized` / `_workerThread` /
      `_startupTask` access is now inside `_syncRoot`.)
   •  ✅ Add a stress/interleaving test using Microsoft.Coyote or a loop with Task.WaitAll of 1000
      concurrent submissions to prove no deadlock or lost items.
      (`ExecuteAsync_ShouldHandleHighConcurrencyStressSubmissionsAsync` runs 1 000 parallel submissions
      with a 30 s deadline in every TFM. Microsoft.Coyote is still open as a nice-to-have.)

   3. Build hygiene → 10
   •  ✅ In CI, run dotnet build --no-incremental and, separately, dotnet build
      -p:ContinuousIntegrationBuild=true -t:Rebuild. (Implemented in the reusable
      `AdaskoTheBeAsT/github-actions` workflow at `D:\GitHub\github-actions` via the
      `strict_build` / `deterministic_build` inputs; this repo consumes the workflow, no
      per-repo CI yaml required.)
   •  ✅ Remove the #pragma warning disable CS0120 pattern entirely. (Phase 1. `OperatingSystem.IsWindows()`
      on net5+ plus `Environment.OSVersion.Platform` fallback on netfx.)
   •  ✅ Replace TryIgnore with a non-generic static class ExecutionHelpers { internal static void
      TryIgnore(Action) {...} } so RCS1158 is satisfied naturally (no pragma). (Phase 1.)
   •  ✅ Add a pre-commit hook (lefthook / husky.net) that runs dotnet build --no-incremental
      -p:TreatWarningsAsErrors=true + full unit test suite.
      (Husky.Net 0.9.1 wired as a local dotnet tool; manifest at `.config/dotnet-tools.json`;
      hook at `.husky/pre-commit` runs `dotnet husky run --group pre-commit`, which in turn
      executes the `strict-build` task — `dotnet build AdaskoTheBeAsT.Interop.slnx
      --no-incremental -t:Rebuild -p:TreatWarningsAsErrors=true -p:ContinuousIntegrationBuild=true`
      — followed by the `unit-tests` task (`dotnet test AdaskoTheBeAsT.Interop.slnx --no-build`).
      `Directory.Build.targets` auto-installs the hook on developer machines on first `dotnet
      restore` (skipped under `CI=true` / `GITHUB_ACTIONS=true` / `TF_BUILD=true`).
      **`dotnet format --verify-no-changes` is intentionally NOT part of the hook**: the
      default conventions emitted by `dotnet format` are incompatible with the project's
      established `.editorconfig` + StyleCop style (different attribute / using / brace
      placement rules), so running it would churn every file. Style enforcement stays with
      `TreatWarningsAsErrors=true` + the StyleCop / Roslynator / Meziantou / SonarAnalyzer
      pack driven by `AdaskoTheBeAsT.ruleset`.)
   •  ✅ Promote the remaining analyzer notes (CA1512, IDE0039, CC0030, RCS1158) to warning or error in
      .editorconfig and fix them. (Phase 1. Ruleset updated; full 9-TFM build clean with
      TreatWarningsAsErrors.)

   4. Testability / test design → 10
   •  ⏩ Delete all test-only mutators from production types: SetFatalFailure, ThrowIfFaulted (as
      internal), TryCompleteChannelForTesting, SetWorkerThreadForTesting, MarkDisposedForTesting,
      Process. (ADR-0001. Retained intentionally — dropping them would be a breaking change;
      everything is `internal` and lives behind `[InternalsVisibleTo]`.)
   •  ✅ Replace them with behavioural tests:
     •  ✅ "fatal failure" → caused by a real failing DisposeSession in the factory.
     •  ✅ "channel rejects work" → dispose the worker, then submit.
     •  ✅ "pool disposed" → Dispose() then submit.
     •  ⏩ "Process invalid state" → kept; behavioural equivalent added, but the hook-based test remains
        until the next major removes the hook.
     •  ✅ "ignore dispose failure during cleanup" → covered by
        `Initialize_ShouldIgnoreWorkerDisposeFailuresDuringCleanup` + session-factory with throwing
        `DisposeSession`.
   •  ✅ Remove TryIgnore_ShouldThrowWhenActionIsNull; the guard is unreachable from production and
      testing dead code pollutes the API. (Removed per ADR-0001 — a `Grep` of
      `ExecutionHelpers.TryIgnore` callers confirmed every production caller passes a non-null
      delegate, so the null-guard cannot be reached from production and pinning it with a test
      would fossilise a non-contract. Zero references to `TryIgnore` remain in `test/`.)
   •  ✅ Add a test/integ/ project: real multi-threaded scenarios, long-run session recycling,
      reentrancy from inside work items, OS-conditional STA on Windows.
      (test/integ/AdaskoTheBeAsT.Interop.Execution.IntegrationTest — 36 tests per modern TFM,
      30 per netfx TFM. Covers multi-threaded, session recycling, STA, reentrancy, zero-alloc,
      snapshots, scoped diagnostics.)
   •  ❌ Add a BenchmarkDotNet project (test/bench/) that measures submission throughput, session reuse
      cost, and dispose latency so regressions are visible.
   •  ❌ Enable mutation testing (dotnet stryker) and commit the baseline score to CI.

   5. API ergonomics → 10
   •  ✅ Add XML doc comments to every public member and delete 1591 from <NoWarn>.
      (Phase 3 point 5. `GenerateDocumentationFile=true` + `CS1591` hard error.)
   •  ⏩ Generate and publish an API reference (DocFX or Doxy). (Phase 6 — deferred.)
   •  🟡 Replace nameof(ExecutionWorker<>) / nameof(ExecutionWorker<TSession>) with a cached
      typeof(...).Name. (Private `TypeName` constants exist on both; open-generic `nameof` trick is gone
      from telemetry paths. Remaining usages are in diagnostic strings only.)
   •  ✅ Introduce IExecutionWorker<TSession> and IExecutionWorkerPool<TSession> interfaces so callers
      can mock and DI can inject. (Phase 3 point 1.)
   •  ✅ Expose read-only Name, WorkerCount (pool already has it), and PendingCount / IsFaulted
      properties. (`Name` + `GetSnapshot()` added uniformly on worker AND pool — ADR-0008.
      `ExecutionWorkerSnapshot` / `ExecutionWorkerPoolSnapshot` public readonly structs.)
   •  ✅ Provide IServiceCollection.AddExecutionWorker<TSession>(...) helpers (separate package if you
      want to keep Execution DI-free). (`AdaskoTheBeAsT.Interop.Execution.DependencyInjection` +
      `.Hosting` packages shipped.)
   •  ✅ Make ExecutionWorkerOptions / ExecutionWorkerPoolOptions support IOptions<T> binding (simple
      init-only properties work). (Phase 3 point 3. Both have parameterless + positional ctors,
      `Validate()`, public setters.)

   6. Cross-target story → 10
   •  ⏩ Run the test matrix on Linux + macOS in CI (the STA test is already OS-gated; the non-STA paths
      should be exercised on all three OSes). (CI matrix change — not in this repo, handled by the
      shared `AdaskoTheBeAsT/github-actions` workflow.)
   •  ✅ Use System.Threading.Lock when NET9_0_OR_GREATER, object otherwise — already done, and audited.
      (`_syncRoot` is the only lock object.)
   •  ✅ Guard SetApartmentState(STA) with [SupportedOSPlatform("windows")] and remove the
      #pragma warning disable CA1416. (Phase 1. Used `OperatingSystem.IsWindows()` guard; pragma
      removed.)
   •  ⏩ Add net47/net462 specific smoke tests running on a real Windows runner in CI. (CI-config;
      unit + hosting + DI + integ tests DO run under net462/net47/net471/net472/net48/net481 locally.)
   •  ✅ Verify System.Threading.Channels version selection per TFM matches the runtime. (Phase 2
      point 7. Package pin dropped on net8+; remains on netfx only.
      `netstandard2.0` was dropped from the TFM matrix during the Copilot review
      follow-up, so the conditional `ItemGroup` is now netfx-only.)

   7. Docs → 10
   •  ✅ XML docs on public API (remove 1591 from NoWarn). (Phase 3 point 5.)
   •  ❌ README additions: TFM matrix table, NuGet install line, versioning + changelog section,
      troubleshooting, perf notes, migration guide. (README not yet rewritten.)
   •  ⏩ Reference API docs site published from CI (DocFX + GitHub Pages). (Phase 6 — deferred.)

   8. Repo hygiene → 10
   •  ❌ Remove coverage-local.xml and test/unit/.../TestResults/ from the repo; add them to
      .gitignore.
   •  ❌ Add .github/dependabot.yml (daily for NuGet + GitHub Actions).
   •  ❌ Enable CodeQL and secret scanning.
   •  🟡 Add CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md, and .github/ISSUE_TEMPLATE/ +
      PULL_REQUEST_TEMPLATE.md. (`.github/CODEOWNERS` added so branch-protection "Require
      review from Code Owners" has a target; the other templates remain open.)
   •  ✅ Add a CHANGELOG.md (Keep a Changelog format). (Added with Unreleased section covering
      zero-alloc ExecuteValueAsync, snapshot surface, scoped diagnostics, fault-publication ordering
      fix. Release-workflow tagging is still open.)
   •  ⏩ Require signed commits and branch protection on main (admin-side, not code).
   •  ❌ Add LICENSE SPDX header comment on every source file (StyleCop SA1633 is already suppressed —
      un-suppress after adding).
