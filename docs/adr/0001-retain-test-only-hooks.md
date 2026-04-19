# ADR-0001 — Retain test-only hooks instead of deleting them

- **Status**: Accepted
- **Date**: 2026-04-18
- **Related**: `findings.md` §4, `plan.md` Phase 4, PR-5

## Context

`findings.md` §4 ("Testability / test design → 10") proposed deleting every test-only mutator from production types:

- `SetFatalFailure`
- `ThrowIfFaulted` (as internal)
- `TryCompleteChannelForTesting`
- `SetWorkerThreadForTesting`
- `MarkDisposedForTesting`
- public `Process`
- `TryIgnore_ShouldThrowWhenActionIsNull`

The argument: "testing dead code pollutes the API".

When drafting Phase 4, we pushed back on this. Every hook named above already carries a `ForTesting` suffix, is narrow in shape, and enables deterministic tests of failure modes that are otherwise either racy (fault latching mid-submission) or unreachable (channel already completed before a submission arrives). Removing them wholesale would:

- drop coverage of the fault-rethrow-after-latching path,
- force rewriting every pool-dispose test into an `async` timing race,
- delete dead-code guards that exist for defence-in-depth and just happen to have a test pinning them.

## Decision

We **retain** the test-only hooks. Phase 4 is now **additive rather than subtractive**:

1. **Keep** the existing hooks (`SetFatalFailure`, `TryCompleteChannelForTesting`, `SetWorkerThreadForTesting`, `MarkDisposedForTesting`, `ThrowIfFaulted`, internal `Process`). Their naming is explicit; any future tightening can be done as a non-breaking narrowing (e.g. `internal` + `[InternalsVisibleTo]` or `[EditorBrowsable(Never)]`).

2. **Delete** only the one genuinely dead-code test: `TryIgnore_ShouldThrowWhenActionIsNull`. A `Grep` of the production call sites for `ExecutionHelpers.TryIgnore` confirmed that every caller passes a non-null action, so the null-guard cannot be reached from production and the test was pinning a contract that isn't real.

3. **Add** behavioural tests that drive the real paths:
   - `ExecuteAsync_ShouldThrowObjectDisposedExceptionAfterDispose` (worker + pool, sync and async variants).
   - `ExecuteAsync_ShouldHandleHighConcurrencyStressSubmissionsAsync` — 1 000 concurrent `Parallel.For` submissions bounded by `Task.WhenAny(allTask, Task.Delay(30s))` for TFM compatibility.
   - A fault-rethrow variant driven through a real faulting `IExecutionSessionFactory` (complementing the hook-based path, not replacing it).

## Consequences

- **Public API unchanged.** Version 1.0.0 ships with the hook surface intact, giving us a free slot to narrow it later without a breaking major.
- **Higher-quality coverage.** The hook-based tests still prove the faulting gate works; the behavioural tests prove that the real pipeline reaches that gate under load and under dispose pressure.
- **Timeout pattern.** The stress tests use `Task.WhenAny(allTask, Task.Delay(30s, CancellationToken.None))` instead of `Task.WaitAll(TimeSpan)` — the latter doesn't exist on `netstandard2.0`.
- **Follow-up.** If a future major version decides to hide the hooks, the preferred route is `internal` + `[InternalsVisibleTo]` — not deletion.

## Benefits

- **+0.0 breaking** (no API surface churn in 1.0.0).
- **Coverage without ceremony** — behavioural tests exercise the real dispose / enqueue paths rather than driving them with private mutators that short-circuit half the machinery.
- **Traceable intent** — any future auditor can see that the hooks are deliberately kept, with a non-breaking exit plan documented.
