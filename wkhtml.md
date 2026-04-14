# WkHtml engine with `ExecutionWorker<TSession>`

This note describes how `WkHtmlToXEngine` would change when it is rebuilt on top of `AdaskoTheBeAsT.Interop.Execution.ExecutionWorker<TSession>`.

## Goal

Keep the public behavior of the WkHtml engine:

- multiple callers can enqueue work concurrently
- all native work runs on one dedicated thread
- Windows can still use STA
- startup failures are surfaced immediately
- broken native state can be recycled

But move the generic worker mechanics out of the engine and into `ExecutionWorker<TSession>`.

## Recommended starting point

For WkHtml specifically, the recommended starting point is still a single `ExecutionWorker<TSession>`.

Move to `ExecutionWorkerPool<TSession>` only after confirming that:

- your native layout is truly isolated per worker
- separate worker-owned copies are safe to run in parallel
- you no longer need strict global FIFO ordering across the whole engine

## Responsibility split

| Current `WkHtmlToXEngine` responsibility | New owner |
| --- | --- |
| `BlockingCollection<ConvertWorkItemBase>` queue | `ExecutionWorker<TSession>` internal `Channel` |
| dedicated thread creation | `ExecutionWorker<TSession>` |
| startup handshake | `ExecutionWorker<TSession>.Initialize()` |
| pending-item cancellation/failure on shutdown | `ExecutionWorker<TSession>` |
| session lifetime on the worker thread | `IExecutionSessionFactory<TSession>` |
| WkHtml loader/module initialization | `WkHtmlToXSessionFactory` |
| WkHtml loader/module cleanup | `WkHtmlToXSessionFactory.DisposeSession` |
| PDF/Image dispatch | thin WkHtml engine adapter |

## New WkHtml-specific types

The WkHtml engine becomes a thin adapter over a worker session.

```csharp
internal sealed class WkHtmlToXSession
{
    public WkHtmlToXSession(
        ILibraryLoader loader,
        IPdfProcessor pdfProcessor,
        IImageProcessor imageProcessor)
    {
        Loader = loader;
        PdfProcessor = pdfProcessor;
        ImageProcessor = imageProcessor;
    }

    public ILibraryLoader Loader { get; }

    public IPdfProcessor PdfProcessor { get; }

    public IImageProcessor ImageProcessor { get; }
}
```

```csharp
internal sealed class WkHtmlToXSessionFactory : IExecutionSessionFactory<WkHtmlToXSession>
{
    private readonly WkHtmlToXConfiguration _configuration;
    private readonly ILibraryLoaderFactory _libraryLoaderFactory;

    public WkHtmlToXSessionFactory(
        WkHtmlToXConfiguration configuration,
        ILibraryLoaderFactory libraryLoaderFactory)
    {
        _configuration = configuration;
        _libraryLoaderFactory = libraryLoaderFactory;
    }

    public WkHtmlToXSession CreateSession(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var loader = _libraryLoaderFactory.Create(_configuration);
        loader.Load();

        var pdfProcessor = new PdfProcessor(_configuration, new WkHtmlToPdfModule());
        if (pdfProcessor.PdfModule.Initialize(0) != 1)
        {
            loader.Release();
            throw new PdfModuleInitializationException("Pdf module not loaded");
        }

        var imageProcessor = new ImageProcessor(_configuration, new WkHtmlToImageModule());
        if (imageProcessor.ImageModule.Initialize(0) != 1)
        {
            pdfProcessor.PdfModule.Terminate();
            loader.Release();
            throw new ImageModuleInitializationException("Image module not loaded");
        }

        return new WkHtmlToXSession(loader, pdfProcessor, imageProcessor);
    }

    public void DisposeSession(WkHtmlToXSession session)
    {
        session.ImageProcessor.ImageModule.Terminate();
        session.PdfProcessor.PdfModule.Terminate();
        session.Loader.Release();
    }
}
```

## Minimal migration

The smallest change keeps `IWkHtmlToXEngine` and the existing work item types.

```csharp
public sealed class WkHtmlToXEngine : IWkHtmlToXEngine
{
    private readonly ExecutionWorker<WkHtmlToXSession> _worker;

    public WkHtmlToXEngine(WkHtmlToXConfiguration configuration)
    {
        var sessionFactory = new WkHtmlToXSessionFactory(
            configuration,
            new LibraryLoaderFactory());

        _worker = new ExecutionWorker<WkHtmlToXSession>(
            sessionFactory,
            new ExecutionWorkerOptions(
                name: "WkHtmlToX Engine Worker",
                useStaThread: true));
    }

    public void Initialize() => _worker.Initialize();

    public void AddConvertWorkItem(ConvertWorkItemBase item, CancellationToken cancellationToken)
    {
        Task<bool> executionTask = item switch
        {
            PdfConvertWorkItem pdf => _worker.ExecuteAsync(
                (session, token) => session.PdfProcessor.Convert(pdf.Document, pdf.StreamFunc),
                new ExecutionRequestOptions(recycleSessionOnFailure: true),
                cancellationToken),

            ImageConvertWorkItem image => _worker.ExecuteAsync(
                (session, token) => session.ImageProcessor.Convert(image.Document, image.StreamFunc),
                new ExecutionRequestOptions(recycleSessionOnFailure: true),
                cancellationToken),

            _ => Task.FromException<bool>(
                new NotSupportedException($"Unsupported item type: {item.GetType().FullName}")),
        };

        _ = executionTask.ContinueWith(
            task =>
            {
                if (task.IsCanceled)
                {
                    item.TaskCompletionSource.TrySetCanceled();
                    return;
                }

                if (task.IsFaulted)
                {
                    item.TaskCompletionSource.TrySetException(task.Exception!.InnerExceptions);
                    return;
                }

                item.TaskCompletionSource.TrySetResult(task.Result);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Dispose() => _worker.Dispose();
}
```

## What disappears from the engine

After the migration, `WkHtmlToXEngine` no longer needs to own:

- `_blockingCollection`
- `_cancellationTokenSource`
- `_workerThread`
- `WorkerContext`
- `Process`
- `InitializeInProcessingThread`
- `CleanupProcessingThreadState`
- `CancelPendingWorkItems`
- `FailPendingWorkItems`
- most of the disposal/thread orchestration code

That logic is either generic worker logic or session-factory logic.

## Recommended behavior for WkHtml

For WkHtml specifically, the worker should usually be configured like this:

- `useStaThread: true` on Windows to preserve current thread behavior
- if `useStaThread` is enabled on non-Windows, the flag is ignored
- `recycleSessionOnFailure: true` for each conversion request
- optionally set `maxOperationsPerSession` if you want periodic native reset

That gives this runtime flow:

1. callers enqueue PDF/image work concurrently
2. `ExecutionWorker<WkHtmlToXSession>` serializes all work onto one dedicated thread
3. the first call to `Initialize()` or first queued item creates the WkHtml session
4. conversions reuse the same loaded native state
5. if a conversion fails and recycle is enabled, the session is terminated and recreated on the next request

## Preferred next step after minimal migration

The minimal migration keeps the current `ConvertWorkItemBase` + visitor-oriented design.

The cleaner end state is to remove the visitor/work-item abstraction from WkHtml completely and let higher layers call:

- `ConvertPdfAsync(...)`
- `ConvertImageAsync(...)`

Those methods would directly call `_worker.ExecuteAsync(...)` and return the resulting task, which makes the WkHtml engine much thinner and makes `ExecutionWorker<TSession>` the single place responsible for queueing, threading, and lifecycle.
