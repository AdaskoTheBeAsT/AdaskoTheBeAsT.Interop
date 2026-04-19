# AdaskoTheBeAsT.Interop

> A focused interop toolbox for dedicated-thread execution, native library isolation, and COM-friendly workloads.

[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Execution.svg?label=AdaskoTheBeAsT.Interop.Execution&logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution/)
[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Execution.DependencyInjection.svg?label=Execution.DependencyInjection&logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.DependencyInjection/)
[![NuGet](https://img.shields.io/nuget/v/AdaskoTheBeAsT.Interop.Execution.Hosting.svg?label=Execution.Hosting&logo=nuget)](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.Hosting/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
![TFMs](https://img.shields.io/badge/TFMs-net10.0%20%7C%20net9.0%20%7C%20net8.0%20%7C%20net4.6.2%E2%80%93net4.8.1-512BD4?logo=dotnet)
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

## ✨ Why you'll love this

- ⚡ **Zero-allocation hot path.** `ExecuteValueAsync` is backed by pooled `IValueTaskSource<T>` on *every* TFM (yes, even `net462`). No `Task` wrapping on the hot path. ([ADR-0007](docs/adr/0007-zero-alloc-value-task-source.md))
- 🧩 **Drop-in DI + Hosting.** `services.AddExecutionWorker<TSession>()` + `AddExecutionWorkerHostedService<TSession>()` and you're done.
- 💫 **Pluggable schedulers.** `LeastQueued` and `RoundRobin` ship in the box; bring your own via `IWorkerScheduler<TSession>`. ([ADR-0002](docs/adr/0002-pluggable-worker-scheduler.md))
- 🔭 **Batteries-included observability.** `ActivitySource` + `Meter` with public constant names, ready for OpenTelemetry. ([ADR-0003](docs/adr/0003-public-diagnostic-constants.md))
- 🪟 **First-class Windows STA.** Flip a boolean, get an STA worker thread on Windows; silently ignored elsewhere.
- ♻️ **Real session recycling.** After N operations, after a failure, or both — your call.
- 🛡️ **Terminal-once faulting.** When a worker goes bad, it says so *once*, loudly, via `WorkerFaulted` — no silent-dead-thread surprises.
- 🖥️ **9 TFMs, all green.** `net10.0`, `net9.0`, `net8.0`, `net481`, `net48`, `net472`, `net471`, `net47`, `net462` — tested across the full matrix on every build.
- ✏️ **Source Link + snupkg.** Step into the library from your debugger without guessing.

---

## 📦 Packages

| Package | What it gives you |
| --- | --- |
| [`AdaskoTheBeAsT.Interop.Execution`](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution/) | ⚓ Core: `ExecutionWorker<TSession>`, `ExecutionWorkerPool<TSession>`, `IExecutionSessionFactory<TSession>`, options, schedulers, diagnostics. |
| [`AdaskoTheBeAsT.Interop.Execution.DependencyInjection`](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.DependencyInjection/) | 🧩 `Microsoft.Extensions.DependencyInjection` helpers: `AddExecutionWorker<TSession>()` / `AddExecutionWorkerPool<TSession>()` with `IOptions<T>` binding. |
| [`AdaskoTheBeAsT.Interop.Execution.Hosting`](https://www.nuget.org/packages/AdaskoTheBeAsT.Interop.Execution.Hosting/) | 🏗️ `Microsoft.Extensions.Hosting` integration: `IHostedService` wrappers driving worker / pool lifetime from the generic host. |

### ⬇️ Install

```powershell
dotnet add package AdaskoTheBeAsT.Interop.Execution
dotnet add package AdaskoTheBeAsT.Interop.Execution.DependencyInjection
dotnet add package AdaskoTheBeAsT.Interop.Execution.Hosting
```

Symbols ship as `.snupkg` with Source Link and embedded untracked sources. Step in. Look around. It's fine.

---

## 🗺️ Target framework matrix

| TFM | Status | Notes |
| --- | :-: | --- |
| `net10.0` | ✅ | Primary target; in-box `System.Threading.Channels` + `System.Diagnostics.DiagnosticSource`. |
| `net9.0` | ✅ | Primary target. |
| `net8.0` | ✅ | Primary target. |
| `net481` | ✅ | Windows desktop; `System.Threading.Channels` + `System.Diagnostics.DiagnosticSource` via NuGet + `IsExternalInit` polyfill. |
| `net48` | ✅ | Same as above. |
| `net472` | ✅ | Same as above. |
| `net471` | ✅ | Same as above. |
| `net47` | ✅ | Same as above. |
| `net462` | ✅ | Same as above. |

Every cell is built with `TreatWarningsAsErrors=true`, `ContinuousIntegrationBuild=true`, `Deterministic=true`, and exercised in CI.

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
- cooperative cancellation of pending items on shutdown
- session reuse + session recycle after failure or after N operations
- observability via `Name`, `IsFaulted`, `Fault`, `QueueDepth`, `WorkerFaulted`, and the uniform `GetSnapshot()`

### ⚙️⚙️⚙️⚙️ `ExecutionWorkerPool<TSession>`

Fan-out pool of `ExecutionWorker<TSession>` instances. Each pool worker owns a private session and a private queue; a pluggable `IWorkerScheduler<TSession>` picks which worker receives each submission.

Owns 👇

- multiple `ExecutionWorker<TSession>` instances
- pluggable work distribution (see [Scheduling](#-scheduling) below)
- one session per worker (ideal for isolated native DLL sets)
- per-worker isolation for native state
- parallel initialization and parallel disposal
- aggregate observability (`QueueDepth`, `IsAnyFaulted`, `WorkerFaults`, `Workers`, forwarded `WorkerFaulted`, uniform `GetSnapshot()`)

### 🏭 `IExecutionSessionFactory<TSession>`

```csharp
public interface IExecutionSessionFactory<TSession>
    where TSession : class
{
    TSession CreateSession(CancellationToken cancellationToken);
    void DisposeSession(TSession session);
}
```

Creates the thread-affine session (loading native libs, initialising modules) and disposes / unloads it. Both methods run on the dedicated worker thread.

### 🎛️ `ExecutionWorkerOptions`

`Name`, `UseStaThread`, `MaxOperationsPerSession` (`0` = unlimited), `DisposeTimeout` (default `Timeout.InfiniteTimeSpan`), `Diagnostics` (scoped `ExecutionDiagnostics` instance — defaults to a process-wide `Shared` singleton). Parameterless ctor + positional ctor + public setters so it binds cleanly via `IOptions<T>`.

### 🎛️ `ExecutionWorkerPoolOptions`

`WorkerCount`, `Name`, `UseStaThread`, `MaxOperationsPerSession`, `DisposeTimeout`, `SchedulingStrategy` (default `LeastQueued`), `Diagnostics`. Same binding story.

### 🎛️ `ExecutionRequestOptions`

Per-call knob: `RecycleSessionOnFailure` (default `false`).

---

## 🪟 STA behavior

If `UseStaThread: true` is set:

- ✅ On Windows, the worker thread is configured as `STA` via `SetApartmentState(ApartmentState.STA)` (guarded by `OperatingSystem.IsWindows()` on `net5+` and `PlatformID.Win32NT` on older TFMs).
- 🤷 On non-Windows, the flag is silently ignored.

That makes the option safe for cross-platform callers that want "STA when possible" behaviour.

---

## 🚀 Quick example

```csharp
using AdaskoTheBeAsT.Interop.Execution;

public sealed class NativeSession
{
    public byte[] Render(string html) => []; // call into your native lib here
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
        // free native handles here
    }
}

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
    (session, ct) => session.Render("<h1>Hello</h1>"),
    new ExecutionRequestOptions(recycleSessionOnFailure: true),
    cancellationToken);
```

### ⚡ Zero-alloc `ValueTask` hot path

When the caller is already in `ValueTask` land (e.g. wrapping a native-interop async API) the **instance** `ExecuteValueAsync` overloads keep you in that domain with pooled `IValueTaskSource<T>` work items — **no inner `Task` allocation, on every TFM**:

```csharp
int sessionId = await worker.ExecuteValueAsync(
    (session, _) => session.SessionId,
    cancellationToken: cancellationToken);
```

See [ADR-0007](docs/adr/0007-zero-alloc-value-task-source.md) for the gritty details; [ADR-0004](docs/adr/0004-valuetask-hotpath-overloads.md) is kept as historical context.

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

---

## 🧩 DI integration

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

`AddExecutionWorkerPool<TSession>` is the pool-flavoured equivalent and binds `IOptions<ExecutionWorkerPoolOptions>`.

---

## 🏗️ Generic host integration

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

When no listener is attached, `StartActivity` returns `null` and the instrumentation is allocation-free. ⚡

See [ADR-0003](docs/adr/0003-public-diagnostic-constants.md) for why the identifiers are a public contract.

---

## ⚠️ Faulting semantics

`ExecutionWorker<TSession>` is **terminal-once**: when a work item throws a non-cancellation exception during startup or session creation — or when `DisposeSession` throws during shutdown — `_fatalFailure` latches, `IsFaulted` flips to `true`, and `WorkerFaulted` fires *exactly once*. Subsequent `ExecuteAsync` calls rethrow the original exception synchronously (unwrapped via `ExceptionDispatchInfo`). The worker cannot be re-initialised after faulting.

Pool consumers observe the same contract aggregated: `IsAnyFaulted`, `WorkerFaults`, and a forwarded `WorkerFaulted` event carrying the originating worker name. 🔔

---

## 🧪 Build and test

```powershell
dotnet build  .\AdaskoTheBeAsT.Interop.slnx
dotnet test   .\AdaskoTheBeAsT.Interop.slnx --no-build
```

| Project | Role |
| --- | --- |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.Test` | 🔬 Unit + behavioural (fault propagation, dispose idempotency, cancellation, telemetry smoke, scheduler contract). |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.DependencyInjection.Test` | 🧩 DI registration, options binding, lifetime. |
| `test/unit/AdaskoTheBeAsT.Interop.Execution.Hosting.Test` | 🏗️ `IHostedService` start/stop lifecycle, idempotent shutdown. |
| `test/integ/AdaskoTheBeAsT.Interop.Execution.IntegrationTest` | 🤝 Multi-threaded submission, STA on Windows, reentrant dispose, session recycling, zero-alloc `ValueTask`, snapshot surface, scoped diagnostics. |

All four projects run across the full 9-target matrix.

---

## 📜 Architecture Decision Records

Small, self-contained design decisions taken on this codebase live under [`docs/adr/`](docs/adr/). Start with the [index](docs/adr/README.md). Highlights:

- 🧭 [ADR-0002 — pluggable worker scheduler](docs/adr/0002-pluggable-worker-scheduler.md)
- 🏷️ [ADR-0003 — public diagnostic constants](docs/adr/0003-public-diagnostic-constants.md)
- ⚡ [ADR-0007 — zero-allocation `ExecuteValueAsync`](docs/adr/0007-zero-alloc-value-task-source.md)
- 📸 [ADR-0008 — uniform snapshot surface](docs/adr/0008-uniform-snapshot-surface.md)
- 🔭 [ADR-0009 — scoped `ExecutionDiagnostics`](docs/adr/0009-scoped-execution-diagnostics.md)

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

---

<p align="center">
  Built for the kind of interop code that likes <strong>one owner thread</strong>, <strong>explicit lifecycle</strong>, and <strong>zero drama</strong>. ✨<br/>
  Made with ❤️ (and a lot of coffee ☕) by <a href="https://github.com/AdaskoTheBeAsT">AdaskoTheBeAsT</a>.
</p>
