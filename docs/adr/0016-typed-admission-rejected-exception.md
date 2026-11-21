# ADR-0016 — Typed `ExecutionAdmissionRejectedException` for bounded queue rejections

- **Status**: Accepted
- **Date**: 2026-11-17
- **Related**: [`docs/plan-2026-11-17.md`](../plan-2026-11-17.md) (`EX-07`), [ADR-0010](0010-worker-completion-and-lifecycle.md)

## Context

When `ExecutionWorkerOptions.QueueCapacity > 0` or
`ExecutionWorkerPoolOptions.QueueCapacity > 0`, `ExecutionWorker<TSession>`
rejects excess waiting work via an atomic CAS reservation loop before writing to
the internal channel ([ADR-0010](0010-worker-completion-and-lifecycle.md)).
Originally, that rejection constructed a plain
`new InvalidOperationException("The worker admission capacity is exhausted.")`.

In production ASP.NET Core services, this created two concrete problems:

1. **Fragile string-literal matching on the HTTP error path.** Endpoints had to
   distinguish bounded-queue backpressure (`HTTP 503 Service Unavailable`) from
   true programming/domain `InvalidOperationException` failures (`HTTP 500`) by
   matching the exact English message string
   `"The worker admission capacity is exhausted."`.
2. **Awkward testability.** Unit tests verifying overload behavior in HTTP or
   application layers could not construct a distinct queue-rejection exception
   directly without either coupling to the internal message string or spinning
   up and blocking a live worker pool.

Replacing `InvalidOperationException` with an unrelated exception hierarchy
would break existing `2.x` and `3.0.0` callers catching
`InvalidOperationException`.

## Decision

Introduce `public sealed class ExecutionAdmissionRejectedException : InvalidOperationException`
in `AdaskoTheBeAsT.Interop.Execution`:

```csharp
public sealed class ExecutionAdmissionRejectedException : InvalidOperationException
{
    public const string DefaultMessage = "The worker admission capacity is exhausted.";

    public ExecutionAdmissionRejectedException();
    public ExecutionAdmissionRejectedException(string message);
    public ExecutionAdmissionRejectedException(string message, Exception innerException);
    public ExecutionAdmissionRejectedException(
        string? workerName,
        int queueCapacity,
        string? message = null);

    public string? WorkerName { get; }
    public int QueueCapacity { get; }
}
```

1. **Backward-compatible inheritance and message.** Deriving from
   `InvalidOperationException` and preserving `DefaultMessage` ensures every
   existing caller catching `InvalidOperationException` (or inspecting
   `ex.Message`) continues to work unchanged across `Task` and `ValueTask`
   submissions.
2. **Structured diagnostic properties.** `WorkerName` and `QueueCapacity`
   (validated `>= 0`) expose the saturated worker identity and configured limit
   without string parsing.
3. **Desktop serialization parity.** On `.NET Framework` (`!NET8_0_OR_GREATER`),
   `ExecutionAdmissionRejectedException` is marked `[Serializable]` and
   validates `QueueCapacity >= 0` during deserialization before round-tripping
   both fields through `GetObjectData`.
4. **Uniform worker and pool emission.** `ExecutionWorker<TSession>.Submit`
   creates `new ExecutionAdmissionRejectedException(_options.Name, capacity)`
   whenever `TryReserveQueueSlot` rejects an item, covering both standalone
   workers and `ExecutionWorkerPool<TSession>` slots across `ExecuteAsync` and
   `ExecuteValueAsync`.

## Consequences

- **Additive subtype.** Catch blocks ordered as
  `catch (ExecutionAdmissionRejectedException)` before
  `catch (InvalidOperationException)` cleanly separate admission shedding from
  unexpected state errors while legacy `catch (InvalidOperationException)` code
  remains unaffected.
- **Direct unit-test construction.** Downstream endpoint and resilience tests
  can instantiate `new ExecutionAdmissionRejectedException("Pool #1", 8)`
  directly without spinning up background threads.

## Benefits

- Eliminates fragile English message matching across HTTP filters, exception
  handlers, and retry policies.
- Carries structured `WorkerName` and `QueueCapacity` context at rejection time
  with zero hot-path overhead when admission succeeds.
