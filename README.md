# AdaskoTheBeAsT.Interop

> A focused interop toolbox for dedicated-thread execution, native library isolation, and COM-friendly workloads.

This repository contains the `AdaskoTheBeAsT.Interop.Execution` family and is designed to work alongside sibling packages such as `AdaskoTheBeAsT.Interop.COM`, `AdaskoTheBeAsT.Interop.Threading`, and `AdaskoTheBeAsT.Interop.Unmanaged`.

## Why this exists

Interop code is fun right up until it isn't:

- native libraries want thread affinity
- COM components want STA and message pumping
- some engines behave better when a single background thread owns all state
- some workloads need explicit load / unload / recycle behavior instead of "hope the process survives"

`AdaskoTheBeAsT.Interop.Execution` gives you a reusable worker (and pool) for exactly that shape of problem.

## Packages in this repo

| Package | What it gives you |
| --- | --- |
| `AdaskoTheBeAsT.Interop.Execution` | Core library: `ExecutionWorker<TSession>`, `ExecutionWorkerPool<TSession>`, `IExecutionSessionFactory<TSession>`, options, scheduler seam, diagnostics. |
| `AdaskoTheBeAsT.Interop.Execution.DependencyInjection` | `Microsoft.Extensions.DependencyInjection` helpers: `AddExecutionWorker<TSession>()` / `AddExecutionWorkerPool<TSession>()` with `IOptions<T>` binding. |
| `AdaskoTheBeAsT.Interop.Execution.Hosting` | `Microsoft.Extensions.Hosting` integration: `IHostedService` wrappers for worker / pool lifetime driven by the generic host. |

### Install

```powershell
dotnet add package AdaskoTheBeAsT.Interop.Execution
dotnet add package AdaskoTheBeAsT.Interop.Execution.DependencyInjection
dotnet add package AdaskoTheBeAsT.Interop.Execution.Hosting
```

Symbols ship as `.snupkg` with Source Link and embedded untracked sources, so you can step into library code from any debugger.

## Target framework matrix

| TFM | Notes |
| --- | --- |
| `net10.0`, `net9.0`, `net8.0` | Primary targets; in-box `System.Threading.Channels` + `System.Diagnostics.DiagnosticSource`. |
| `net481`, `net48`, `net472`, `net471`, `net47`, `net462` | Windows desktop; adds `System.Threading.Channels` + `System.Diagnostics.DiagnosticSource` via NuGet, plus the `IsExternalInit` polyfill. |

Every TFM is built with `TreatWarningsAsErrors=true`, `ContinuousIntegrationBuild=true`, `Deterministic=true`, and every cell of the matrix is exercised in CI.

## Core idea

Instead of letting every interop-heavy engine invent its own:

- queue
- worker thread
- startup synchronization
- session lifetime
- disposal logic
- failure / recycle behavior

you move that generic machinery into `ExecutionWorker<TSession>` or `ExecutionWorkerPool<TSession>`. Your engine becomes a thin adapter that says:

1. how to create a session
2. how to dispose a session
3. what work should run on that session

## Main types

### `ExecutionWorker<TSession>`

A single dedicated background thread that owns one `TSession`. Submitted work runs sequentially in FIFO order. Implements `IDisposable` and `IAsyncDisposable`.

Owns:

- a multi-writer / single-reader `Channel` of work items
- one dedicated background `Thread` (optionally STA on Windows)
- startup / shutdown lifecycle (sync `Initialize` + async `InitializeAsync(CancellationToken)`)
- cooperative cancellation of pending items on shutdown
- session reuse
- session recycle after failure or after N operations
- observability (`IsFaulted`, `Fault`, `QueueDepth`, `WorkerFaulted` event)

### `ExecutionWorkerPool<TSession>`

Fan-out pool of `ExecutionWorker<TSession>` instances. Each pool worker owns a private session and a private queue; a pluggable `IWorkerScheduler<TSession>` picks which worker receives each submission.

Owns:

- multiple `ExecutionWorker<TSession>` instances
- pluggable work distribution (see "Scheduling" below)
- one session per worker (ideal for isolated native DLL sets)
- per-worker isolation for native state
- parallel initialization and parallel disposal
- aggregate observability (`QueueDepth`, `IsAnyFaulted`, `WorkerFaults`, forwarded `WorkerFaulted`)

### `IExecutionSessionFactory<TSession>`

```csharp
public interface IExecutionSessionFactory<TSession>
    where TSession : class
{
    TSession CreateSession(CancellationToken cancellationToken);
    void DisposeSession(TSession session);
}
```

The factory owns creating the thread-affine session (loading native libraries, initializing modules) and disposing / unloading it. Both methods run on the dedicated worker thread.

### `ExecutionWorkerOptions`

