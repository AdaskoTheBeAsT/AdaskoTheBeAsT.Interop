# ADR-0005 — Public integration test project

- **Status**: Accepted
- **Date**: 2026-04-18
- **Related**: `improve.md` ("No public integration tests yet (test/integ folder exists but is empty)"), `plan.md` Phase 4 ("New projects under `test/`")

## Context

The unit test tier already covers the hot paths with behavioural + hook-based tests: fault propagation, dispose idempotency, cancellation, telemetry smoke, DI registration, hosted-service lifecycle. What it doesn't cover — by design — are the end-to-end scenarios that actually exercise the multi-threaded / OS-conditional / load-sensitive parts of the library:

- a real burst of concurrent submissions collapsing onto a single dedicated thread,
- pool fan-out across multiple dedicated threads with FIFO per-worker ordering,
- STA apartment state on Windows (and the explicit no-op on non-Windows),
- reentrant `Dispose()` from inside a work item,
- session recycling after `MaxOperationsPerSession` and after `RecycleSessionOnFailure`,
- the `ValueTask` hot-path overloads landing on the right code paths,
- the `ExecutionDiagnosticNames` constants staying pinned to their string values.

These belong in a separate integration tier because they're:

- slower (stress scenarios submit thousands of items),
- OS-aware (STA gating),
- timing-sensitive (reentrancy polling with a bounded timeout),
- contract-shaped (diagnostic name pinning is a regression test for a public constant set, not a unit under test).

`improve.md` lists the missing integration tier as one of the residual gaps that kept the library under the 9.5 quality bar.

## Decision

Create `test/integ/AdaskoTheBeAsT.Interop.Execution.IntegrationTest` with 6 test classes across the full 9-TFM matrix:

- `MultiThreadedExecutionWorkerTest` — 32 × 64 concurrent submissions collapse to one dedicated thread; pool fan-out across 4 workers; FIFO ordering per submitter.
- `StaApartmentExecutionWorkerTest` — Windows-guarded STA verification for worker + pool; non-STA default verified too.
- `ReentrancyExecutionWorkerTest` — reentrant `Dispose()` from inside the delegate does not deadlock; nested `ExecuteAsync` from the outside sees the same session.
- `SessionRecyclingExecutionWorkerTest` — `MaxOperationsPerSession` recycling over 23 ops / batch of 5; `RecycleSessionOnFailure=true` vs default behaviour.
- `ValueTaskExecutionWorkerTest` — all 4 `ExecuteValueAsync` overloads + exception propagation.
- `DiagnosticNamesTest` — pins the 13 public diagnostic identifiers as a contract (regression test for ADR-0003).

The project:

- uses a private `IntegrationSession` + `IntegrationSessionFactory` (not shared with unit tests) so unit test refactors cannot accidentally break integration coverage.
- suppresses `xUnit1051` and `CC0030` at the `.csproj` level, with a multi-line comment explaining why — integration tests deliberately pass `CancellationToken.None` to exercise the production default-cancellation path, and `TestContext.Current` is an `xunit.v3`-only surface that would not work uniformly across the net462-net481 targets that still use `xunit` v2.
- wires into `AdaskoTheBeAsT.Interop.slnx` under the existing `/test/integ/` folder so `dotnet test` on the solution picks it up automatically.
- is strict-mode clean (`TreatWarningsAsErrors=true`, `ContinuousIntegrationBuild=true`).

The reentrancy test deserves a specific design call-out: the outer `DisposeAsync` in the test's `finally` is idempotent because the reentrant `Dispose()` call from inside the delegate already flipped `_disposeState`, so session disposal happens asynchronously on the worker thread's finally block. The test polls `factory.DisposeCount` with a bounded 10-second deadline to observe eventual disposal, rather than assuming synchronous completion.

## Consequences

- **+198 test executions per run** (22 tests × 9 TFMs).
- **Real thread / real OS coverage.** STA verification actually runs on Windows, not a mocked apartment state.
- **Regression net for ADR-0002, ADR-0003, ADR-0004.** The scheduler seam is covered by unit tests; the `ValueTask` overloads, the diagnostic names, and the multi-threaded contract are now covered by integration tests too.
- **Boundary-safe analyser suppressions.** The `xUnit1051` suppression lives on the integration project only; unit tests keep the analyser on.

## Benefits

- **+0.2 on the quality score.**
- **Confidence under load** — 32-thread / 1000-submission-class stress scenarios prove FIFO and thread-affinity guarantees.
- **Platform-specific coverage** — STA on Windows is now exercised, not just asserted.
- **Contract pinning** for every recent ADR-landed surface.
