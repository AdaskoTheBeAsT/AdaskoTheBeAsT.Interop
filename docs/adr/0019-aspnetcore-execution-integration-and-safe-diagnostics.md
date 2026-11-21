# ADR-0019 — Companion `AdaskoTheBeAsT.Interop.Execution.AspNetCore` package and AOT-safe diagnostics DTOs

- **Status**: Accepted
- **Date**: 2026-11-17
- **Related**: [`docs/plan-2026-11-17.md`](../plan-2026-11-17.md) (`EX-10`), [ADR-0008](0008-uniform-snapshot-surface.md), [ADR-0015](0015-hosting-health-checks-and-readiness-snapshots.md), [ADR-0016](0016-typed-admission-rejected-exception.md)

## Context

ASP.NET Core Minimal API hosts fronting `IExecutionWorker<TSession>` and
`IExecutionWorkerPool<TSession>` repeatedly implemented the same three HTTP
concerns:

1. **Execution exception-to-HTTP translation:** mapping `ArgumentException` to
   `400 Bad Request` `ProblemDetails` (without echoing raw native filesystem
   paths), client-aborted `OperationCanceledException` (`HttpContext.RequestAborted`)
   to `499 Client Closed Request`, and `TimeoutException`,
   `ExecutionAdmissionRejectedException`, or `ObjectDisposedException` to
   `503 Service Unavailable` `ProblemDetails` (optionally with `Retry-After`).
2. **Latent production failure in raw snapshot JSON serialization:** endpoints
   returning `pool.GetSnapshot()` directly with `System.Text.Json` source
   generation (`[JsonSerializable(typeof(ExecutionWorkerPoolSnapshot))]`)
   succeeded while healthy (`Fault == null`), but threw `NotSupportedException`
   at runtime as soon as any worker faulted (`Fault != null`) because
   `System.Exception.TargetSite` exposes reflection `MethodBase`, and risked
   leaking internal stack traces and native paths over HTTP.
3. **Standard `/healthz` and `/readyz` probe endpoints:** pairing a zero-check
   liveness endpoint (`/healthz`) with a tag-filtered readiness endpoint
   (`/readyz`) backed by `ExecutionWorkerHealthCheck` /
   `ExecutionWorkerPoolHealthCheck` ([ADR-0015](0015-hosting-health-checks-and-readiness-snapshots.md)).

Adding `Microsoft.AspNetCore.App` to `AdaskoTheBeAsT.Interop.Execution.Hosting`
was rejected because `Hosting` supports `.NET Framework 4.7.2`–`4.8.1` and
non-HTTP Generic Host worker services.

## Decision

### 1. Ship `AdaskoTheBeAsT.Interop.Execution.AspNetCore` targeting `net11.0;net10.0`

Create a fourth package referencing `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
plus `Execution`, `Execution.DependencyInjection`, and `Execution.Hosting`, with
**zero third-party NuGet dependencies**.

### 2. Endpoint filter and global `IExceptionHandler`

Provide `ExecutionAspNetCoreOptions` (`BadRequestTitle`, `UnavailableTitle`,
optional `RetryAfter` validated `> TimeSpan.Zero`), `ExecutionEndpointFilter`
(`WithExecutionExceptionHandling` on `RouteHandlerBuilder` and
`RouteGroupBuilder`), and `ExecutionExceptionHandler : IExceptionHandler`
(`AddExecutionExceptionHandler`):

- `ArgumentException` -> `400 Bad Request` `ProblemDetails` using generic
  `BadRequestTitle` (never raw exception messages or native paths).
- `OperationCanceledException` when `httpContext.RequestAborted.IsCancellationRequested`
  -> `499 Client Closed Request` (`StatusCodes.Status499ClientClosedRequest`).
- `TimeoutException`, `ExecutionAdmissionRejectedException`, or
  `ObjectDisposedException` -> `503 Service Unavailable` `ProblemDetails` using
  `UnavailableTitle` and optional ceiling-rounded integer `Retry-After` seconds.
- Unrelated exceptions (including non-client `OperationCanceledException`) pass
  through untouched to standard ASP.NET Core error handling.

### 3. Source-generated, leak-free diagnostics DTOs and endpoint extensions

Provide immutable `ExecutionWorkerDiagnosticsDto` and
`ExecutionWorkerPoolDiagnosticsDto` types backed by
`ExecutionAspNetCoreJsonSerializerContext` (`JsonKnownNamingPolicy.CamelCase`):

- Replace `Exception? Fault` with `string? FaultType` (`snapshot.Fault?.GetType().Name`),
  preserving camelCase JSON contract names (`name`, `workerCount`, `queueDepth`,
  `initializedWorkerCount`, `faultedWorkerCount`, `isAnyFaulted`,
  `isInitialized`, `isDisposed`, `workers`, `faultType`).
- `AddExecutionAspNetCoreJson()` prepends
  `ExecutionAspNetCoreJsonSerializerContext.Default` to
  `HttpJsonOptions.SerializerOptions.TypeInfoResolverChain` idempotently.
- `MapExecutionWorkerDiagnostics<TSession>(pattern = "/api/diagnostics")`,
  `MapExecutionWorkerPoolDiagnostics<TSession>(pattern = "/api/diagnostics")`,
  and `MapExecutionHealthProbes(livenessPattern = "/healthz", readinessPattern = "/readyz", readyTag = "ready")`
  wire diagnostics and Kubernetes/container probes in one line each.

## Consequences

- **Modern-.NET-only package.** `AdaskoTheBeAsT.Interop.Execution.AspNetCore`
  targets `net11.0` and `net10.0` only, keeping the core five-TFM matrix
  (`net11.0;net10.0;net481;net48;net472`) intact on `Execution`,
  `DependencyInjection`, and `Hosting`.
- **Safe under Native AOT / trimmed JSON pipelines.** Diagnostics and
  `ProblemDetails` responses serialize without runtime reflection even when
  workers hold populated `Exception` instances with `TargetSite` and
  `StackTrace`.

## Benefits

- Fixes the `Fault != null` source-generated JSON crash and prevents accidental
  stack-trace/path disclosure on `/api/diagnostics`.
- Standardizes HTTP `400` / `499` / `503` mapping and `/healthz` + `/readyz`
  probe endpoints across ASP.NET Core services.
