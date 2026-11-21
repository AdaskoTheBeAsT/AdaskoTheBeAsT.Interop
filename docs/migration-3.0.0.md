# Migrating from 2.x to 3.0.0

This guide compares the `3.0.0` release with `2.2.0`.

**3.0.0 is a breaking release:** `.NET 8` (`net8.0`) and `.NET 9` (`net9.0`) are
no longer targeted, and `.NET 11` (`net11.0`) is now the primary modern target
alongside `.NET 10` (`net10.0`) and `.NET Framework 4.7.2`–`4.8.1`. This target
framework matrix change (together with `.NET 11` / `10.0.15` runtime package
floors) is the reason for the major version bump.

On retained targets (`net11.0`, `net10.0`, `net481`, `net48`, `net472`), all
existing `2.2.0` public interfaces, constructors, options, snapshots, and DI /
Hosting registration signatures remain source- and binary-compatible.

- [Release details](../CHANGELOG.md)
- [1.0.x to 2.0.0 migration guide](migration-2.0.0.md)
- [Lifecycle design and rationale](adr/0010-worker-completion-and-lifecycle.md)
- [3.0.0 target framework baseline (ADR-0011)](adr/0011-net11-target-framework-matrix.md)
- [Contextual session lifecycle (ADR-0012)](adr/0012-contextual-session-lifecycle.md)
- [`BeforeThreadStart` startup ordering (ADR-0013)](adr/0013-before-thread-start-hook.md)
- [Execution deadline extensions (ADR-0014)](adr/0014-execution-deadline-extensions.md)
- [Hosting health checks and readiness snapshots (ADR-0015)](adr/0015-hosting-health-checks-and-readiness-snapshots.md)
- [Typed `ExecutionAdmissionRejectedException` (ADR-0016)](adr/0016-typed-admission-rejected-exception.md)
- [`IExecutionDispatcher` forwarding and validated DI options (ADR-0017)](adr/0017-dispatcher-forwarding-and-validated-di-options.md)
- [`DelegateExecutionSessionFactory` and inline delegates (ADR-0018)](adr/0018-delegate-execution-session-factory.md)
- [ASP.NET Core integration and AOT-safe diagnostics (ADR-0019)](adr/0019-aspnetcore-execution-integration-and-safe-diagnostics.md)
- [Worker `MaxStackSize` and thread-affine scopes (ADR-0020)](adr/0020-worker-thread-stack-size-and-thread-affine-scopes.md)

## 1. Check and retarget your application

The three core `3.0.0` packages (`Execution`, `Execution.DependencyInjection`,
and `Execution.Hosting`) target the five-framework matrix below, while the new
`AdaskoTheBeAsT.Interop.Execution.AspNetCore` companion package targets
`net11.0` and `net10.0`:

| Application target | 3.0.0 support | Upgrade action |
| --- | --- | --- |
| `net8.0`, `net9.0` (.NET 8, .NET 9) | Removed | Retarget to `net10.0` or `net11.0`, or remain on `2.2.0`. |
| `net10.0` (.NET 10) | Retained | Update packages; ensure `Microsoft.Extensions.*` resolves `>= 10.0.15`. |
| `net11.0` (.NET 11) | Added | Supported natively with `[11.0.0, 12.0.0)` dependency bounds. |
| `net472`, `net48`, `net481` (.NET Framework 4.7.2, 4.8, 4.8.1) | Retained | Update packages; `System.Threading.Channels`, `System.Diagnostics.DiagnosticSource`, and `Microsoft.Extensions.*` move to `[11.0.0, 12.0.0)`. |