`name`, `useStaThread`, `maxOperationsPerSession` (`0` = unlimited), `disposeTimeout` (default `Timeout.InfiniteTimeSpan`). Parameterless ctor + positional ctor + public setters so it binds cleanly via `IOptions<T>`.

### `ExecutionWorkerPoolOptions`

`workerCount`, `name`, `useStaThread`, `maxOperationsPerSession`, `disposeTimeout`, `schedulingStrategy` (default `LeastQueued`). Same binding story.

### `ExecutionRequestOptions`

Per-call knob: `recycleSessionOnFailure` (default `false`).

## STA behavior

If `useStaThread: true` is set:

- on Windows, the worker thread is configured as `STA` via `SetApartmentState(ApartmentState.STA)` (guarded by `OperatingSystem.IsWindows()` on net5+ and `PlatformID.Win32NT` on older TFMs).
- on non-Windows, the flag is silently ignored.

That makes the option safe for cross-platform callers that want "STA when possible" behavior.

## Quick example

```csharp
using AdaskoTheBeAsT.Interop.Execution;

public sealed class NativeSession
{
    public byte[] Render(string html) => [];
}

public sealed class NativeSessionFactory : IExecutionSessionFactory<NativeSession>
{
    public NativeSession CreateSession(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new NativeSession();
    }

    public void DisposeSession(NativeSession session)
    {
    }
}

await using var worker = new ExecutionWorker<NativeSession>(
    new NativeSessionFactory(),
    new ExecutionWorkerOptions(
        name: "Native Render Worker",
        useStaThread: true,
        maxOperationsPerSession: 500));

await worker.InitializeAsync();

var bytes = await worker.ExecuteAsync(
    (session, cancellationToken) => session.Render("<h1>Hello</h1>"),
    new ExecutionRequestOptions(recycleSessionOnFailure: true),
    cancellationToken);
```

### `ValueTask` hot path

When the caller is already in `ValueTask` land (e.g. wrapping a native-interop async API) the `ExecuteValueAsync` extension overloads keep you in that domain without manually wrapping the returned `Task`:

```csharp
using AdaskoTheBeAsT.Interop.Execution;

int sessionId = await worker.ExecuteValueAsync(
    (session, _) => session.SessionId);
```

## Pool: multiple workers

If one worker is not enough, use `ExecutionWorkerPool<TSession>`. Typical fits:

- several native library copies in separate folders
- each worker should load its own isolated DLL set
- one failed worker session should recycle without touching the others
- several dedicated threads, but still serialized execution per worker

```csharp
await using var pool = new ExecutionWorkerPool<NativePoolSession>(
    workerIndex => new NativePoolSessionFactory($@"c:\native\slot-{workerIndex + 1:D2}"),
    new ExecutionWorkerPoolOptions(
        workerCount: 4,
        name: "Native Pool",
        useStaThread: true,
        maxOperationsPerSession: 250));

await pool.InitializeAsync();

var result = await pool.ExecuteAsync(
    (session, ct) => session.Render("<h1>Hello from pool</h1>"),
    new ExecutionRequestOptions(recycleSessionOnFailure: true),
    cancellationToken);
```

## Scheduling

The pool ships with two built-in schedulers and a public `IWorkerScheduler<TSession>` seam if you need something bespoke.

| Built-in | Semantics |
| --- | --- |
| `LeastQueuedWorkerScheduler<TSession>` (default) | Picks the healthy worker with the smallest `QueueDepth`. Ties break via a shared rolling index so equal-depth workers rotate. Skips faulted workers. Early-exits when it finds a zero-depth worker. |
| `RoundRobinWorkerScheduler<TSession>` | Strict rotation across healthy workers via an `Interlocked` index. Skips faulted workers. |

Swap built-ins via options, or plug in a custom scheduler via the pool ctor:

```csharp
// Option A: pick a built-in via options.
var opts = new ExecutionWorkerPoolOptions(
    workerCount: 4,
    schedulingStrategy: SchedulingStrategy.RoundRobin);

// Option B: inject a custom scheduler.
IWorkerScheduler<NativeSession> custom = new MyAffinityScheduler();
await using var pool = new ExecutionWorkerPool<NativeSession>(
    workerIndex => new NativeSessionFactory(),
    new ExecutionWorkerPoolOptions(workerCount: 4),
    custom);
```

Rationale, trade-offs, and the faulted-worker contract are captured in [`docs/adr/0002-pluggable-worker-scheduler.md`](docs/adr/0002-pluggable-worker-scheduler.md).

## When to use what

### Choose `ExecutionWorker<TSession>` when

- the native engine is effectively process-global
- the library is known to be thread-sensitive
- you want strict serialized access to one engine instance
- you want exactly one owner thread

### Choose `ExecutionWorkerPool<TSession>` when

- you have isolated native copies per worker
- the library can run in parallel across separate worker-owned sessions
- you want better throughput
- you want one worker to recycle independently from the others

## Session recycle story

