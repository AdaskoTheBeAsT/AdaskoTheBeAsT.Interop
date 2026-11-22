# AdaskoTheBeAsT.Interop

> A focused interop toolbox for dedicated-thread execution, native library isolation, and COM-friendly workloads.

**New in 3.1:** Use `ExecutionPoolConfiguration.IsValid` in your options validation
and `ConfigureStaPool` in the hosted pool callback. Pass your own worker limit and
keep any custom stack size, startup hooks, or deadline application in your integration.
See [ADR 0021](docs/adr/0021-hosted-pool-configuration.md).

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Execution.svg?label=AdaskoTheBeAsT.Interop.Execution&logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution/)
[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Execution.DependencyInjection.svg?label=Execution.DependencyInjection&logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.DependencyInjection/)
[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Execution.Hosting.svg?label=Execution.Hosting&logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.Hosting/)
[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Execution.AspNetCore.svg?label=Execution.AspNetCore&logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.AspNetCore/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
![TFMs](https://img.shields.io/badge/TFMs-net11.0%20%7C%20net10.0%20%7C%20net4.7.2%E2%80%93net4.8.1-512BD4?logo=dotnet)
![Warnings](https://img.shields.io/badge/warnings--as--errors-on-green)
![Deterministic](https://img.shields.io/badge/deterministic%20build-on-blue)

### 🔬 Code quality — SonarCloud

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=coverage)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=coverage)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=sqale_rating)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=sqale_rating)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=reliability_rating)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=reliability_rating)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=security_rating)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=security_rating)
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=bugs)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=bugs)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=vulnerabilities)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=vulnerabilities)
[![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=code_smells)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=code_smells)
[![Duplicated Lines (%)](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=duplicated_lines_density)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=duplicated_lines_density)
[![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=sqale_index)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=sqale_index)
[![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=ncloc)](https://sonarcloud.io/component_measures?id=AdaskoTheBeAsT_AdaskoTheBeAsT.Interop&metric=ncloc)

---

## 👋 Hello, interop friend

Interop code is fun, right up until it *isn't*. You know the signs:

- 🧬 a native library that secretly wants thread affinity
- 🏢 a COM component that quietly insists on STA + message pumping
- 🧸 an engine that loses its mind if two threads touch it at once
- ♻️ a workload that needs explicit load / unload / recycle, not "hope the process survives"

`AdaskoTheBeAsT.Interop.Execution` is the reusable boilerplate you keep rewriting in every project: a dedicated worker thread (or a pool of them), a session it owns, a queue in front of it, and all the cancellation / disposal / telemetry plumbing that *should* just be a library by now. 📦

And now it is. ✨

---

## Changelog

### 3.0.0

The version in [`Directory.Build.props`](Directory.Build.props) is **3.0.0**
across all four packages. This release upgrades the repository to **.NET 11**
(`11.0.100`) and **C# 14**, moves the multi-targeted matrix to **`net11.0`,
`net10.0`, `net481`, `net48`, and `net472`** (removing `net8.0` and `net9.0`),
introduces **`AdaskoTheBeAsT.Interop.Execution.AspNetCore`** (`net11.0;net10.0`),
and adds contextual session lifecycle metadata, delegate session factories,
`IExecutionDispatcher<TSession>` DI forwarding, validated `IServiceProvider`
options, typed admission-rejection exceptions, dedicated worker-thread stack
sizing and scopes, execution deadline extensions, and Hosting `IHealthCheck`
support:

- **Contextual session lifecycle & `DelegateExecutionSessionFactory<TSession>`:**
  `SessionLifecycleContext`, `SessionRecycleReason`,
  `ContextualExecutionSessionFactory<TSession>`, and
  `DelegateExecutionSessionFactory<TSession>` pass a stable `WorkerIndex`
  (`0..WorkerCount - 1`), `WorkerCount`, `WorkerName`, `ManagedThreadId`,
  `PreviousRecycleReason`, and matching `SessionGeneration` values to both
  `CreateSession` and `DisposeSession`, with single-lambda DI and Hosting
  registration overloads.
- **Ergonomic DI & Hosting registration:** `AddExecutionWorker<TSession>` and
  `AddExecutionWorkerPool<TSession>` automatically forward
  `IExecutionDispatcher<TSession>` via `TryAddSingleton`, add
  `Action<IServiceProvider, ...>` and worker-indexed overloads (mirrored on
  `AddExecutionWorkerHostedService<TSession>` and
  `AddExecutionWorkerPoolHostedService<TSession>`), and validate options on
  startup via `IValidateOptions<T>` + `.ValidateOnStart()`.
- **Worker-thread lifecycle & `MaxStackSize`:** optional caller-side
  `BeforeThreadStart` plus dedicated-thread `MaxStackSize`,
  `OnWorkerThreadStart`, and worker-lifetime `CreateWorkerThreadScope`
  (`IDisposable` disposed on the same worker thread strictly after final
  `DisposeSession()`).
- **Typed `ExecutionAdmissionRejectedException` & execution deadlines:**
  bounded `QueueCapacity` saturation throws
  `ExecutionAdmissionRejectedException : InvalidOperationException` carrying
  `WorkerName` and `QueueCapacity`, while `ExecutionDispatcherExtensions` bounds
  startup, queueing, and execution waits with `TimeSpan timeout`.
- **Hosting health checks & `Execution.AspNetCore` integration:** extended
  `ExecutionWorkerSnapshot` / `ExecutionWorkerPoolSnapshot` readiness fields,
  built-in `IHealthCheck` registrations in `Execution.Hosting`, and a new
  `AdaskoTheBeAsT.Interop.Execution.AspNetCore` companion package (`net11.0;net10.0`)
  providing `WithExecutionExceptionHandling`, `AddExecutionExceptionHandler`,
  AOT-safe source-generated diagnostics DTOs (`MapExecutionWorkerDiagnostics`,
  `MapExecutionWorkerPoolDiagnostics`), and `MapExecutionHealthProbes`.
- **Target framework & dependency updates:** added `net11.0`, removed `net8.0`
  and `net9.0`, updated `net11.0` and `.NET Framework` package ranges to
  `[11.0.0, 12.0.0)` and `net10.0` `Microsoft.Extensions.*` ranges to
  `[10.0.15, 11.0.0)`, and updated SDK (`11.0.100`), analyzer, and `xunit.v3`
  (`4.0.3`) packages.

See the [2.x to 3.0.0 migration guide](docs/migration-3.0.0.md) and the
[full changelog](CHANGELOG.md) for release history.

---

## Why upgrade to 2.0.0?

**2.0.0 makes worker execution and shutdown more reliable and raises the minimum
.NET Framework version to 4.7.2.** Dropping
`net462`, `net47`, and `net471` is a breaking change and the reason for the major
version. Existing integration APIs remain available on supported targets.
The comparison below is against the tagged **1.0.0** release.

| If your application... | What could go wrong in 1.0.0 | What 2.0.0 improves |
| --- | --- | --- |
| Uses `ExecuteValueAsync` under load | A caller could return a pooled source while the worker still used it; delegate failures bypassed worker recovery. | Completion is the worker's last access to the item. Task and ValueTask requests share failure, cancellation, recycling, and outcome reporting. |
| Recycles native sessions | A request could report success before required teardown finished. | Awaiting the request includes recycle teardown. Cleanup failures become observable instead of looking like successful work. |
| Submits work before initialization finishes | An async submission could block its calling thread on session creation. | Submissions enqueue without waiting for creation and retain per-worker FIFO order. Startup failures settle the returned requests. |
| Shuts down from several places | A repeated or reentrant disposal could make an external caller think teardown was already complete. | Every external `DisposeAsync()` joins actual teardown, even after a synchronous disposal timeout. |
| Receives bursts of work | Admission was unbounded. | Opt into `QueueCapacity` to limit waiting requests and reject excess work explicitly, including during startup. |
| Needs a shutdown policy | Pending-work handling was not an explicit choice. | Choose `Drain` or `CancelPending`. Hosted shutdown also observes the host's cancellation deadline without canceling cleanup. |
| Relies on faults and tracing | Fault handlers ran on the owning thread before `Fault` was published; listener errors could disrupt execution. | Fault state is latched before off-thread notification. Listener exceptions are contained, and spans use the submitting request's activity context. |
| Reuses options or custom pool schedulers | Later option mutations could alter live behavior; a scheduler could select a foreign concrete worker. | Workers snapshot configuration, pools reject non-members, and partial pool construction rolls back earlier workers. |

**What is not new:** pooled `ExecuteValueAsync`, DI/Hosting,
session recycling, schedulers, snapshots, and scoped diagnostics already shipped
in 1.0.0. This is a correctness and lifecycle upgrade, not a claim of faster
native execution or a new benchmark result.

**Before upgrading:** applications targeting .NET Framework 4.6.2, 4.7, or 4.7.1
must retarget to 4.7.2 or later, or remain on 1.0.0. Modern .NET 8, 9, and 10
targets are unchanged.

On supported targets, existing public signatures are retained. The new options
default to unlimited admission (`QueueCapacity = 0`) and `Drain`; applications
receive the fixes without opting into queue limits. Completion timing, event
ordering, and option validation do change, so review the
[1.0.x to 2.0.0 migration guide](docs/migration-2.0.0.md) before upgrading.
See the [changelog](CHANGELOG.md) for release details.

---

## ✨ Why you'll love this

- **Pooled ValueTask path.** `ExecuteValueAsync` reuses `IValueTaskSource<T>` work items on every supported TFM, including `net472`, avoiding a per-request source/Task allocation when a pooled item is available. This is not a guarantee of zero total allocations. ([Completion contract](docs/adr/0010-worker-completion-and-lifecycle.md))
- **DI + Hosting.** Use `AddExecutionWorker<TSession>()` for plain DI, or `AddExecutionWorkerHostedService<TSession>()` to register both the worker and its hosted lifecycle.
- 💫 **Pluggable schedulers.** `LeastQueued` and `RoundRobin` ship in the box; bring your own via `IWorkerScheduler<TSession>`. ([ADR-0002](docs/adr/0002-pluggable-worker-scheduler.md))
- 🔭 **Batteries-included observability.** `ActivitySource` + `Meter` with public constant names, ready for OpenTelemetry. ([ADR-0003](docs/adr/0003-public-diagnostic-constants.md))
- 🪟 **First-class Windows STA.** Flip a boolean, get an STA worker thread on Windows; silently ignored elsewhere.
- ♻️ **Real session recycling.** After N operations, after a failure, or both — your call.
- 🛡️ **Terminal-once faulting.** When a worker goes bad, it says so *once*, loudly, via `WorkerFaulted` — no silent-dead-thread surprises.
- ⏱️ **Built-in execution deadlines & health checks.** Bound caller waits with `TimeSpan` extensions on `IExecutionDispatcher<TSession>` and wire readiness/degradation checks straight into `AddHealthChecks()`.
- **5 target frameworks.** `net11.0`, `net10.0`, `net481`, `net48`, `net472`. .NET Framework 4.7.2 is the desktop minimum and .NET 10 is the modern .NET minimum in 3.0.0.
- ✏️ **Source Link + snupkg.** Step into the library from your debugger without guessing.

---

## 📦 Packages

| Package | What it gives you |
| --- | --- |
| [`AdaskoTheBeAsT.Interop.Execution`](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution/) | ⚓ Core: `ExecutionWorker<TSession>`, `ExecutionWorkerPool<TSession>`, `IExecutionSessionFactory<TSession>`, `IContextualExecutionSessionFactory<TSession>`, `DelegateExecutionSessionFactory<TSession>`, `ExecutionAdmissionRejectedException`, deadline extensions, options, schedulers, diagnostics. |
| [`AdaskoTheBeAsT.Interop.Execution.DependencyInjection`](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.DependencyInjection/) | 🧩 `Microsoft.Extensions.DependencyInjection` helpers: `AddExecutionWorker<TSession>()` / `AddExecutionWorkerPool<TSession>()` with automatic `IExecutionDispatcher<TSession>` forwarding, inline session delegates, worker-indexed resolvers, and `ValidateOnStart` `IOptions<T>` binding. |
| [`AdaskoTheBeAsT.Interop.Execution.Hosting`](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.Hosting/) | 🏗️ `Microsoft.Extensions.Hosting` & `HealthChecks` integration: `IHostedService` wrappers and `IHealthCheck` registrations driving worker / pool lifetime and readiness from the generic host. |
| [`AdaskoTheBeAsT.Interop.Execution.AspNetCore`](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.AspNetCore/) | 🌐 ASP.NET Core Minimal API & `ProblemDetails` integration (`net11.0;net10.0`): `WithExecutionExceptionHandling`, `AddExecutionExceptionHandler`, AOT-safe source-generated diagnostics DTOs (`MapExecutionWorkerDiagnostics`, `MapExecutionWorkerPoolDiagnostics`), and `MapExecutionHealthProbes`. |

### ⬇️ Install

```powershell
dotnet add package AdaskoTheBeAsT.Interop.Execution
dotnet add package AdaskoTheBeAsT.Interop.Execution.DependencyInjection
dotnet add package AdaskoTheBeAsT.Interop.Execution.Hosting
dotnet add package AdaskoTheBeAsT.Interop.Execution.AspNetCore
```

Symbols ship as `.snupkg` with Source Link and embedded untracked sources. Step in. Look around. It's fine.

---

## 🗺️ Target framework matrix

| TFM | Status | Notes |
| --- | :-: | --- |
| `net11.0` | ✅ | Primary target across all 4 packages; in-box `System.Threading.Channels` + `System.Diagnostics.DiagnosticSource`. |
| `net10.0` | ✅ | Primary LTS target across all 4 packages. |
| `net481` | ✅ | Windows desktop (`Execution`, `DependencyInjection`, `Hosting`); `System.Threading.Channels` + `System.Diagnostics.DiagnosticSource` via NuGet + `IsExternalInit` polyfill. |
| `net48` | ✅ | Same as above. |
| `net472` | ✅ | Same as above. |

The five-target matrix applies to `Execution`, `Execution.DependencyInjection`,
and `Execution.Hosting`, while `Execution.AspNetCore` targets `net11.0` and
`net10.0` (`Microsoft.AspNetCore.App`).
**Removed in 3.0.0:** `net8.0` (.NET 8) and `net9.0` (.NET 9); see the
[2.x to 3.0.0 migration guide](docs/migration-3.0.0.md).
**Removed in 2.0.0:** `net462` (.NET Framework 4.6.2), `net47` (4.7), and
`net471` (4.7.1); see the [1.0.x to 2.0.0 migration guide](docs/migration-2.0.0.md).

CI enables `TreatWarningsAsErrors=true`, `ContinuousIntegrationBuild=true`, and
`Deterministic=true`. The four multi-targeted test projects target this same
five-TFM matrix, while `Execution.AspNetCore.Test` targets `net11.0` and
`net10.0`.

---

## 💡 The core idea

Instead of every interop-heavy engine reinventing:

- a queue 📑
- a worker thread 🧵
- startup synchronisation 🚀
- session lifetime ⏳
- disposal logic 🗑️
- failure / recycle behaviour ♻️

...you park that generic machinery in `ExecutionWorker<TSession>` or `ExecutionWorkerPool<TSession>`. Your engine becomes a thin adapter that answers three questions:

1. 🌱 How do I create a session?
2. 🥀 How do I dispose a session?
3. 🛠️ What work should run on that session?

That's it. The rest is the library's problem now.

```
                        ┌──────────────────────────────┐
   ExecuteAsync(x) ──▶  │   Channel<ExecutionWorkItem> │
                        │   (multi-writer, 1 reader)   │
                        └──────────────┬───────────────┘
                                       │
                                       ▼
                            ┌───────────────────────┐
                            │  Dedicated Thread     │
                            │  owns ONE TSession    │  ◀──  STA on Windows
                            │  runs work in FIFO    │        if you ask
                            └───────────┬───────────┘
                                        │
                                        ▼
                                ┌─────────────┐
                                │  TSession   │  (native libs, COM, ...)
                                └─────────────┘
```

---

## 📚 Main types

### ⚙️ `ExecutionWorker<TSession>`

A single dedicated background thread that owns one `TSession`. Submitted work runs sequentially in FIFO order. Implements `IDisposable` *and* `IAsyncDisposable`.

Owns 👇

- a multi-writer / single-reader `Channel` of work items
- one dedicated background `Thread` (optionally STA on Windows)
- startup / shutdown lifecycle (`InitializeAsync(CancellationToken)` + sync `Initialize`)
- configurable draining or cancellation of pending items on shutdown
- session reuse + session recycle after failure or after N operations
- observability via `Name`, `IsFaulted`, `Fault`, `QueueDepth`, `WorkerFaulted`, and the uniform `GetSnapshot()`

### ⚙️⚙️⚙️⚙️ `ExecutionWorkerPool<TSession>`

Fan-out pool of `ExecutionWorker<TSession>` instances. Each pool worker owns a private session and a private queue; a pluggable `IWorkerScheduler<TSession>` picks which worker receives each submission.

Owns 👇

- multiple `ExecutionWorker<TSession>` instances
- pluggable work distribution (see [Scheduling](#-scheduling) below)
- one session per worker (ideal for isolated native DLL sets)
- separate worker-owned sessions (process-global native state still needs adapter-level isolation)
- parallel initialization and parallel disposal
- aggregate observability (`QueueDepth`, `IsAnyFaulted`, `WorkerFaults`, forwarded `WorkerFaulted`, and per-worker snapshots via `GetSnapshot().Workers`)

### 🏭 `IExecutionSessionFactory<TSession>`, `IContextualExecutionSessionFactory<TSession>`, and `DelegateExecutionSessionFactory<TSession>`

```csharp
public interface IExecutionSessionFactory<TSession>
    where TSession : class
{
    TSession CreateSession(CancellationToken cancellationToken);
    void DisposeSession(TSession session);
}
```

Creates the thread-affine session (loading native libs, initialising modules) and disposes / unloads it. Both methods run on the dedicated worker thread.

Need the stable worker slot (`0..WorkerCount - 1`), pool size, session generation (`0, 1, 2, ...`), or previous recycle reason (`MaxOperationsReached` / `OperationFaulted`)? Implement `IContextualExecutionSessionFactory<TSession>` (or subclass `ContextualExecutionSessionFactory<TSession>`): `CreateSession` and `DisposeSession` receive the same `SessionLifecycleContext.SessionGeneration` for a given session lifetime.

Don't want to write a custom factory class just to invoke a constructor and `Dispose()`? Use `DelegateExecutionSessionFactory<TSession>` (or the inline delegate overloads in DI and Hosting):

```csharp
var factory = new DelegateExecutionSessionFactory<NativeSession>(
    (context, ct) => new NativeSession(context.WorkerIndex));
// Automatically checks ct.ThrowIfCancellationRequested() and calls (session as IDisposable)?.Dispose().
```

### 🎛️ `ExecutionWorkerOptions`

`Name`, `UseStaThread`, `MaxStackSize` (`0` = runtime default, `> 0` = dedicated thread stack reservation in bytes), `MaxOperationsPerSession` (`0` = unlimited), `DisposeTimeout` (default `Timeout.InfiniteTimeSpan`), `Diagnostics` (scoped `ExecutionDiagnostics` instance — defaults to a process-wide `Shared` singleton), `BeforeThreadStart` (optional callback invoked synchronously on the caller before creating and starting the worker thread), `OnWorkerThreadStart` (invoked once on the dedicated worker thread after `ApartmentState` is set and before initial `CreateSession`), and `CreateWorkerThreadScope` (`Func<IDisposable?>?` invoked once on the dedicated worker thread before initial `CreateSession`, surviving session recyclings, and disposed on that exact worker thread strictly after the final `DisposeSession()`). Parameterless ctor + positional ctor + public setters + public `Validate()` so it binds cleanly via `IOptions<T>`.

`QueueCapacity` limits requests waiting for startup or execution (`0` = unlimited).
`ShutdownMode` selects `Drain` (default) or `CancelPending`. Options are validated
and copied when the worker is constructed; later mutations do not reconfigure it.

### 🎛️ `ExecutionWorkerPoolOptions`

`WorkerCount`, `Name`, `UseStaThread`, `MaxStackSize`, `MaxOperationsPerSession`, `DisposeTimeout`, `SchedulingStrategy` (default `LeastQueued`), `Diagnostics`, `BeforeThreadStart` (invoked synchronously once per pool startup attempt before any worker thread is created), `OnWorkerThreadStart` (`Action<int>?` receiving `workerIndex`), and `CreateWorkerThreadScope` (`Func<int, IDisposable?>?` receiving `workerIndex`). Same binding and `Validate()` story.

`QueueCapacity` and `ShutdownMode` apply to each worker. Pool options are also
copied at construction. Capacity is per worker, not a shared pool-wide limit.

### 🎛️ `ExecutionRequestOptions`

Per-call knob: `RecycleSessionOnFailure` (default `false`).

---

## 🪟 STA behavior

If `UseStaThread: true` is set:

- ✅ On Windows, the worker thread is configured as `STA` via `SetApartmentState(ApartmentState.STA)` (guarded by `OperatingSystem.IsWindows()` on `net5+` and `PlatformID.Win32NT` on older TFMs).
- 🤷 On non-Windows, the flag is silently ignored.

That makes the option safe for cross-platform callers that want "STA when possible" behaviour.

The worker does **not** run a COM or UI message pump. STA alone is insufficient
for components that require one. Separate worker sessions also do not isolate
process-global native state; the adapter must supply any required process-wide
serialization or process isolation.

## Startup, admission, cancellation, and shutdown

- Submit **synchronous** delegates only. An async lambda can escape the owning
  thread and outlive its session. Do not synchronously wait for nested work on
  the same worker.
- Async submissions return without waiting for initial session creation. Work
  is queued in FIFO order on each worker, including during startup.
- `InitializeAsync(token)` cancels only that caller's wait. Shared startup
  continues for other callers. Disposing the worker separately requests
  cancellation of initial session creation.
- A full `QueueCapacity` faults the submission with
  `ExecutionAdmissionRejectedException` (which derives from
  `InvalidOperationException` and exposes `WorkerName` and `QueueCapacity`);
  it does not block or silently drop work. The executing request is excluded
  from the limit. A pool does not retry another worker after capacity rejection.
- A request canceled before execution is skipped when dequeued. Once running,
  its delegate must observe its own token. Neither request cancellation nor
  shutdown forcibly interrupts managed or native code.
- `Drain` completes queued work on an available session. `CancelPending` skips
  requests not yet started. Both let running delegates finish before teardown.
  If initial creation is canceled or fails, queued requests cannot run and are
  completed with cancellation or failure.
- Every external `DisposeAsync()` joins the same actual teardown. A call from
  the owning worker only requests shutdown, avoiding a self-deadlock.
  Synchronous `Dispose()` can abandon its wait at `DisposeTimeout`; cleanup
  continues and a later `DisposeAsync()` still joins it.
- Hosted `StopAsync(token)` uses the host deadline to bound its wait, not the
  cleanup itself. If the token fires before teardown completes, awaiting
  `StopAsync` throws `OperationCanceledException`. Container disposal may still
  wait for a blocked native call.

Hard deadlines and recovery from a hung native call require a separate process.
See [ADR-0010](docs/adr/0010-worker-completion-and-lifecycle.md) for the ownership
and compatibility decisions.

---

## 🚀 Quick example

This complete console example uses top-level statements on modern .NET. The
session is a stub; replace `Render` and the factory cleanup with your native API.

```csharp
using System.Text;
using AdaskoTheBeAsT.Interop.Execution;

CancellationToken cancellationToken = CancellationToken.None;

// 1. Spin up the worker.
await using var worker = new ExecutionWorker<NativeSession>(
    new NativeSessionFactory(),
    new ExecutionWorkerOptions(
        name: "Native Render Worker",
        useStaThread: true,
        maxOperationsPerSession: 500));

await worker.InitializeAsync(cancellationToken);

// 2. Throw work at it. Returns when the work item completes.
byte[] bytes = await worker.ExecuteAsync(
    (session, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        return session.Render("<h1>Hello</h1>");
    },
    new ExecutionRequestOptions(recycleSessionOnFailure: true),
    cancellationToken);

Console.WriteLine($"Produced {bytes.Length} bytes.");

public sealed class NativeSession
{
    public byte[] Render(string html) => Encoding.UTF8.GetBytes(html);
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
        // Free native handles here, on the owning worker thread.
    }
}
```

### Pooled `ValueTask` hot path

Both the `IExecutionWorker<TSession>` and `IExecutionWorkerPool<TSession>`
interfaces and the concrete `ExecutionWorker<TSession>` and
`ExecutionWorkerPool<TSession>` types expose instance `ExecuteValueAsync`
overloads backed by pooled `IValueTaskSource<T>` work items on every supported
TFM. Use the same synchronous delegate as with `ExecuteAsync`; only the returned
completion type changes.

```csharp
byte[] bytes = await worker.ExecuteValueAsync(
    (session, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        return session.Render("<h1>Hello</h1>");
    },
    cancellationToken: cancellationToken);
```

Await each returned ValueTask exactly once. If you need to share or repeatedly
await a result, use `ExecuteAsync`, or call `AsTask()` once and retain that Task.
Do not discard, double-await, or synchronously read an incomplete ValueTask.

Pooling avoids per-request source/Task allocations when an item can be reused;
startup, pool misses, delegates, channels, continuations, and tracing can still
allocate. The pooling API already existed in 1.0.0. The 2.0.0 improvement is safe
completion ownership and the same recovery behavior as the Task path.
See [ADR-0010](docs/adr/0010-worker-completion-and-lifecycle.md), which supersedes
the lifecycle design in [ADR-0007](docs/adr/0007-zero-alloc-value-task-source.md).

### Execution deadlines (`TimeSpan timeout`)

`ExecutionDispatcherExtensions` provides `ExecuteAsync` and `ExecuteValueAsync`
extension overloads with a `TimeSpan timeout` on `IExecutionDispatcher<TSession>`:

```csharp
byte[] bytes = await worker.ExecuteAsync(
    (session, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        return session.Render("<h1>Hello</h1>");
    },
    timeout: TimeSpan.FromSeconds(2),
    cancellationToken: cancellationToken);
```

The deadline bounds startup, queueing, and execution. Queued work that expires
before dequeue never runs; deadline expiry throws `TimeoutException`, while
caller cancellation throws `OperationCanceledException` with the caller's exact
`CancellationToken`. If a synchronous native call is already running when the
deadline fires, the caller returns promptly while the worker stays occupied
until the native call completes.

---

## 🏭🏭🏭🏭 Pool: multiple workers

If one worker is not enough, use `ExecutionWorkerPool<TSession>`. Great fit when:

- 📁 you have several native library copies in separate folders
- 📦 each worker should load its own isolated DLL set
- ♻️ one failed worker session should recycle without touching the others
- 🏎️ you want several dedicated threads, but still serialised execution *per* worker

```csharp
await using var pool = new ExecutionWorkerPool<NativePoolSession>(
    workerIndex => new NativePoolSessionFactory($@"c:\native\slot-{workerIndex + 1:D2}"),
    new ExecutionWorkerPoolOptions(
        workerCount: 4,
        name: "Native Pool",
        useStaThread: true,
        maxOperationsPerSession: 250));

await pool.InitializeAsync(cancellationToken);

var result = await pool.ExecuteAsync(
    (session, ct) => session.Render("<h1>Hello from pool</h1>"),
    new ExecutionRequestOptions(recycleSessionOnFailure: true),
    cancellationToken);
```

> 💡 **Tip:** need every worker to share the exact same factory? There's a single-factory constructor overload too: `new ExecutionWorkerPool<T>(factory, options)`. Nice and tidy for stateless factories. ([ADR-0008](docs/adr/0008-uniform-snapshot-surface.md))

---

## 🚦 Scheduling

The pool ships with two built-in schedulers and a public `IWorkerScheduler<TSession>` seam if you need something bespoke.

| Built-in | Icon | Semantics |
| --- | :-: | --- |
| `LeastQueuedWorkerScheduler<TSession>` *(default)* | ⚖️ | Picks the healthy worker with the smallest `QueueDepth`. Ties break via a shared rolling index so equal-depth workers rotate. Skips faulted workers. Early-exits when it finds a zero-depth worker. |
| `RoundRobinWorkerScheduler<TSession>` | 🔄 | Strict rotation across healthy workers via an `Interlocked` index. Skips faulted workers. |

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

Rationale, trade-offs, and the faulted-worker contract are captured in [ADR-0002](docs/adr/0002-pluggable-worker-scheduler.md).

---

## 🤔 When to use what

### 1️⃣ Choose `ExecutionWorker<TSession>` when

- 🔒 the native engine is effectively process-global
- 🥵 the library is known to be thread-sensitive
- ⛔ you want strict serialised access to one engine instance
- 👑 you want exactly one owner thread

### 4️⃣ Choose `ExecutionWorkerPool<TSession>` when

- 👤👤👤👤 you have isolated native copies per worker
- 🏎️🏎️ the library can run in parallel across separate worker-owned sessions
- 🚀 you want better throughput
- 🔧 you want one worker to recycle independently from the others

---

## ♻️ Session recycle story

You can choose to recycle the session:

- ❌ after a failed request — `ExecutionRequestOptions.RecycleSessionOnFailure = true`
- 💯 after a fixed number of operations — `ExecutionWorkerOptions.MaxOperationsPerSession > 0`
- ✨ or both

Set `maxOperationsPerSession: 0` when you want unlimited session lifetime and only failure-based recycling.

Task and ValueTask calls use the same failure and recycle policy. Completion
is published only after any required session teardown. A teardown error after
a successful delegate faults the request and worker. If the delegate also
failed, its exception remains the request failure and `Fault` records the
terminal teardown error. A canceled request does not count as a successful
operation or trigger failure-based recycling.

---

## 🧩 DI integration

```csharp
using AdaskoTheBeAsT.Interop.Execution;
using AdaskoTheBeAsT.Interop.Execution.DependencyInjection;

// Option A: class-based session factory
services.AddSingleton<IExecutionSessionFactory<NativeSession>, NativeSessionFactory>();
services.AddExecutionWorker<NativeSession>(options =>
{
    options.Name = "Native Render Worker";
    options.UseStaThread = true;
    options.MaxOperationsPerSession = 500;
});

// Option B: inline session delegate + IServiceProvider-aware options (validated on start)
services.AddExecutionWorkerPool<NativeSession>(
    (sp, ctx, ct) => new NativeSession(ctx.WorkerIndex),
    (sp, options) =>
    {
        var cfg = sp.GetRequiredService<IOptions<HostPoolSettings>>().Value;
        options.WorkerCount = cfg.WorkerCount;
        options.QueueCapacity = cfg.QueueCapacity;
    });

// Resolve IExecutionDispatcher<NativeSession>, IExecutionWorker<NativeSession>,
// or IExecutionWorkerPool<NativeSession> from DI and use it as usual.
```

Both `AddExecutionWorker<TSession>` and `AddExecutionWorkerPool<TSession>` automatically forward `IExecutionDispatcher<TSession>` via `TryAddSingleton` and wire `IValidateOptions<T>` + `.ValidateOnStart()` so invalid option values fail fast at host startup. `AddExecutionWorkerPool<TSession>` resolves `IContextualExecutionSessionFactory<TSession>` when registered (falling back to `IExecutionSessionFactory<TSession>`), resolves an optional `IWorkerScheduler<TSession>`, and also offers worker-indexed (`(sp, workerIndex) => ...`) and inline-delegate (`(sp, ctx, ct) => ...`) overloads.

---

## 🏗️ Generic host & health-check integration

```csharp
using AdaskoTheBeAsT.Interop.Execution.Hosting;

services.AddSingleton<IExecutionSessionFactory<NativeSession>, NativeSessionFactory>();
services.AddExecutionWorkerHostedService<NativeSession>(options =>
{
    options.Name = "Native Render Worker";
    options.UseStaThread = true;
});

services.AddHealthChecks()
    .AddExecutionWorkerHealthCheck<NativeSession>(
        configure: health => health.QueueDepthWarningThreshold = 16,
        tags: ["ready"]);
```

The registration includes the worker, `IExecutionDispatcher<TSession>` forwarding,
and its `IHostedService` wrapper; you do not need a separate
`AddExecutionWorker<TSession>()` call. Both DI and Hosting also support inline
`createSession` delegates (`DelegateExecutionSessionFactory<TSession>`) and
`Action<IServiceProvider, ...>` option callbacks with `.ValidateOnStart()`. The
wrapper drives `InitializeAsync` on `StartAsync` and joins `DisposeAsync` on
`StopAsync`.
In 2.0.0+, a canceled stop token cancels only that wait, not worker cleanup.
Repeated stops join the same teardown, subject to each caller's token.
`AddExecutionWorkerPoolHostedService<TSession>` and
`AddExecutionWorkerPoolHealthCheck<TSession>` cover the pool (reporting
`Unhealthy` before eager initialization completes, after disposal, or when all
workers fault, and `Degraded` on partial worker loss or elevated queue depth).

---

## 🌐 ASP.NET Core Minimal API integration

```csharp
using AdaskoTheBeAsT.Interop.Execution.AspNetCore;
using AdaskoTheBeAsT.Interop.Execution.Hosting;

builder.Services.AddExecutionAspNetCoreJson();
builder.Services.AddExecutionWorkerPoolHostedService<NativeSession>(
    (sp, ctx, ct) => new NativeSession(ctx.WorkerIndex),
    (sp, options) =>
    {
        options.WorkerCount = 4;
        options.QueueCapacity = 32;
    });
builder.Services.AddHealthChecks()
    .AddExecutionWorkerPoolHealthCheck<NativeSession>(tags: ["ready"]);

var app = builder.Build();
app.MapExecutionHealthProbes(); // /healthz + /readyz
app.MapExecutionWorkerPoolDiagnostics<NativeSession>(); // AOT-safe /api/diagnostics

app.MapPost("/api/calculate", async (
        CalcRequest req,
        IExecutionDispatcher<NativeSession> dispatcher,
        CancellationToken ct) =>
    TypedResults.Ok(await dispatcher.ExecuteValueAsync(
        (session, token) => session.Calculate(req, token),
        TimeSpan.FromSeconds(5),
        ct)))
    .WithExecutionExceptionHandling("Native engine unavailable.");
```

`WithExecutionExceptionHandling` (available on both `RouteHandlerBuilder` and
`RouteGroupBuilder`, alongside the optional global
`builder.Services.AddExecutionExceptionHandler(...)` + `app.UseExceptionHandler()`
pipeline) maps:

- `ArgumentException` -> `400 Bad Request` `ProblemDetails` using
  `ExecutionAspNetCoreOptions.BadRequestTitle` (never echoing internal native
  paths or stack traces).
- `OperationCanceledException` when `HttpContext.RequestAborted` is canceled ->
  `499 Client Closed Request`.
- `TimeoutException`, `ExecutionAdmissionRejectedException`, or
  `ObjectDisposedException` -> `503 Service Unavailable` `ProblemDetails` using
  `ExecutionAspNetCoreOptions.UnavailableTitle` and an optional ceiling-rounded
  `Retry-After` header (`ExecutionAspNetCoreOptions.RetryAfter`).

`MapExecutionWorkerDiagnostics<TSession>` and
`MapExecutionWorkerPoolDiagnostics<TSession>` project snapshots through
source-generated DTOs (`ExecutionWorkerDiagnosticsDto` /
`ExecutionWorkerPoolDiagnosticsDto` backed by
`ExecutionAspNetCoreJsonSerializerContext`, exposing `faultType` instead of raw
`System.Exception`) so `/api/diagnostics` remains Native-AOT-safe and leak-free
even when workers fault. `MapExecutionHealthProbes` wires `/healthz` (zero-check
liveness) and `/readyz` (`"ready"`-tagged readiness) in a single call.

---

## 🔭 Observability

Every worker emits to an `ActivitySource` and `Meter` named `AdaskoTheBeAsT.Interop.Execution` (customisable per worker via `ExecutionWorkerOptions.Diagnostics` — see [ADR-0009](docs/adr/0009-scoped-execution-diagnostics.md) for scoped emitters).

| Instrument | Kind | Tags |
| --- | --- | --- |
| `ExecutionWorker.Execute` | 📑 `Activity` (span) | `worker.name` |
| `execution.worker.operations` | 📈 `Counter<long>` | `worker.name`, `outcome` ∈ `success` / `faulted` / `cancelled` |
| `execution.worker.session_recycles` | 📈 `Counter<long>` | `worker.name`, `reason` ∈ `max_operations` / `failure` |
| `execution.worker.queue_depth` | 📉 `ObservableGauge<int>` | `worker.name` |

All these identifiers are exposed as `public const string` on `ExecutionDiagnosticNames`, so telemetry pipelines can subscribe without hard-coding strings:

```csharp
using AdaskoTheBeAsT.Interop.Execution;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(ExecutionDiagnosticNames.SourceName))
    .WithMetrics(m => m.AddMeter(ExecutionDiagnosticNames.SourceName));
```

When no activity listener is attached, `StartActivity` returns `null` and no
execution span is created. This does not guarantee allocation-free execution.

See [ADR-0003](docs/adr/0003-public-diagnostic-constants.md) for why the identifiers are a public contract.

---

## ⚠️ Faulting semantics

`ExecutionWorker<TSession>` is **terminal-once**: a session creation or teardown
failure latches `Fault` before `WorkerFaulted` is queued to the thread pool.
Subscribers run off the owning thread; throwing subscribers are contained.
Notification is asynchronous and may arrive after disposal completes. The first
terminal exception wins, and the worker cannot be re-initialised.

A delegate exception alone faults that request, optionally recycling its session.
An `OperationCanceledException` is cancellation only when the request token was
canceled; otherwise it is a failure for both Task and ValueTask calls. Future
submissions rethrow a terminal fault synchronously unless disposal has already
been requested, in which case they throw `ObjectDisposedException`.

Disposal joins teardown without rethrowing terminal session errors. Inspect
`Fault`, `IsFaulted`, or `WorkerFaulted` for those errors. Diagnostic-listener
exceptions do not fail requests; execution spans use the submitting activity
as their parent.

Pool consumers observe the same contract aggregated: `IsAnyFaulted`, `WorkerFaults`, and a forwarded `WorkerFaulted` event carrying the originating worker name. 🔔

---

## 🔁 Migrating

- **From 2.x to 3.0.0:** see the [3.0.0 migration guide](docs/migration-3.0.0.md). Retarget any `net8.0` or `net9.0` projects to `net10.0` or `net11.0`; on `net11.0`, `net10.0`, `net481`, `net48`, and `net472`, existing `2.2.0` APIs remain source- and binary-compatible.
- **From 1.0.x to 2.0.0:** see the [2.0.0 migration guide](docs/migration-2.0.0.md). It covers retargeting from `.NET Framework < 4.7.2`, before/after completion and fault ordering, option snapshots, `QueueCapacity`, and `ShutdownMode`.

---

## 🧪 Build and test

```powershell
dotnet build .\AdaskoTheBeAsT.Interop.slnx
dotnet test --solution .\AdaskoTheBeAsT.Interop.slnx --no-build
```

| Project | Role |
| --- | --- |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.Test` | 🔬 Unit + behavioural (fault propagation, dispose idempotency, cancellation, deadlines, contextual & delegate factories, `ExecutionAdmissionRejectedException`, `BeforeThreadStart`, `MaxStackSize`, `OnWorkerThreadStart`, `CreateWorkerThreadScope`, telemetry smoke, scheduler contract). |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test` | 🧩 DI registration, `IExecutionDispatcher<TSession>` forwarding, `IServiceProvider` options + `ValidateOnStart`, contextual/indexed/delegate factory resolution. |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.Hosting.Test` | 🏗️ `IHostedService` start/stop lifecycle, idempotent registration/shutdown, startup option validation, worker/pool `IHealthCheck` transitions. |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.AspNetCore.Test` | 🌐 Minimal API `TestServer` filter/exception-handler status mappings (`200`/`400`/`499`/`503`), strict source-generated `/api/diagnostics` under faults, and `/healthz` + `/readyz` probes (`net11.0;net10.0`). |
| `test/integ/AdaskoTheBeAsT.Interop.Execution.IntegrationTest` | 🤝 Multi-threaded submission, STA on Windows, reentrant dispose, multi-pool contextual session recycling, zero-alloc `ValueTask`, snapshot surface, scoped diagnostics. |

The four core test projects target `net11.0`, `net10.0`, `net481`, `net48`, and `net472`, and `Execution.AspNetCore.Test` targets `net11.0` and `net10.0`.

---

## 📜 Architecture Decision Records

Small, self-contained design decisions taken on this codebase live under [`docs/adr/`](docs/adr/). Start with the [index](docs/adr/README.md). Highlights:

- 🧭 [ADR-0002 — pluggable worker scheduler](docs/adr/0002-pluggable-worker-scheduler.md)
- 🏷️ [ADR-0003 — public diagnostic constants](docs/adr/0003-public-diagnostic-constants.md)
- ⚡ [ADR-0007 — zero-allocation `ExecuteValueAsync`](docs/adr/0007-zero-alloc-value-task-source.md)
- 📸 [ADR-0008 — uniform snapshot surface](docs/adr/0008-uniform-snapshot-surface.md)
- 🔭 [ADR-0009 — scoped `ExecutionDiagnostics`](docs/adr/0009-scoped-execution-diagnostics.md)
- [ADR-0010: 2.0.0 completion ownership and lifecycle](docs/adr/0010-worker-completion-and-lifecycle.md)
- 🎯 [ADR-0011 — .NET 11 target framework matrix and 3.0.0 baseline](docs/adr/0011-net11-target-framework-matrix.md)
- 🪪 [ADR-0012 — contextual session lifecycle and indexed DI/Hosting](docs/adr/0012-contextual-session-lifecycle.md)
- 🚦 [ADR-0013 — `BeforeThreadStart` startup ordering hook](docs/adr/0013-before-thread-start-hook.md)
- ⏱️ [ADR-0014 — execution deadline extension overloads](docs/adr/0014-execution-deadline-extensions.md)
- 🩺 [ADR-0015 — Hosting `IHealthCheck` and readiness snapshots](docs/adr/0015-hosting-health-checks-and-readiness-snapshots.md)
- 🚫 [ADR-0016 — typed `ExecutionAdmissionRejectedException`](docs/adr/0016-typed-admission-rejected-exception.md)
- 🔌 [ADR-0017 — `IExecutionDispatcher` forwarding and validated DI options](docs/adr/0017-dispatcher-forwarding-and-validated-di-options.md)
- 🧩 [ADR-0018 — `DelegateExecutionSessionFactory` and inline delegates](docs/adr/0018-delegate-execution-session-factory.md)
- 🌐 [ADR-0019 — `Execution.AspNetCore` and AOT-safe diagnostics](docs/adr/0019-aspnetcore-execution-integration-and-safe-diagnostics.md)
- 🧵 [ADR-0020 — worker `MaxStackSize` and thread-affine scopes](docs/adr/0020-worker-thread-stack-size-and-thread-affine-scopes.md)

---

## 🙋 Contributing

Found a bug? Got an idea? Spotted a typo that's been haunting you? 👻

1. 🐙 Open an issue describing the problem or the proposal.
2. 🛠️ Fork + branch (`feature/your-idea`).
3. ✅ Run `dotnet build` + `dotnet test` across the full matrix.
4. ✨ Add/update tests and an ADR if the change is load-bearing.
5. 🚀 Open a PR — the strict-build + CI will do the rest.

---

## 📚 Further reading

- 📄 [`wkhtml.md`](./wkhtml.md) — WkHtml migration notes.
- 📁 [`docs/adr/`](docs/adr/) — design rationale for every recent change.
- 📝 [`CHANGELOG.md`](./CHANGELOG.md) — what landed when.
- [2.x to 3.0.0 migration guide](docs/migration-3.0.0.md): `.NET 11` retargeting and `3.0.0` feature adoption.
- [1.0.x to 2.0.0 migration guide](docs/migration-2.0.0.md): retargeting, upgrade steps, and behavioral changes.

---

<p align="center">
  Built for the kind of interop code that likes <strong>one owner thread</strong>, <strong>explicit lifecycle</strong>, and <strong>zero drama</strong>. ✨<br/>
  Made with ❤️ (and a lot of coffee ☕) by <a href="https://github.com/AdaskoTheBeAsT">AdaskoTheBeAsT</a>.
</p>