Build environments building from source require `.NET SDK 11.0.100` or later
(C# 14). Consumers referencing the compiled NuGet packages on `net10.0` or
`.NET Framework 4.7.2`–`4.8.1` only need a compatible SDK and transitive package
graph.

## 2. Update the packages you use

Update your direct package references together so all packages stay on `3.0.0`
(and optionally add the new `AdaskoTheBeAsT.Interop.Execution.AspNetCore`
package in `net11.0` / `net10.0` ASP.NET Core hosts):

```powershell
dotnet add package AdaskoTheBeAsT.Interop.Execution --version 3.0.0
dotnet add package AdaskoTheBeAsT.Interop.Execution.DependencyInjection --version 3.0.0
dotnet add package AdaskoTheBeAsT.Interop.Execution.Hosting --version 3.0.0
dotnet add package AdaskoTheBeAsT.Interop.Execution.AspNetCore --version 3.0.0
```

`AdaskoTheBeAsT.Interop.Execution.Hosting` now also references
`Microsoft.Extensions.Diagnostics.HealthChecks` (`[11.0.0, 12.0.0)` on
`net11.0` / `.NET Framework`, `[10.0.15, 11.0.0)` on `net10.0`) to provide
built-in worker and pool `IHealthCheck` implementations.

## 3. Existing contracts preserved without changes

No changes are required to existing `2.2.0` callers on supported targets:

- **Eager session startup:** `InitializeAsync` and `Initialize` continue to
  create the initial session on each dedicated worker thread before completing
  startup (in parallel across pool workers). No opt-in warm-up flag is needed.
- **Bounded queue admission:** `ExecutionWorkerOptions.QueueCapacity` and
  `ExecutionWorkerPoolOptions.QueueCapacity` (`0` = unlimited) retain atomic
  CAS admission and now throw `ExecutionAdmissionRejectedException` (which
  derives from `InvalidOperationException` and keeps the exact message
  `"The worker admission capacity is exhausted."`, so existing
  `catch (InvalidOperationException)` handlers continue working while new code
  can catch `ExecutionAdmissionRejectedException` directly and inspect
  `WorkerName` and `QueueCapacity`).
- **Snapshots:** existing `ExecutionWorkerSnapshot` and
  `ExecutionWorkerPoolSnapshot` constructors and properties (`Name`,
  `QueueDepth`, `IsFaulted`, `Fault`, `WorkerCount`, `IsAnyFaulted`, `Workers`)
  remain unchanged; readiness and disposal fields are additive.
- **Dispatcher interfaces:** `IExecutionDispatcher<TSession>`,
  `IExecutionWorker<TSession>`, and `IExecutionWorkerPool<TSession>` add no new
  abstract members, so custom test doubles and wrappers continue to compile
  unchanged.

## 4. Optional 3.0.0 capabilities to adopt

### Contextual session lifecycle and worker-indexed DI (`EX-01`)

If your session factory needs a stable worker slot (`0..WorkerCount - 1`), pool
size, session generation (`0, 1, 2, ...`), or recycle reason, implement
`IContextualExecutionSessionFactory<TSession>` (or derive from
`ContextualExecutionSessionFactory<TSession>`):

```csharp
public sealed class PatchedNativeSessionFactory
    : ContextualExecutionSessionFactory<NativeSession>
{
    public override NativeSession CreateSession(
        SessionLifecycleContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dllPath = $@"C:\native\engine_{context.WorkerIndex:D2}.dll";
        return new NativeSession(dllPath, context.SessionGeneration);
    }

    public override void DisposeSession(
        NativeSession session,
        SessionLifecycleContext context)
    {
        session.Dispose();
    }
}
```

`CreateSession` and `DisposeSession` observe the exact same
`SessionGeneration` for a given session instance, and `SessionGeneration`
advances only after a replacement session is successfully created.

DI and Hosting automatically prefer `IContextualExecutionSessionFactory<TSession>`
when registered, or you can supply a per-slot factory resolver directly:

```csharp
services.AddExecutionWorkerPoolHostedService<NativeSession>(
    (sp, workerIndex) => new SlotSessionFactory(
        sp.GetRequiredService<NativeCatalog>(),
        workerIndex),
    options => options.WorkerCount = 4);
```

### Pre-thread startup hook (`BeforeThreadStart`, `EX-06`)

When process-wide native paths, activation contexts, or COM registration must
run on the caller before any dedicated STA/MTA worker `Thread` is created, set
`BeforeThreadStart` on `ExecutionWorkerOptions` or `ExecutionWorkerPoolOptions`:

```csharp
services.AddExecutionWorkerPoolHostedService<NativeSession>(options =>
{
    options.WorkerCount = 4;
    options.UseStaThread = true;
    options.BeforeThreadStart = () => NativeBootstrap.EnsureRegisteredOnce();
});
```

For a pool, `BeforeThreadStart` runs synchronously once on the initiating thread
before any worker thread starts; if it throws, startup fails before worker
threads are spawned.

### Execution deadlines (`ExecutionDispatcherExtensions`, `EX-03`)

Replace custom pooled `CancellationTokenSource` deadline wrappers with the
extension overloads on `IExecutionDispatcher<TSession>`:

```csharp
ResultDto result = await pool.ExecuteAsync(
    (session, ct) => session.Calculate(input, ct),
    timeout: TimeSpan.FromSeconds(2),
    cancellationToken: httpContext.RequestAborted);
```

- Bounds the entire caller wait (startup, queueing, and execution).
- Throws `TimeoutException` when the deadline expires, or
  `OperationCanceledException` carrying the caller's exact `CancellationToken`
  when the caller cancels.
- Queued items whose deadline expires before dequeue never invoke the delegate.
- If the deadline expires while synchronous native code is already running, the
  caller returns promptly with `TimeoutException` while the worker thread
  remains occupied until the native delegate finishes; the linked token is kept
  alive until worker completion.

### Built-in Hosting health checks (`EX-04`)

Replace custom `/readyz` checks over `IExecutionWorker<TSession>` or
`IExecutionWorkerPool<TSession>` with the built-in health-check registrations in
`AdaskoTheBeAsT.Interop.Execution.Hosting`:

```csharp
builder.Services.AddExecutionWorkerPoolHostedService<NativeSession>(
    options => options.WorkerCount = 4);

builder.Services.AddHealthChecks()
    .AddExecutionWorkerPoolHealthCheck<NativeSession>(
        configure: health => health.QueueDepthWarningThreshold = 32,
        tags: ["ready"]);
```

- Reports `Unhealthy` before eager initialization completes, after disposal, or
  when all workers are faulted.
- Reports `Degraded` when a subset of pool workers is faulted or when
  `QueueDepth >= QueueDepthWarningThreshold`.
- Reports `Healthy` once initialized with active workers below the warning
  threshold.

### `IExecutionDispatcher<TSession>` forwarding, `IServiceProvider` options, and inline session delegates (`EX-08`, `EX-09`)

`AddExecutionWorker<TSession>` and `AddExecutionWorkerPool<TSession>` now
automatically register `IExecutionDispatcher<TSession>` via `TryAddSingleton`,
validate options on host startup via `IValidateOptions<T>` +
`.ValidateOnStart()`, and accept both `Action<IServiceProvider, TOptions>` and
inline `createSession` / `disposeSession` delegates backed by
`DelegateExecutionSessionFactory<TSession>`:

```csharp
builder.Services.AddExecutionWorkerPoolHostedService<NativeSession>(
    (sp, ctx, ct) => new NativeSession(
        sp.GetRequiredService<NativeCatalog>(),
        ctx.WorkerIndex),
    (sp, options) =>
    {
        var cfg = sp.GetRequiredService<IOptions<SolverPoolOptions>>().Value;
        options.WorkerCount = cfg.WorkerCount;
        options.QueueCapacity = cfg.QueueCapacity;
        options.MaxStackSize = 2 * 1024 * 1024;
        options.CreateWorkerThreadScope = slot => ComAutomationLocaleScope.EnterInvariant();
    });
```

### Worker-thread `MaxStackSize`, `OnWorkerThreadStart`, and `CreateWorkerThreadScope` (`EX-11`)

When dedicated worker threads need a custom stack size (`MaxStackSize > 0`) or
thread-affine setup/teardown that runs once on the dedicated STA/MTA worker
thread and survives session recyclings, configure `MaxStackSize`,
`OnWorkerThreadStart`, and `CreateWorkerThreadScope` on
`ExecutionWorkerOptions` or `ExecutionWorkerPoolOptions`. Any `IDisposable`
returned from `CreateWorkerThreadScope` is disposed on that exact worker thread
strictly after the final `DisposeSession()` during shutdown.

### ASP.NET Core Minimal API & AOT-safe diagnostics (`AdaskoTheBeAsT.Interop.Execution.AspNetCore`, `EX-10`)

In `net11.0` and `net10.0` Web APIs, replace per-endpoint `try / catch` blocks
and raw `pool.GetSnapshot()` JSON serialization with
`AdaskoTheBeAsT.Interop.Execution.AspNetCore`:

```csharp
builder.Services.AddExecutionAspNetCoreJson();
app.MapExecutionHealthProbes();
app.MapExecutionWorkerPoolDiagnostics<NativeSession>();
app.MapPost("/api/calculate", handler)
    .WithExecutionExceptionHandling("Native engine unavailable.");
```

## 5. Upgrade checklist

- [ ] Retarget any `net8.0` or `net9.0` projects to `net10.0` or `net11.0`.
- [ ] Update `AdaskoTheBeAsT.Interop.Execution` (and `.DependencyInjection` /
      `.Hosting` if used, plus optional `.AspNetCore` on `net11.0;net10.0`) to
      `3.0.0`.
- [ ] Verify `.NET Framework` and `net10.0` transitive `Microsoft.Extensions.*`
      versions satisfy `[11.0.0, 12.0.0)` and `[10.0.15, 11.0.0)` respectively.
- [ ] Optionally replace string-matched queue-saturation catches, boilerplate
      `*SessionFactory` classes, manual `IExecutionDispatcher<TSession>`
      forwarding, deadline `CancellationTokenSource` wrappers, readiness
      `IHealthCheck` classes, and Minimal API exception/diagnostics boilerplate
      with the built-in `3.0.0` APIs.