You can choose to recycle the session:

- after a failed request (`ExecutionRequestOptions.RecycleSessionOnFailure = true`)
- after a fixed number of operations (`ExecutionWorkerOptions.MaxOperationsPerSession > 0`)
- or both

Set `maxOperationsPerSession: 0` when you want unlimited session lifetime and only failure-based recycling.

## DI integration

```csharp
using AdaskoTheBeAsT.Interop.Execution;
using AdaskoTheBeAsT.Interop.Execution.DependencyInjection;

services.AddSingleton<IExecutionSessionFactory<NativeSession>, NativeSessionFactory>();
services.AddExecutionWorker<NativeSession>(options =>
{
    options.Name = "Native Render Worker";
    options.UseStaThread = true;
    options.MaxOperationsPerSession = 500;
});

// resolve IExecutionWorker<NativeSession> from DI and use it as usual
```

`AddExecutionWorkerPool<TSession>` is the pool-flavored equivalent and binds `IOptions<ExecutionWorkerPoolOptions>`.

## Generic host integration

```csharp
using AdaskoTheBeAsT.Interop.Execution.Hosting;

services.AddSingleton<IExecutionSessionFactory<NativeSession>, NativeSessionFactory>();
services.AddExecutionWorkerHostedService<NativeSession>(options =>
{
    options.Name = "Native Render Worker";
    options.UseStaThread = true;
});
```

The `IHostedService` wrappers drive `InitializeAsync` on `StartAsync` and `DisposeAsync` on `StopAsync`, idempotent against double-stop. `AddExecutionWorkerPoolHostedService<TSession>` covers the pool.

## Observability

Every worker emits to a shared `ActivitySource` and `Meter` named `AdaskoTheBeAsT.Interop.Execution`.

| Instrument | Kind | Tags |
| --- | --- | --- |
| `ExecutionWorker.Execute` | `Activity` (span) | `worker.name` |
| `execution.worker.operations` | `Counter<long>` | `worker.name`, `outcome` ∈ `success` / `faulted` / `cancelled` |
| `execution.worker.session_recycles` | `Counter<long>` | `worker.name`, `reason` ∈ `max_operations` / `failure` |
| `execution.worker.queue_depth` | `ObservableGauge<int>` | `worker.name` |

All these identifiers are exposed as `public const string` on `ExecutionDiagnosticNames`, so telemetry pipelines can subscribe without hard-coding strings:

```csharp
using AdaskoTheBeAsT.Interop.Execution;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(ExecutionDiagnosticNames.SourceName))
    .WithMetrics(m => m.AddMeter(ExecutionDiagnosticNames.SourceName));
```

When no listener is attached, `StartActivity` returns `null` and the instrumentation is allocation-free.

See [`docs/adr/0003-public-diagnostic-constants.md`](docs/adr/0003-public-diagnostic-constants.md) for why the identifiers are a public contract.

## Faulting semantics

`ExecutionWorker<TSession>` is **terminal-once**: when a work item throws a non-cancellation exception during startup or session creation — or when `DisposeSession` throws during shutdown — `_fatalFailure` latches, `IsFaulted` flips to `true`, and `WorkerFaulted` fires exactly once. Subsequent `ExecuteAsync` calls rethrow the original exception synchronously (unwrapped via `ExceptionDispatchInfo`). The worker cannot be re-initialised after faulting.

Pool consumers observe the same contract aggregated: `IsAnyFaulted`, `WorkerFaults`, and a forwarded `WorkerFaulted` event carrying the originating worker name.

## Build and test

```powershell
dotnet build  .\AdaskoTheBeAsT.Interop.slnx
dotnet test   .\AdaskoTheBeAsT.Interop.slnx --no-build
```

Test suites:

| Project | Role |
| --- | --- |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.Test` | Unit + behavioural tests (fault propagation, dispose idempotency, cancellation, telemetry smoke, scheduler contract). |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test` | DI registration, options binding, lifetime. |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.Hosting.Test` | `IHostedService` start/stop lifecycle, idempotent shutdown. |
| `test/integ/AdaskoTheBeAsT.Interop.Execution.IntegrationTest` | Multi-threaded submission, STA on Windows, reentrant dispose, session recycling, `ValueTask` overloads, diagnostic-names contract. |

All four projects run across the full 9-target matrix (`net10.0`, `net9.0`, `net8.0`, `net481`, `net48`, `net472`, `net471`, `net47`, `net462`).

## Architecture Decision Records

Small, self-contained design decisions taken on this codebase live under [`docs/adr/`](docs/adr/). Start with the [index](docs/adr/README.md).

## Extra notes

- WkHtml migration details live in [`wkhtml.md`](./wkhtml.md).
- Design rationale for every recent change is under [`docs/adr/`](docs/adr/).

---

Built for the kind of interop code that likes one owner thread, explicit lifecycle, and zero drama.
