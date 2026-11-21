# ADR-0017 — Automatic `IExecutionDispatcher<TSession>` forwarding and validated `IServiceProvider` options in DI/Hosting

- **Status**: Accepted
- **Date**: 2026-11-17
- **Related**: [`docs/plan-2026-11-17.md`](../plan-2026-11-17.md) (`EX-08`), [ADR-0012](0012-contextual-session-lifecycle.md)

## Context

Downstream services typically write application calculators and domain adapters
against `IExecutionDispatcher<TSession>` rather than coupling callers to whether
execution is backed by a single `IExecutionWorker<TSession>` or a multi-worker
`IExecutionWorkerPool<TSession>`.

Auditing downstream multi-service hosts revealed two ergonomic gaps in
`AdaskoTheBeAsT.Interop.Execution.DependencyInjection` and
`AdaskoTheBeAsT.Interop.Execution.Hosting`:

1. **`IExecutionDispatcher<TSession>` was not registered.** Calling
   `AddExecutionWorker<TSession>()` or `AddExecutionWorkerPool<TSession>()`
   registered only `IExecutionWorker<TSession>` or
   `IExecutionWorkerPool<TSession>`, forcing every consumer to add a manual
   forwarding registration.
2. **Options callbacks could not inspect `IServiceProvider` or fail fast at host
   startup.** Real hosts bind strongly typed configuration (such as
   `IOptions<SolverPoolOptions>`) with `.ValidateOnStart()` and map those settings
   into `ExecutionWorkerOptions` or `ExecutionWorkerPoolOptions`. Without
   `Action<IServiceProvider, TOptions>` overloads and `IValidateOptions<TOptions>`
   startup validation, invalid `WorkerCount`, `QueueCapacity`,
   `MaxOperationsPerSession`, `DisposeTimeout`, or `MaxStackSize` values only
   surfaced lazily on first service resolution instead of failing during
   `IHost.StartAsync`.

## Decision

### 1. Forward `IExecutionDispatcher<TSession>` via `TryAddSingleton`

- `AddExecutionWorker<TSession>` registers
  `services.TryAddSingleton<IExecutionDispatcher<TSession>>(static sp => sp.GetRequiredService<IExecutionWorker<TSession>>());`.
- `AddExecutionWorkerPool<TSession>` registers
  `services.TryAddSingleton<IExecutionDispatcher<TSession>>(static sp => sp.GetRequiredService<IExecutionWorkerPool<TSession>>());`.
- Using `TryAddSingleton` preserves first-registration-wins semantics if an
  application registers a custom `IExecutionDispatcher<TSession>` decorator
  ahead of the worker/pool registration, and duplicate calls to
  `AddExecutionWorker<TSession>` / `AddExecutionWorkerPool<TSession>` remain
  idempotent across both service descriptors and named options.

### 2. Add `IServiceProvider`-aware configuration overloads and `ValidateOnStart`

Add `Action<IServiceProvider, ExecutionWorkerOptions>` and
`Action<IServiceProvider, ExecutionWorkerPoolOptions>` overloads (including the
worker-indexed `sessionFactoryResolver` overload) in both
`ExecutionWorkerServiceCollectionExtensions` and
`ExecutionWorkerHostingExtensions`:

```csharp
public static IServiceCollection AddExecutionWorker<TSession>(
    this IServiceCollection services,
    Action<IServiceProvider, ExecutionWorkerOptions> configure)
    where TSession : class;

public static IServiceCollection AddExecutionWorkerPool<TSession>(
    this IServiceCollection services,
    Action<IServiceProvider, ExecutionWorkerPoolOptions> configure)
    where TSession : class;

public static IServiceCollection AddExecutionWorkerPool<TSession>(
    this IServiceCollection services,
    Func<IServiceProvider, int, IExecutionSessionFactory<TSession>> sessionFactoryResolver,
    Action<IServiceProvider, ExecutionWorkerPoolOptions> configure)
    where TSession : class;
```

*(Mirrored on `AddExecutionWorkerHostedService<TSession>` and
`AddExecutionWorkerPoolHostedService<TSession>`.)*

Each registration wires named options through
`services.AddOptions<TOptions>(optionsName)` with `.ValidateOnStart()` and
registers singleton `IValidateOptions<ExecutionWorkerOptions>` /
`IValidateOptions<ExecutionWorkerPoolOptions>` validators backed by public
`ExecutionWorkerOptions.Validate()` / `ExecutionWorkerPoolOptions.Validate()`.

## Consequences

- **Fail-fast host startup.** Invalid option values throw
  `OptionsValidationException` during `IHost.StartAsync` even when no hosted
  service eagerly resolves the worker or pool.
- **Idempotent duplicate registration.** Calling
  `AddExecutionWorkerHostedService<TSession>` or
  `AddExecutionWorkerPoolHostedService<TSession>` multiple times registers a
  single worker/pool, a single `IExecutionDispatcher<TSession>`, and a single
  `IHostedService` without overwriting the initial named-options configuration.

## Benefits

- Removes repetitive `TryAddSingleton<IExecutionDispatcher<TSession>>` and
  manual `ExecutionWorkerPool` factory lambdas across host projects.
- Bridges host-validated `IOptions<T>` pipelines directly into worker and pool
  configuration with startup-time validation.
