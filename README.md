# AdaskoTheBeAsT.Interop

> 🧩 A focused interop toolbox for dedicated-thread execution, native library isolation, and COM-friendly workloads.

This repository currently contains `AdaskoTheBeAsT.Interop.Execution` and is designed to work nicely with sibling packages such as `AdaskoTheBeAsT.Interop.COM`, `AdaskoTheBeAsT.Interop.Threading`, and `AdaskoTheBeAsT.Interop.Unmanaged`.

## ✨ Why this exists

Interop code is fun right up until it isn't:

- native libraries want thread affinity
- COM components want STA and message pumping
- some engines behave better when a single background thread owns all state
- some workloads need explicit load / unload / recycle behavior instead of "hope the process survives"

`AdaskoTheBeAsT.Interop.Execution` gives you a reusable worker for exactly that shape of problem.

## 📦 What is in this repo?

Today this repo contains:

- `AdaskoTheBeAsT.Interop.Execution`
  - `ExecutionWorker<TSession>` for one dedicated thread and one session owner
  - `ExecutionWorkerPool<TSession>` for multiple dedicated workers
  - multi-writer, single-reader execution per worker built on `Channel`
  - thread-owned sessions created by `IExecutionSessionFactory<TSession>`
  - optional STA on Windows
  - session recycle after failure or after N operations

## 🚀 Core idea

Instead of letting every interop-heavy engine invent its own:

- queue
- worker thread
- startup synchronization
- session lifetime
- disposal logic
- failure / recycle behavior

...you move that generic machinery into `ExecutionWorker<TSession>` or `ExecutionWorkerPool<TSession>`.

Your engine becomes a thin adapter that says:

1. how to create a session
2. how to dispose a session
3. what work should run on that session

## 🧠 Main types

### `ExecutionWorker<TSession>`

The worker owns:

- a `Channel` with multiple writers and one reader
- one dedicated background thread
- startup / shutdown lifecycle
- pending-work cancellation on shutdown
- session reuse
- optional session recycle

### `ExecutionWorkerPool<TSession>`

The pool owns:

- multiple `ExecutionWorker<TSession>` instances
- round-robin work distribution
- one session per worker
- per-worker isolation for native state
- initialization and disposal of the whole worker set

### `IExecutionSessionFactory<TSession>`

The factory owns:

- creating the thread-affine session
- loading native libraries
- initializing modules
- disposing / unloading the session

### `ExecutionWorkerOptions`

Use it to configure:

- `name`
- `useStaThread`
- `maxOperationsPerSession` (`0` means unlimited and is the default)

### `ExecutionWorkerPoolOptions`

Use it to configure:

- `workerCount`
- `name`
- `useStaThread`
- `maxOperationsPerSession` (`0` means unlimited and is the default)

### `ExecutionRequestOptions`

Use it per request to configure:

- `recycleSessionOnFailure`

## 🪟 STA behavior

If `useStaThread: true` is set:

- on Windows, the worker thread is configured as `STA`
- on non-Windows, the flag is simply ignored

That makes the option safe for cross-platform callers that want "STA when possible" behavior.

## ⚡ Quick example

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

using var worker = new ExecutionWorker<NativeSession>(
    new NativeSessionFactory(),
    new ExecutionWorkerOptions(
        name: "Native Render Worker",
        useStaThread: true,
        maxOperationsPerSession: 500));

worker.Initialize();

var bytes = await worker.ExecuteAsync(
    (session, cancellationToken) => session.Render("<h1>Hello</h1>"),
    new ExecutionRequestOptions(recycleSessionOnFailure: true),
    cancellationToken);
```

## 🏊 Multiple workers

If one worker is not enough, use `ExecutionWorkerPool<TSession>`.

This is useful when:

- you have several native library copies in separate folders
- each worker should load its own isolated DLL set
- one failed worker session should be recycled without touching the others
- you want several dedicated threads, but still want serialized execution per worker

### Pool behavior

- calls are distributed round-robin
- each worker has its own dedicated thread
- each worker has its own session
- each worker can use its own session factory inputs
- if a request fails with `recycleSessionOnFailure: true`, only the selected worker session is recycled

### Pool example with isolated native folders

```csharp
using AdaskoTheBeAsT.Interop.Execution;

public sealed class NativePoolSession
{
    public NativePoolSession(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    public byte[] Render(string html) => [];
}

public sealed class NativePoolSessionFactory : IExecutionSessionFactory<NativePoolSession>
{
    private readonly string _folder;

    public NativePoolSessionFactory(string folder)
    {
        _folder = folder;
    }

    public NativePoolSession CreateSession(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new NativePoolSession(_folder);
    }

    public void DisposeSession(NativePoolSession session)
    {
    }
}

using var workerPool = new ExecutionWorkerPool<NativePoolSession>(
    workerIndex => new NativePoolSessionFactory($@"c:\native\slot-{workerIndex + 1:D2}"),
    new ExecutionWorkerPoolOptions(
        workerCount: 4,
        name: "Native Pool",
        useStaThread: true,
        maxOperationsPerSession: 250));

workerPool.Initialize();

var result = await workerPool.ExecuteAsync(
    (session, cancellationToken) => session.Render("<h1>Hello from pool</h1>"),
    new ExecutionRequestOptions(recycleSessionOnFailure: true),
    cancellationToken);
```

## 🧭 When to use what?

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

## ♻️ Session recycle story

This is where the worker becomes especially useful for native code.

You can choose to recycle the session:

- after a failed request
- after a fixed number of operations
- or both

Set `maxOperationsPerSession: 0` when you want unlimited session lifetime and only failure-based recycling.

That means you can:

- keep one loaded native state for fast steady-state execution
- reset it when it becomes unhealthy
- prepare engines that load DLLs from isolated folders
- scale out to multiple isolated worker-owned sessions

## 🧪 Build and test

```powershell
dotnet build .\AdaskoTheBeAsT.Interop.slnx
dotnet test .\AdaskoTheBeAsT.Interop.slnx --no-build
```

## 📘 Extra notes

- WkHtml migration details live in [`wkhtml.md`](./wkhtml.md).

---

Built for the kind of interop code that likes one owner thread, explicit lifecycle, and zero drama. 😎
