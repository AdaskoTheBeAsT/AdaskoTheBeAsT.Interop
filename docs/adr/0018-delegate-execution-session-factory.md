# ADR-0018 — `DelegateExecutionSessionFactory<TSession>` and inline delegate DI/Hosting registration

- **Status**: Accepted
- **Date**: 2026-11-17
- **Related**: [`docs/plan-2026-11-17.md`](../plan-2026-11-17.md) (`EX-09`), [ADR-0012](0012-contextual-session-lifecycle.md)

## Context

Even with `ContextualExecutionSessionFactory<TSession>` ([ADR-0012](0012-contextual-session-lifecycle.md)),
most native and COM session factories only need to:

1. Observe `cancellationToken.ThrowIfCancellationRequested()`,
2. Construct `new TSession(...)` (optionally inspecting `SessionLifecycleContext`
   or `IServiceProvider`), and
3. Dispose `TSession` via `IDisposable.Dispose()` (while suppressing `IDISP007`
   in every custom factory file because `DisposeSession` disposes an instance it
   did not locally allocate in that method).

Writing a dedicated `*SessionFactory` class per engine added 20–30 lines of
ceremony across every interop library and benchmark harness without adding
domain logic.

## Decision

### 1. Ship `DelegateExecutionSessionFactory<TSession>` in `AdaskoTheBeAsT.Interop.Execution`

Provide a sealed `ContextualExecutionSessionFactory<TSession>` implementation
supporting both simple and contextual creation/disposal delegates:

```csharp
public sealed class DelegateExecutionSessionFactory<TSession>
    : ContextualExecutionSessionFactory<TSession>
    where TSession : class
{
    public DelegateExecutionSessionFactory(
        Func<CancellationToken, TSession> createSession,
        Action<TSession>? disposeSession = null);

    public DelegateExecutionSessionFactory(
        Func<SessionLifecycleContext, CancellationToken, TSession> createSession,
        Action<TSession, SessionLifecycleContext>? disposeSession = null);
}
```

- Calls `cancellationToken.ThrowIfCancellationRequested()` before invoking
  `createSession`.
- Validates `session` against `null` in `DisposeSession`.
- Defaults disposal to `(session as IDisposable)?.Dispose()` when
  `disposeSession` is `null` (encapsulating the ownership `IDISP007`
  suppression in one audited place), is a safe no-op when `TSession` does not
  implement `IDisposable`, and invokes custom `disposeSession` when supplied.

### 2. Add inline delegate overloads in DI and Hosting

Expose concise registration overloads on `ExecutionWorkerServiceCollectionExtensions`
and `ExecutionWorkerHostingExtensions`:

```csharp
public static IServiceCollection AddExecutionWorker<TSession>(
    this IServiceCollection services,
    Func<IServiceProvider, SessionLifecycleContext, CancellationToken, TSession> createSession,
    Action<IServiceProvider, ExecutionWorkerOptions>? configure = null,
    Action<TSession, SessionLifecycleContext>? disposeSession = null)
    where TSession : class;

public static IServiceCollection AddExecutionWorkerPool<TSession>(
    this IServiceCollection services,
    Func<IServiceProvider, SessionLifecycleContext, CancellationToken, TSession> createSession,
    Action<IServiceProvider, ExecutionWorkerPoolOptions>? configure = null,
    Action<TSession, SessionLifecycleContext>? disposeSession = null)
    where TSession : class;
```

*(Plus matching `AddExecutionWorkerHostedService<TSession>` and
`AddExecutionWorkerPoolHostedService<TSession>` overloads.)*

For delegate-based pool overloads when `configure` is `null` and no external
`IOptions<ExecutionWorkerPoolOptions>` is registered, a default
`ExecutionWorkerPoolOptions` (`WorkerCount = 1`) is used so single-lambda pool
setup works out of the box.

## Consequences

- **Class-based factories remain first-class.** Complex stateful factories with
  constructor-injected dependencies can continue implementing
  `IExecutionSessionFactory<TSession>` or deriving from
  `ContextualExecutionSessionFactory<TSession>`.
- **Zero extra allocations per operation.** `DelegateExecutionSessionFactory<TSession>`
  is allocated once at registration/startup and invoked only on session creation
  and recycle/shutdown disposal.

## Benefits

- Collapses 25-line `*SessionFactory.cs` boilerplate classes + `IDISP007`
  suppressions into a single strongly typed lambda in standalone, DI, and
  Generic Host applications.
