# Migrating from 1.0.x to 2.0.0

**Status: 2.0.0 is unreleased.** This guide compares the current implementation
with the tagged `v1.0.0` release.

**2.0.0 is a breaking release:** .NET Framework versions below 4.7.2 are no
longer supported. The removed targets are `net462`, `net47`, and `net471`.
This support change is the reason for the major version.

On retained targets, existing public signatures remain available. Callers can
keep their session factories, delegates, worker/pool constructors, and
DI/Hosting registration, but must review the behavioral corrections. The
reason to upgrade is more reliable completion, failure recovery, startup, and
shutdown, not a new native execution model.

- [Why upgrade?](../README.md#why-upgrade-to-200)
- [Release details](../CHANGELOG.md)
- [Lifecycle design and rationale](adr/0010-worker-completion-and-lifecycle.md)

## 1. Check and retarget your application

All three 2.0.0 packages support the same six targets:

| Application target | 2.0.0 support | Upgrade action |
| --- | --- | --- |
| `net462`, `net47`, `net471` (.NET Framework 4.6.2, 4.7, 4.7.1) | Removed | Retarget to .NET Framework 4.7.2 or later, or remain on 1.0.0. |
| `net472`, `net48`, `net481` (.NET Framework 4.7.2, 4.8, 4.8.1) | Retained | Update packages; no framework change required. |
| `net8.0`, `net9.0`, `net10.0` (.NET 8, 9, 10) | Retained | Update packages; no framework change required. |

For an SDK-style project, change an older `<TargetFramework>` value to
`net472` or a later supported target. For a multi-targeted project, update
`<TargetFrameworks>` and remove or replace the unsupported entries. For a
legacy .NET Framework project, set `<TargetFrameworkVersion>` to `v4.7.2` or
later.

Retarget affected applications, adapters, and test projects before restoring
2.0.0. Install the matching developer/targeting pack on build machines and
ensure deployment machines have a compatible .NET Framework runtime.
Installing a newer runtime alone does not change a project's compile target.
If you must keep an older target, keep its package references on 1.0.0 instead.

## 2. Update the packages you use

Once 2.0.0 is published to your package feed, update your direct references
together. Run these commands in your application project directory; omit
companion packages you do not use.

```powershell
dotnet add package AdaskoTheBeAsT.Interop.Execution --version 2.0.0
dotnet add package AdaskoTheBeAsT.Interop.Execution.DependencyInjection --version 2.0.0
dotnet add package AdaskoTheBeAsT.Interop.Execution.Hosting --version 2.0.0
```

For Central Package Management, update the corresponding entries in
`Directory.Packages.props` instead. Restore and rebuild your application,
including its adapters and tests, against the updated dependency graph.

Pooled ValueTask overloads, snapshots, shared-factory pool constructors, scoped
diagnostics, and DI option isolation already existed in tagged 1.0.0. No
migration away from an extension-method ValueTask API is needed for that release.

## 3. Review the behavioral changes

Even on retained targets, unchanged API signatures do not imply identical
timing or failure behavior. These corrections apply even if you leave the new
options at their defaults.

| Area | 1.0.0 behavior or risk | 2.0.0 contract | What to change or verify |
| --- | --- | --- | --- |
| Request completion | Completion could precede recycle teardown; pooled sources could be reused while the worker still accessed them. | Completion follows required teardown and worker bookkeeping. Publication is the worker's last access to the item. | Measure completion latency including cleanup. Do not assume delegate return alone means the request succeeded. |
| Pooled failure recovery | ValueTask delegate exceptions bypassed worker recovery and outcome reporting. | Task and ValueTask requests use the same failure/recycle policy and telemetry outcomes. | Expect `RecycleSessionOnFailure` to run for pooled calls too; test the next session after a failure. |
| Session lifecycle errors | A successful result could hide failed cleanup; replacement creation was not consistently terminal. | Creation/teardown failures terminate the worker. Failed cleanup after a successful delegate faults the request. | Check both the request exception and worker `Fault`. Replace a terminal worker; do not reinitialize it. |
| Fault events | Handlers ran synchronously on the worker before fault state was published. | The first `Fault` is latched before a single thread-pool notification. Throwing handlers are contained. | Make handlers thread-safe and independent of the session-owning thread. Do not assume they finish before disposal or an `IsFaulted` read. |
| Repeated disposal | A later or reentrant disposal could return early while teardown continued. | Every external async disposer joins actual teardown. Reentrant calls only request shutdown. | Expect every external await to wait for slow cleanup, even after synchronous disposal timed out. |
| Cold submission | An async call could synchronously wait for initial session creation. | It queues without waiting for creation, with failures delivered through returned requests. | Put the submission **and its await** inside error handling. Use `InitializeAsync` explicitly if startup readiness is required. |
| Startup cancellation | Pool initialization cancellation could trigger cleanup of shared workers. | An initialization token cancels that caller's wait, not shared creation. | Dispose the worker/pool separately when abandoning ownership, rather than assuming a canceled wait stopped it. |
| Request cancellation | Task and ValueTask could classify an unrequested `OperationCanceledException` differently. | Without requested cancellation on the request token, it is a fault. | Do not throw `OperationCanceledException` for ordinary failures. Filter cancellation handling on the request token. |
| Options | Mutating the supplied object could change a live worker. | Worker/pool options are copied at construction and validated. | Finish configuration before construction/resolution; replace an instance to reconfigure it. |
| Custom schedulers | A foreign concrete worker could be accepted. | The scheduler receives a read-only list and must return an actual member. | Select from the supplied list. Do not return a wrapper, a separately created worker, or `null`. |
| Hosted shutdown | `StopAsync` could wait beyond its cancellation deadline. | A deadline cancels the stop wait, not cleanup. | Handle a canceled stop task; do not treat it as confirmation that native resources are released. |
| Telemetry | Listener exceptions could disrupt requests; spans could inherit startup context. | Listeners are best effort; spans use the submitting activity context. | Update tracing assertions and remove any dependency on listener exceptions reaching the caller. |

### Keep request failure separate from worker failure

A delegate failure normally faults only that request. Recycling after failure
remains opt-in through `ExecutionRequestOptions.RecycleSessionOnFailure`.

If the delegate fails **and** recycle teardown fails, the request preserves the
delegate exception; `worker.Fault` records the terminal teardown exception.
Final shutdown teardown failures are also recorded in `Fault`, but are not
rethrown by `DisposeAsync`. Pools expose `IsAnyFaulted` and `WorkerFaults`.

Notification is asynchronous and can arrive after disposal completes. A health
check should read `IsFaulted`/`Fault` (or the pool equivalents), not wait for an
event handler to update a second flag.

### Await each pooled ValueTask exactly once

The producer/consumer race is fixed, but the standard ValueTask usage rules
still apply. These alternative snippets assume an existing concrete
`ExecutionWorker<NativeSession>` and a synchronous `session.Render(string)`.
Pooled overloads belong to the concrete worker/pool types, not their interfaces.
The interfaces registered by DI expose `ExecuteAsync`; keep using that method
unless your adapter deliberately works with the concrete implementation.

```csharp
// One consumer: await the returned ValueTask once.
byte[] bytes = await worker.ExecuteValueAsync(
    (session, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        return session.Render("<h1>Hello</h1>");
    },
    cancellationToken: cancellationToken);
```

```csharp
// Multiple consumers: convert once and share the Task, not the ValueTask.
Task<byte[]> sharedResult = worker.ExecuteValueAsync(
    (session, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        return session.Render("<h1>Hello</h1>");
    },
    cancellationToken: cancellationToken).AsTask();

byte[] first = await sharedResult;
byte[] second = await sharedResult;
```

Using `ExecuteAsync` directly is also appropriate when you need a Task. Do not
discard a returned ValueTask, call `AsTask()` twice on it, or read its result
while incomplete. Pooling avoids source/Task allocations when work items can
be reused; it does not promise zero allocations for the entire operation.

## 4. Configure before construction; opt into limits deliberately

The new defaults are `QueueCapacity = 0` (unlimited waiting work) and
`ShutdownMode = ExecutionShutdownMode.Drain`. You do not need to change
registration code to receive the correctness fixes.

Use object properties or a DI configuration callback for the new options.
They are not new positional constructor arguments. For example, with the
factory from the [README example](../README.md):

```csharp
using AdaskoTheBeAsT.Interop.Execution;

var options = new ExecutionWorkerOptions
{
    Name = "Native Render Worker",
    UseStaThread = true,
    MaxOperationsPerSession = 500,
    QueueCapacity = 100, // Waiting requests, excluding the active request.
    ShutdownMode = ExecutionShutdownMode.Drain,
    DisposeTimeout = TimeSpan.FromSeconds(30), // Synchronous Dispose wait only.
};

await using var worker = new ExecutionWorker<NativeSession>(
    new NativeSessionFactory(), options);

await worker.InitializeAsync(cancellationToken);

// Changing options here would NOT reconfigure worker.
```

The same settings are available through Hosting; this registration also adds
the worker, so do not register it a second time:

```csharp
using AdaskoTheBeAsT.Interop.Execution;
using AdaskoTheBeAsT.Interop.Execution.Hosting;
using Microsoft.Extensions.DependencyInjection;

services.AddSingleton<IExecutionSessionFactory<NativeSession>, NativeSessionFactory>();
services.AddExecutionWorkerHostedService<NativeSession>(options =>
{
    options.Name = "Native Render Worker";
    options.QueueCapacity = 100;
    options.ShutdownMode = ExecutionShutdownMode.CancelPending;
});
```

Use plain `AddExecutionWorker<TSession>` when you manage lifecycle yourself,
or `AddExecutionWorkerPoolHostedService<TSession>` for a hosted pool.

### Queue capacity is rejection, not an asynchronous wait for space

- A full queue returns a faulted Task/ValueTask with
  `InvalidOperationException`. Observe it by awaiting the result. The worker
  does not block admission or silently drop the request.
- Capacity includes requests waiting for initial startup. Canceled queued
  requests occupy a slot until dequeued; cancellation does not remove them
  immediately.
- The active request does not consume a waiting slot. A capacity of 100 allows
  up to 100 waiting requests plus one executing request per worker.
- Pool capacity is **per worker**. A scheduler can select a full worker even
  when another has room; the pool does not automatically retry elsewhere.
- Decide how your application reports overload or retries it. Do not blindly
  retry every `InvalidOperationException`: delegates and terminal worker faults
  can use that exception type too.

### Validation is stricter

`QueueCapacity` must be nonnegative and `ShutdownMode` must be a defined mode.
`DisposeTimeout` must be `Timeout.InfiniteTimeSpan` or between zero and
`TimeSpan.FromMilliseconds(int.MaxValue)` (about 24.9 days). Oversized values
now fail validation at configuration/construction time rather than reaching an
invalid wait during shutdown. Do not use `TimeSpan.MaxValue` for infinity.

## 5. Decide what shutdown completion means

| Mechanism | What it guarantees | What it does not guarantee |
| --- | --- | --- |
| `Drain` | Admitted work is processed before final teardown when a usable session is available, subject to request cancellation and terminal faults. | Queued work cannot run if initial creation is canceled/fails or the worker terminates. |
| `CancelPending` | Requests not yet started are skipped and completed as canceled when processed during shutdown. | The active delegate is not interrupted, and canceled requests need not complete immediately. |
| External `DisposeAsync()` | Completion joins actual session teardown for the worker, or all workers in a pool. Repeated calls join the same exit. | Terminal session errors are not rethrown; inspect fault properties. |
| Reentrant disposal from an owning worker | Requests shutdown without waiting on itself. | Its early return does not mean teardown has finished. An external caller must join it. |
| `DisposeTimeout` | Bounds only synchronous `Dispose()`'s wait. | A timeout does not stop cleanup or establish that resources have been released. |
| Hosted `StopAsync(token)` | Joins cleanup until completion or the host deadline; cancellation of an unfinished wait throws `OperationCanceledException`. | Canceling the wait does not cancel cleanup. Later container disposal can still block. |

If synchronous disposal is unavoidable, a later asynchronous call can confirm
teardown. This example assumes a worker configured with a finite
`DisposeTimeout`, and must run outside its owning thread:

```csharp
worker.Dispose(); // May return because the synchronous wait timed out.
await worker.DisposeAsync(); // Still joins actual teardown.

if (worker.Fault is Exception failure)
{
    Console.Error.WriteLine(failure);
}
```

Disposal requests cancellation of initial session creation separately from
request tokens. Running delegates must observe their request token
cooperatively; neither shutdown mode forcibly terminates managed or native
code. An `InitializeAsync` timeout also does not stop shared startup.

**A hard deadline for a hung native call requires process isolation.** This
release does not provide a process supervisor, a COM/UI message pump, or
automatic isolation of process-global native state. Keep delegates synchronous,
and never synchronously wait for nested work on the same worker.

## 6. Validate your application's upgrade

- [ ] Retarget any `net462`, `net47`, or `net471` projects to .NET Framework
      4.7.2 or later, including adapters and tests. Check build targeting packs
      and deployed runtimes, or keep affected projects on 1.0.0.
- [ ] Restore/build each supported application target with aligned 2.0.0
      package references.
- [ ] Test both Task and ValueTask failure paths, including opted-in recycling
      and a factory that fails during replacement creation or teardown.
- [ ] Verify request completion is observed only after required cleanup, and
      inspect worker/pool fault properties independently of notification timing.
- [ ] Submit before initialization completes; verify startup failure handling,
      FIFO behavior, and canceling one initialization wait without stopping others.
- [ ] Test repeated external disposal, reentrant shutdown, and a synchronous
      timeout followed by an async join.
- [ ] If using Hosting, test a stop deadline both before and during cleanup.
- [ ] If enabling capacity or `CancelPending`, test overload and canceled pending
      requests. Confirm retries do not duplicate native side effects.
- [ ] Check option values before worker resolution, custom scheduler membership,
      single-observation ValueTask usage, and synchronous delegates.
- [ ] Validate trace parentage and fault-handler thread safety in your host.

Repository regression examples:
[worker correctness](../test/unit/AdaskoTheBeAsT.Interop.Execution.Test/WorkerCorrectnessTest.cs),
[pooled work items](../test/unit/AdaskoTheBeAsT.Interop.Execution.Test/PooledExecutionWorkItemTest.cs),
and [host deadlines](../test/unit/AdaskoTheBeAsT.Interop.Execution.Hosting.Test/ShutdownDeadlineTest.cs).
