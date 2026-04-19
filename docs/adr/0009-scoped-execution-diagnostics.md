# ADR-0009 — Scoped `ExecutionDiagnostics` with `Shared` singleton

- **Status**: Accepted
- **Date**: 2026-04-19
- **Related**: `improve.md` ("static `ExecutionWorkerDiagnostics` holds a process-wide `Meter` / `ActivitySource` … parallel test hosts and parallel test classes can observe each other's telemetry and assertions racing on counters"), ADR-0003 (public diagnostic constants).

## Context

Phase 2 point 5 shipped an `internal static class ExecutionWorkerDiagnostics` that owned the `ActivitySource`, `Meter`, two `Counter<long>`s, and the `ObservableGauge<int>` backing the queue-depth telemetry. Consumers subscribed by name using the public `ExecutionDiagnosticNames.SourceName` constant (see ADR-0003).

Static ownership made the telemetry trivially discoverable but introduced three related problems:

1. **Parallel-test-host race.** When xUnit runs test classes in parallel inside a single test host (the default), every worker constructed across those tests registers itself into the same `ObservableGauge` callback and emits counter increments onto the same shared `Counter<long>`. Tests that assert exact counter totals could observe increments from other tests still running, producing flakes that only reproduce under the solution-wide `dotnet test <slnx>` run and disappear under isolated per-project runs.
2. **No multi-tenant isolation.** A host that wanted to route one cohort of workers to one OpenTelemetry pipeline and another cohort to a different pipeline could not — they all emitted on one shared `Meter` / `ActivitySource`.
3. **Disposal semantics were undefined.** The static `Meter` / `ActivitySource` have process lifetime by construction, which is correct for production but prevents test tear-down from unregistering the `ObservableGauge` callback — the callback keeps firing for the lifetime of the process.

## Decision

Expose a new public class `ExecutionDiagnostics` that owns the full telemetry state on an instance:

```csharp
public sealed class ExecutionDiagnostics : IDisposable
{
    public static ExecutionDiagnostics Shared { get; }
    public ExecutionDiagnostics(string sourceName);

    public string SourceName { get; }

    internal ActivitySource   ActivitySource         { get; }
    internal Counter<long>    OperationsCounter      { get; }
    internal Counter<long>    SessionRecyclesCounter { get; }

    internal void RegisterWorker(ExecutionWorkerRegistration registration);
    internal void UnregisterWorker(ExecutionWorkerRegistration registration);

    public void Dispose();
}
```

`ExecutionDiagnostics.Shared` is a lazy singleton that uses `ExecutionDiagnosticNames.SourceName` — so every existing OpenTelemetry / `MeterListener` / `ActivityListener` subscriber by that stable public name continues to work without source changes.

Custom instances are constructed with a caller-chosen source name:

```csharp
using var scope = new ExecutionDiagnostics($"{ExecutionDiagnosticNames.SourceName}.Test.{Guid.NewGuid():N}");
await using var worker = new ExecutionWorker<TSession>(
    factory,
    new ExecutionWorkerOptions(name: "Scoped Worker", diagnostics: scope));
```

A worker registered against a custom scope does not contribute measurements to `Shared`, and nothing in the shared scope contributes measurements to the custom one — the two `Meter`/`ActivitySource` pairs are fully isolated.

`ExecutionWorkerOptions.Diagnostics` and `ExecutionWorkerPoolOptions.Diagnostics` carry the scope. `null` (the default) resolves to `ExecutionDiagnostics.Shared` inside the worker ctor.

`Dispose` on the `Shared` instance is a no-op by design — it has process lifetime. `Dispose` on a custom instance releases the `Meter` (and therefore the `ObservableGauge` callback) and the `ActivitySource`.

## Consequences

### Telemetry stability

- The default telemetry name remains `ExecutionDiagnosticNames.SourceName`. OpenTelemetry pipelines, `MeterListener`s, and `ActivityListener`s already subscribed by that name are unaffected.
- Counters, the gauge, tag keys, activity name, and outcome/reason values are unchanged — ADR-0003 constants are still the stable public contract.

### Test isolation

- Every telemetry assertion in `ExecutionWorkerTest` now constructs a per-test `ExecutionDiagnostics` scope with a unique GUID-suffixed source name, attaches its `MeterSnapshot` / `ActivityListener` to that scope, and passes it to the worker via `ExecutionWorkerOptions.Diagnostics`. The solution-wide parallel test-host race on the previously-shared static `Counter<long>` / `ObservableGauge<int>` is no longer reachable.
- New integration tests in `ScopedDiagnosticsTest` pin the isolation contract: a custom-scoped worker's operations do not move the `Shared` meter's operations counter, and the `Shared` singleton's disposal is a no-op.

### Ergonomics

- Zero-setup callers pay no cost. `new ExecutionWorker<TSession>(factory)` still uses `ExecutionDiagnostics.Shared` automatically.
- Advanced callers (multi-tenant hosts, unit-test fixtures, SDK-embedding scenarios) construct their own scope and plug it in through options.

### API surface

- `ExecutionWorkerDiagnostics` (internal static) was deleted. Internal test code that referenced its constants migrated to the already-public `ExecutionDiagnosticNames.*` constants. No public API was removed.
- `ExecutionWorkerOptions.Diagnostics` and `ExecutionWorkerPoolOptions.Diagnostics` are additive — both existing positional ctors and property-initializer usage keep working.

### What this does not change

- The ObservableGauge attached to a custom scope reports the queue depths of workers registered against *that* scope only. The `Shared` singleton's gauge still reports the queue depths of every worker registered against `Shared`. This is the intended isolation contract and matches the way `Meter.CreateObservableGauge` is designed to be scoped.
- Activity sampling decisions are still per-`ActivitySource`. A custom-named scope requires a matching `ActivityListener` to record activities; the default `Shared` scope continues to use the well-known `ExecutionDiagnosticNames.SourceName`.
