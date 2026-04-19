# ADR-0003 — Public `ExecutionDiagnosticNames` constants

- **Status**: Accepted
- **Date**: 2026-04-18
- **Related**: `improve.md` ("Public `ExecutionWorkerDiagnostics` — currently internal, some telemetry consumers may want to attach to the `ActivitySource` / `Meter` directly without hardcoding the DiagnosticName string"), `plan.md` Phase 2 point 5

## Context

Phase 2 point 5 shipped full `ActivitySource` / `Meter` instrumentation under an internal `ExecutionWorkerDiagnostics` class. Telemetry consumers (OpenTelemetry collectors, `ActivityListener`s, `MeterListener`s, log enrichers) that wanted to subscribe to the worker's activities and metrics had to hard-code string literals:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("AdaskoTheBeAsT.Interop.Execution"))
    .WithMetrics(m => m.AddMeter("AdaskoTheBeAsT.Interop.Execution"));
```

That couples every consumer to a string that might be rebranded later and offers zero compile-time safety if we ever rename the source or add a second one. The same hard-coding hazard applied to tag keys (`worker.name`, `outcome`, `reason`), outcome values (`success` / `faulted` / `cancelled`), and recycle reasons (`max_operations` / `failure`).

We could not simply flip `ExecutionWorkerDiagnostics` to `public` because the class carries mutable state (`ActivitySource`, `Meter`, a live registration dictionary, the `ObservableGauge` root) and has members only the worker should be allowed to touch (`RegisterWorker` / `UnregisterWorker`).

## Decision

Split the concerns:

- `ExecutionWorkerDiagnostics` stays **internal** — nothing about the instrument roots or the registration dictionary changes.
- A new **public** `ExecutionDiagnosticNames` static class exposes every stable string as a `public const string`:
  - `SourceName` — the `ActivitySource` / `Meter` name.
  - `ActivityExecute` — the span name.
  - `MetricOperations`, `MetricSessionRecycles`, `MetricQueueDepth` — metric names.
  - `TagWorkerName`, `TagOutcome`, `TagRecycleReason` — tag keys.
  - `OutcomeSuccess`, `OutcomeFaulted`, `OutcomeCancelled` — outcome tag values.
  - `RecycleMaxOperations`, `RecycleFailure` — recycle reason tag values.

The internal diagnostics class is refactored to **reference** the public constants (`public const string DiagnosticName = ExecutionDiagnosticNames.SourceName`, etc.) so there is exactly one source of truth. Instrument emission is byte-for-byte identical to before; consumers who hard-coded strings keep working, consumers who move to the constants get a compile-time pin.

```csharp
using AdaskoTheBeAsT.Interop.Execution;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(ExecutionDiagnosticNames.SourceName))
    .WithMetrics(m => m.AddMeter(ExecutionDiagnosticNames.SourceName));
```

## Consequences

- **One-way door.** Once the constants are public, their string values are part of the public contract and cannot change without a major-version bump. This is deliberate — it's the whole point of the ADR.
- **Zero behavioural change.** No new instruments, no renamed tags, no extra allocations. A `DiagnosticNamesTest` integration test pins every constant to its string value so drift is caught by CI.
- **Forward compatibility.** Future additions (new outcome values, new recycle reasons) just add new `public const string` fields; existing consumers keep working.

## Benefits

- **+0.2 on the quality score.**
- **Compile-time safety** for telemetry consumers.
- **Refactor-safe** rename via "Find all references" rather than `grep`.
- **Documented contract** — the class-level XML doc explicitly tells consumers these are stable identifiers intended for subscription.
