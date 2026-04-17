⛬  Here is a concrete, prescriptive checklist per category. Treat it as acceptance criteria for a 10.

   1. Correctness / design → 10
   •  Add CancellationToken to Initialize() so startup can be aborted without disposing.
   •  Implement IAsyncDisposable and surface an async DisposeAsync() that awaits the worker thread instead of 
      Thread.Join().
   •  Expose bool IsFaulted { get; } and event EventHandler<Exception> WorkerFaulted so hosts can observe terminal 
      failures.
   •  Pool: replace pure round-robin with "least-queued" / work-stealing selection so a slow worker does not delay 
      other callers.
   •  Add ActivitySource/Meter instrumentation (ExecutionWorker.{Operation,QueueDepth,SessionRecycles}) - no-op when 
      unused.

   2. Thread-safety → 10
   •  Clear _fatalFailure + _initialized atomically on a successful re-start path (or make a faulted worker explicitly 
      terminal and document it).
   •  Mark _initialized access via Volatile or move every read/write under _syncRoot (today it is set under lock but 
      read without).
   •  Add a stress/interleaving test using Microsoft.Coyote or a loop with Task.WaitAll of 1000 concurrent submissions 
      to prove no deadlock or lost items.

   3. Build hygiene → 10
   •  In CI, run dotnet build --no-incremental and, separately, dotnet build -p:ContinuousIntegrationBuild=true 
      -t:Rebuild. The recent CS0120 slipped through only because of incremental caching.
   •  Remove the #pragma warning disable CS0120 pattern entirely (you cannot suppress compiler errors) - the codebase 
      should never hint at it.
   •  Replace TryIgnore with a non-generic static class ExecutionHelpers { internal static void TryIgnore(Action) {...}
       } so RCS1158 is satisfied naturally (no pragma).
   •  Add a pre-commit hook (lefthook / husky.net) that runs dotnet format --verify-no-changes + dotnet build 
      --no-incremental -p:TreatWarningsAsErrors=true.
   •  Promote the remaining analyzer notes (CA1512, IDE0039, CC0030, RCS1158) to warning or error in .editorconfig and 
      fix them.

   4. Testability / test design → 10
   •  Delete all test-only mutators from production types: SetFatalFailure, ThrowIfFaulted (as internal), 
      TryCompleteChannelForTesting, SetWorkerThreadForTesting, MarkDisposedForTesting, Process.
   •  Replace them with behavioural tests:
     •  "fatal failure" → caused by a real failing DisposeSession in the factory (already covered by 
        ExecuteAsync_ShouldRethrowFatalFailureAfterTheWorkerFaultsAsync, so the SetFatalFailure/ThrowIfFaulted tests 
        become redundant).
     •  "channel rejects work" → dispose the worker, then submit. Remove TryCompleteChannelForTesting.
     •  "pool disposed" → call Dispose() then submit. Remove MarkDisposedForTesting.
     •  "Process invalid state" → delete the test, keep Process private.
     •  "ignore dispose failure during cleanup" → already covered by 
        Initialize_ShouldIgnoreWorkerDisposeFailuresDuringCleanup; drop the PoisonFirstWorkerThread / 
        SetWorkerThreadForTesting hook and replace with a session factory whose DisposeSession throws.
   •  Remove TryIgnore_ShouldThrowWhenActionIsNull; the guard is unreachable from production and testing dead code 
      pollutes the API.
   •  Add a test/integ/ project: real multi-threaded scenarios, long-run session recycling, reentrancy from inside work
       items, OS-conditional STA on Windows.
   •  Add a BenchmarkDotNet project (test/bench/) that measures submission throughput, session reuse cost, and dispose 
      latency so regressions are visible.
   •  Enable mutation testing (dotnet stryker) and commit the baseline score to CI.

   5. API ergonomics → 10
   •  Add XML doc comments to every public member and delete 1591 from <NoWarn>. Generate and publish an API reference 
      (DocFX or Doxy).
   •  Replace nameof(ExecutionWorker<>) / nameof(ExecutionWorkerPool<>) with nameof(ExecutionWorker<TSession>) or a 
      cached typeof(...).Name - the open-generic nameof trick looks suspicious in telemetry.
   •  Introduce IExecutionWorker<TSession> and IExecutionWorkerPool<TSession> interfaces so callers can mock and DI can
       inject.
   •  Expose read-only Name, WorkerCount (pool already has it), and PendingCount / IsFaulted properties.
   •  Provide IServiceCollection.AddExecutionWorker<TSession>(...) helpers (separate package if you want to keep 
      Execution DI-free).
   •  Make ExecutionWorkerOptions / ExecutionWorkerPoolOptions support IOptions<T> binding (simple init-only properties
       work).

   6. Cross-target story → 10
   •  Run the test matrix on Linux + macOS in CI (the STA test is already OS-gated; the non-STA paths should be 
      exercised on all three OSes).
   •  Use System.Threading.Lock when NET9_0_OR_GREATER, object otherwise - already done, but also audit for other lock 
      objects (_syncRoot only - good).
   •  Guard SetApartmentState(STA) with [SupportedOSPlatform("windows")] and remove the #pragma warning disable CA1416.
   •  Add net47/net462 specific smoke tests running on a real Windows runner in CI (Ubuntu cannot run netfx TFMs).
   •  Verify System.Threading.Channels version selection per TFM matches the runtime - minor: net10.0 + 
      System.Threading.Channels 10.0.6 duplicates in-box APIs; drop the package reference on net8.0+ once in-box 
      Channels is sufficient.

   7. Docs → 10
   •  XML docs on public API (remove 1591 from NoWarn).
   •  README additions: TFM matrix table, NuGet install line (dotnet add package ...), versioning + changelog section, 
      troubleshooting (what to do if ExecuteAsync hangs, how to diagnose STA / DLL-load failures), perf notes (thread 
      cost, per-op overhead), migration guide from "hand-rolled worker" patterns.
   •  Reference API docs site published from CI (DocFX + GitHub Pages).

   8. Repo hygiene → 10
   •  Remove coverage-local.xml and test/unit/.../TestResults/ from the repo; add them to .gitignore.
   •  Add .github/dependabot.yml (daily for NuGet + GitHub Actions).
   •  Enable CodeQL and secret scanning.
   •  Add CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md, and .github/ISSUE_TEMPLATE/ + PULL_REQUEST_TEMPLATE.md.
   •  Add a CHANGELOG.md (Keep a Changelog format) and a tagging/release workflow.
   •  Require signed commits and branch protection on main (admin-side, not code).
   •  Add LICENSE SPDX header comment on every source file (StyleCop SA1633 is already suppressed - un-suppress after 
      adding).