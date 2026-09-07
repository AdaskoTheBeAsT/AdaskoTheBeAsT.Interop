using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Default <see cref="IExecutionWorker{TSession}"/> implementation: a single
/// dedicated <see cref="Thread"/> consuming a <see cref="Channel{T}"/> of work
/// items and executing them sequentially against one <typeparamref name="TSession"/>.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public sealed class ExecutionWorker<TSession> : IExecutionWorker<TSession>
    where TSession : class
{
    private static readonly string TypeName = typeof(ExecutionWorker<TSession>).Name;

    private readonly Channel<IExecutionWorkItem<TSession>> _channel;
#if NET9_0_OR_GREATER
    private readonly Lock _syncRoot = new();
#else
    private readonly object _syncRoot = new();
#endif
    private readonly CancellationTokenSource _workerCancellationTokenSource = new();
    private readonly IExecutionSessionFactory<TSession> _sessionFactory;
    private readonly ExecutionWorkerOptions _options;
    private readonly TaskCompletionSource<object?> _workerExitCompletionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ExecutionWorkerRegistration _diagnosticsRegistration;
    private readonly ExecutionDiagnostics _diagnostics;

    private volatile ExceptionDispatchInfo? _fatalFailure;
    private Task? _startupTask;
    private Thread? _workerThread;
    private TSession? _session;
    private int _disposeState;
    private int _operationsProcessed;
    private int _faultEventRaised;
    private int _queueDepth;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorker{TSession}"/> class.
    /// </summary>
    /// <param name="sessionFactory">Factory that produces and tears down the
    /// per-worker session instance.</param>
    /// <param name="options">Optional worker configuration. Defaults to
    /// <see cref="ExecutionWorkerOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sessionFactory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/>
    /// violates its invariants (validated via <c>Validate()</c>).</exception>
    public ExecutionWorker(
        IExecutionSessionFactory<TSession> sessionFactory,
        ExecutionWorkerOptions? options = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _options = (options ?? ExecutionWorkerOptions.Default).Snapshot();
        _options.Validate();
        _channel = Channel.CreateUnbounded<IExecutionWorkItem<TSession>>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = false,
            });

        _diagnostics = _options.Diagnostics ?? ExecutionDiagnostics.Shared;
        _diagnosticsRegistration = new ExecutionWorkerRegistration(
            _options.Name,
            () => Volatile.Read(ref _queueDepth));
        _diagnostics.RegisterWorker(_diagnosticsRegistration);
    }

    /// <inheritdoc />
    public event EventHandler<WorkerFaultedEventArgs>? WorkerFaulted;

    /// <inheritdoc />
    public bool IsFaulted => _fatalFailure is not null;

    /// <inheritdoc />
    public Exception? Fault => _fatalFailure?.SourceException;

    /// <inheritdoc />
    public int QueueDepth => Volatile.Read(ref _queueDepth);

    /// <inheritdoc />
    public string? Name => _options.Name;

    internal bool IsCurrentThread => ReferenceEquals(_workerThread, Thread.CurrentThread);

    /// <inheritdoc />
    public ExecutionWorkerSnapshot GetSnapshot()
    {
        var fatalFailure = _fatalFailure;
        return new ExecutionWorkerSnapshot(
            _options.Name,
            Volatile.Read(ref _queueDepth),
            fatalFailure is not null,
            fatalFailure?.SourceException);
    }

    /// <inheritdoc />
    public void Initialize()
    {
        EnsureInitialized();
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var startupTask = EnsureStartedLockedAsync();
        return ExecutionHelpers.WaitForStartupAsync(startupTask, cancellationToken);
    }

    /// <inheritdoc />
    public Task ExecuteAsync(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(action);
#else
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }
#endif

        var executionAction = action;
        return ExecuteAsync<object?>(
            (session, token) =>
            {
                executionAction(session, token);
                return null;
            },
            options,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteAsync<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(action);
#else
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }
#endif

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TResult>(cancellationToken);
        }

        var effectiveOptions = options ?? ExecutionRequestOptions.Default;
        _ = EnsureStartedLockedAsync();
        var workItem = new ExecutionWorkItem<TResult>(action, effectiveOptions, cancellationToken);
        Submit(workItem);
        return workItem.Task;
    }

    /// <summary>
    /// Pooled ValueTask equivalent of
    /// <see cref="ExecuteAsync(Action{TSession, CancellationToken}, ExecutionRequestOptions?, CancellationToken)"/>
    /// backed by a pooled <see cref="System.Threading.Tasks.Sources.IValueTaskSource"/>.
    /// </summary>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when <paramref name="action"/> finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Pooling avoids per-request source/Task allocations when a work item is reused;
    /// it does not guarantee allocation-free execution.
    /// The returned <see cref="ValueTask"/> MUST be observed (awaited or
    /// <c>AsTask()</c>'d) exactly once, as required by the framework spec —
    /// the underlying source is recycled on first observation.
    /// </remarks>
    public ValueTask ExecuteValueAsync(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(action);
#else
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }
#endif

        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask(Task.FromCanceled(cancellationToken));
        }

        var effectiveOptions = options ?? ExecutionRequestOptions.Default;
        _ = EnsureStartedLockedAsync();
        var workItem = PooledVoidExecutionWorkItem<TSession>.Rent(action, effectiveOptions, cancellationToken);
        var result = new ValueTask(workItem, workItem.Version);
        Submit(workItem);
        return result;
    }

    /// <summary>
    /// Pooled ValueTask equivalent of
    /// <see cref="ExecuteAsync{TResult}(Func{TSession, CancellationToken, TResult}, ExecutionRequestOptions?, CancellationToken)"/>
    /// backed by a pooled <see cref="System.Threading.Tasks.Sources.IValueTaskSource{TResult}"/>.
    /// </summary>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> producing the delegate result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Pooling avoids per-request source/Task allocations when a work item is reused;
    /// it does not guarantee allocation-free execution.
    /// The returned <see cref="ValueTask{TResult}"/> MUST be observed exactly
    /// once, as required by the framework spec — the underlying source is
    /// recycled on first observation.
    /// </remarks>
    public ValueTask<TResult> ExecuteValueAsync<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(action);
#else
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }
#endif

        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<TResult>(Task.FromCanceled<TResult>(cancellationToken));
        }

        var effectiveOptions = options ?? ExecutionRequestOptions.Default;
        _ = EnsureStartedLockedAsync();
        var workItem = PooledValueExecutionWorkItem<TSession, TResult>.Rent(action, effectiveOptions, cancellationToken);
        var result = new ValueTask<TResult>(workItem, workItem.Version);
        Submit(workItem);
        return result;
    }

    /// <summary>
    /// Asynchronously completes the queue using the configured shutdown mode, disposes
    /// the session on the worker thread, and waits for the worker thread exit.
    /// Every external caller awaits the same exit. A call from the worker only
    /// requests shutdown, since awaiting its own exit would deadlock.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes when the worker thread exits.</returns>
    public ValueTask DisposeAsync()
    {
        var exit = RequestStopAsync();
        return IsCurrentThread ? default : new ValueTask(exit);
    }

    /// <summary>
    /// Synchronous disposal bounded by
    /// <see cref="ExecutionWorkerOptions.DisposeTimeout"/>. Prefer
    /// <see cref="DisposeAsync"/> whenever practical; <see cref="Dispose"/> is
    /// provided for RAII-style and .NET Framework call sites that cannot await.
    /// </summary>
    public void Dispose()
    {
        var disposeTask = DisposeAsync().AsTask();
        var timeout = _options.DisposeTimeout;

        // Safe sync-over-async (VSTHRD002 disabled): DisposeAsync's completion is driven
        // by _workerExitCompletionSource, a TaskCompletionSource constructed with
        // RunContinuationsAsynchronously and signaled from the dedicated worker Thread's
        // Process method. No caller SynchronizationContext is captured on either side,
        // so GetAwaiter().GetResult() cannot deadlock. Task.Wait(timeout) is used first
        // to bound the wait by ExecutionWorkerOptions.DisposeTimeout (default
        // Timeout.InfiniteTimeSpan, matching the historical Thread.Join contract); on
        // completion we call GetAwaiter().GetResult() instead of relying on Wait so any
        // DisposeAsync fault is rethrown unwrapped with the original exception type
        // rather than wrapped in an AggregateException. This is the exception-propagation
        // advantage of option A over a bare Thread.Join in the sync disposal path.
#pragma warning disable VSTHRD002, MA0040
        bool completed;
        try
        {
            // Stop cancellation must not cancel the wait for actual thread exit.
            completed = disposeTask.Wait((int)timeout.TotalMilliseconds, CancellationToken.None);
        }
        catch (AggregateException)
        {
            // Task.Wait throws AggregateException if the task faulted within the
            // timeout. Fall through so GetAwaiter().GetResult() rethrows the original
            // exception unwrapped.
            completed = true;
        }

        if (!completed)
        {
            // Timeout guard: abandon the wait to avoid blocking the caller
            // indefinitely if the worker fails to drain within DisposeTimeout.
            return;
        }

        disposeTask.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002, MA0040
    }

    internal bool TryCompleteChannelForTesting(Exception? exception = null)
    {
        return _channel.Writer.TryComplete(exception);
    }

    internal Task RequestStopAsync()
    {
        var cancelStartup = false;
        lock (_syncRoot)
        {
            if (Interlocked.Exchange(ref _disposeState, 1) == 0)
            {
                _channel.Writer.TryComplete();
                if (_workerThread is null)
                {
                    _diagnostics.UnregisterWorker(_diagnosticsRegistration);
                    _workerCancellationTokenSource.Dispose();
                    _workerExitCompletionSource.TrySetResult(null);
                }
                else
                {
                    cancelStartup = true;
                }
            }
        }

        if (cancelStartup)
        {
            // Application callbacks must not block DisposeAsync before it returns
            // its join task, nor run while a pool holds its disposal lock.
            ThreadPool.QueueUserWorkItem(_ =>
            {
#pragma warning disable VSTHRD103, MA0042
                ExecutionHelpers.TryIgnore(_workerCancellationTokenSource.Cancel);
#pragma warning restore VSTHRD103, MA0042
            });
        }

        // Completion belongs to this worker and runs continuations asynchronously.
#pragma warning disable VSTHRD003
        return _workerExitCompletionSource.Task;
#pragma warning restore VSTHRD003
    }

    internal void Process(object? state)
    {
        if (state is not TaskCompletionSource<object?> startupCompletionSource)
        {
            throw new ArgumentException(message: "Invalid worker startup state.", paramName: nameof(state));
        }

        Exception? fatalException = null;

        try
        {
            EnsureSessionCreated(_workerCancellationTokenSource.Token);
            startupCompletionSource.TrySetResult(null);
            ProcessChannel();
        }
        catch (OperationCanceledException) when (_workerCancellationTokenSource.IsCancellationRequested)
        {
            startupCompletionSource.TrySetCanceled(_workerCancellationTokenSource.Token);
        }
        catch (Exception exception)
        {
            fatalException = exception;
            SetFatalFailure(ExceptionDispatchInfo.Capture(exception));
            startupCompletionSource.TrySetException(exception);
        }
        finally
        {
            if (fatalException is not null)
            {
                SetFatalFailure(ExceptionDispatchInfo.Capture(fatalException));
            }

            try
            {
                DisposeSession();
            }
            catch (Exception exception)
            {
                fatalException ??= exception;
                SetFatalFailure(ExceptionDispatchInfo.Capture(fatalException));
            }

            _channel.Writer.TryComplete(Fault);
            FailPendingItems(Fault ?? new ObjectDisposedException(TypeName));
            _workerCancellationTokenSource.Dispose();
            _diagnostics.UnregisterWorker(_diagnosticsRegistration);

            // Signal worker-thread exit to any awaiter of DisposeAsync. We always
            // complete with a successful result so DisposeAsync does not resurface
            // terminal session/create/dispose failures (those remain observable via
            // IsFaulted / Fault / WorkerFaulted surface); this
            // preserves the historical sync Dispose() contract of silently ignoring
            // session dispose failures.
            _workerExitCompletionSource.TrySetResult(null);
        }
    }

    internal void ThrowIfFaulted()
    {
        var fatalFailure = _fatalFailure;
        if (fatalFailure is null)
        {
            return;
        }

        fatalFailure.Throw();
    }

    internal void SetFatalFailure(ExceptionDispatchInfo fatalFailure)
    {
        lock (_syncRoot)
        {
            if (_fatalFailure is not null)
            {
                return;
            }

            _fatalFailure = fatalFailure;
            _channel.Writer.TryComplete(fatalFailure.SourceException);
        }

        // Never run application fault handlers on the session-owning thread.
        ThreadPool.QueueUserWorkItem(_ => RaiseFaultedOnce(fatalFailure.SourceException));
    }

    internal void SetWorkerThreadForTesting(Thread? workerThread)
    {
        lock (_syncRoot)
        {
            _workerThread = workerThread;
        }
    }

    private void RaiseFaultedOnce(Exception exception)
    {
        if (Interlocked.CompareExchange(ref _faultEventRaised, 1, 0) != 0)
        {
            return;
        }

        var handler = WorkerFaulted;
        if (handler is null)
        {
            return;
        }

        var args = new WorkerFaultedEventArgs(exception, _options.Name);
        foreach (var subscriber in handler.GetInvocationList())
        {
            var typedSubscriber = (EventHandler<WorkerFaultedEventArgs>)subscriber;
            ExecutionHelpers.TryIgnore(() => typedSubscriber(this, args));
        }
    }

    private void EnsureInitialized()
    {
        var startupTask = EnsureStartedLockedAsync();

        // Safe to block here (VSTHRD002 disabled): the startup TaskCompletionSource is created with
        // RunContinuationsAsynchronously and the worker runs on a bare Thread, so no caller
        // SynchronizationContext is captured and no sync-over-async deadlock is possible. The wait is
        // a one-time synchronous handoff to the dedicated worker thread and preserves the synchronous
        // Initialize() contract for legacy (non-cancellable) callers. Async-aware callers should use
        // InitializeAsync(CancellationToken) instead.
#pragma warning disable VSTHRD002
        startupTask.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    private Task EnsureStartedLockedAsync()
    {
        // Thread-safety contract:
        // - _syncRoot serializes all access to _initialized, _workerThread and
        //   _startupTask; no other path reads or writes them outside this lock.
        // - _fatalFailure is volatile, read here via ThrowIfFaulted before the
        //   _initialized check so a faulted worker can never be (re-)initialised:
        //   ExecutionWorker is terminal once _fatalFailure is set.
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            ThrowIfFaulted();

            if (!_initialized)
            {
                var startupCompletionSource = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var thread = new Thread(Process)
                {
                    IsBackground = true,
                    Name = CreateThreadName(),
                };

                _startupTask = startupCompletionSource.Task;
                _ = _startupTask.ContinueWith(
                    static failed => GC.KeepAlive(failed.Exception),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                _initialized = true;
                try
                {
                    ConfigureThread(thread);
                    _workerThread = thread;
                    thread.Start(startupCompletionSource);
                }
                catch (Exception exception)
                {
                    _workerThread = null;
                    SetFatalFailure(ExceptionDispatchInfo.Capture(exception));
                    startupCompletionSource.TrySetException(exception);
                    _workerExitCompletionSource.TrySetResult(null);
                    _diagnostics.UnregisterWorker(_diagnosticsRegistration);
                }
            }

            return _startupTask!;
        }
    }

    private void ProcessChannel()
    {
        while (WaitToRead())
        {
            while (_channel.Reader.TryRead(out var workItem))
            {
                Interlocked.Decrement(ref _queueDepth);
                ProcessWorkItem(workItem);
                if (IsFaulted)
                {
                    return;
                }
            }
        }
    }

    private void ProcessWorkItem(IExecutionWorkItem<TSession> workItem)
    {
        var activity = StartActivity(workItem.ParentContext);
        var (failure, canceled) = ExecuteOnSession(workItem);
        var outcome = failure is null ? ExecutionDiagnosticNames.OutcomeSuccess : ExecutionDiagnosticNames.OutcomeFaulted;
        if (canceled)
        {
            outcome = ExecutionDiagnosticNames.OutcomeCancelled;
        }

        RecordOperationOutcome(outcome);
        try
        {
            activity?.SetStatus(failure is null ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            activity?.Dispose();
        }
        catch (Exception exception)
        {
            GC.KeepAlive(exception);
        }

        // Publication is the final access: a ValueTask consumer may recycle the
        // item immediately, including from another worker sharing the same pool.
        if (canceled)
        {
            workItem.TrySetCanceled();
        }
        else if (failure is not null)
        {
            workItem.TrySetException(failure);
        }
        else
        {
            workItem.TrySetResult();
        }
    }

    private (Exception? Failure, bool Canceled) ExecuteOnSession(IExecutionWorkItem<TSession> workItem)
    {
        if (Volatile.Read(ref _disposeState) != 0 && _options.ShutdownMode == ExecutionShutdownMode.CancelPending)
        {
            return (null, true);
        }

        TSession session;
        try
        {
            workItem.CancellationToken.ThrowIfCancellationRequested();
            session = EnsureSessionCreated(workItem.CancellationToken);
        }
        catch (OperationCanceledException) when (workItem.CancellationToken.IsCancellationRequested)
        {
            return (null, true);
        }
        catch (Exception exception)
        {
            SetFatalFailure(ExceptionDispatchInfo.Capture(exception));
            return (exception, false);
        }

        Exception? failure = null;
        try
        {
            workItem.Execute(session);
        }
        catch (OperationCanceledException) when (workItem.CancellationToken.IsCancellationRequested)
        {
            return (null, true);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            if (failure is null)
            {
                var operations = Interlocked.Increment(ref _operationsProcessed);
                if (_options.MaxOperationsPerSession > 0 && operations >= _options.MaxOperationsPerSession)
                {
                    DisposeSession();
                    RecordSessionRecycle(ExecutionDiagnosticNames.RecycleMaxOperations);
                }
            }
            else if (workItem.Options.RecycleSessionOnFailure)
            {
                DisposeSession();
                RecordSessionRecycle(ExecutionDiagnosticNames.RecycleFailure);
            }
        }
        catch (Exception exception)
        {
            SetFatalFailure(ExceptionDispatchInfo.Capture(exception));
            failure ??= exception;
        }

        return (failure, false);
    }

    private Activity? StartActivity(ActivityContext parentContext)
    {
        try
        {
            // Do not inherit Thread.Start's caller or a previous activity whose
            // listener threw while stopping. Each request carries its own parent.
            Activity.Current = null;
            var activity = _diagnostics.ActivitySource.StartActivity(
                ExecutionDiagnosticNames.ActivityExecute, ActivityKind.Internal, parentContext);
            activity?.SetTag(ExecutionDiagnosticNames.TagWorkerName, _options.Name);
            return activity;
        }
        catch (Exception exception)
        {
            GC.KeepAlive(exception);
            return null;
        }
    }

    private void RecordOperationOutcome(string outcome)
    {
        try
        {
            _diagnostics.OperationsCounter.Add(
                1,
                new KeyValuePair<string, object?>(ExecutionDiagnosticNames.TagWorkerName, _options.Name),
                new KeyValuePair<string, object?>(ExecutionDiagnosticNames.TagOutcome, outcome));
        }
        catch (Exception exception)
        {
            GC.KeepAlive(exception);
        }
    }

    private void RecordSessionRecycle(string reason)
    {
        try
        {
            _diagnostics.SessionRecyclesCounter.Add(
                1,
                new KeyValuePair<string, object?>(ExecutionDiagnosticNames.TagWorkerName, _options.Name),
                new KeyValuePair<string, object?>(ExecutionDiagnosticNames.TagRecycleReason, reason));
        }
        catch (Exception exception)
        {
            GC.KeepAlive(exception);
        }
    }

    private void Submit(IExecutionWorkItem<TSession> workItem)
    {
        var admitted = Interlocked.Increment(ref _queueDepth);
        if (_options.QueueCapacity > 0 && admitted > _options.QueueCapacity)
        {
            Interlocked.Decrement(ref _queueDepth);
            workItem.TrySetException(new InvalidOperationException("The worker admission capacity is exhausted."));
            return;
        }

        // Queue during startup too: independent async continuations would reorder
        // submissions and could outlive the worker's disposal completion.
        Enqueue(workItem);
    }

    private void Enqueue(IExecutionWorkItem<TSession> workItem)
    {
        // Submit reserves queue depth before publication, including startup waiters.
        if (!_channel.Writer.TryWrite(workItem))
        {
            Interlocked.Decrement(ref _queueDepth);
            workItem.TrySetException(Fault ?? new ObjectDisposedException(TypeName));
        }
    }

    private TSession EnsureSessionCreated(CancellationToken cancellationToken)
    {
        var existingSession = Volatile.Read(ref _session);
        if (existingSession is not null)
        {
            return existingSession;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var createdSession = _sessionFactory.CreateSession(cancellationToken)
            ?? throw new InvalidOperationException("The session factory returned null.");

        Volatile.Write(ref _session, createdSession);
        Interlocked.Exchange(ref _operationsProcessed, 0);

        return createdSession;
    }

    private bool WaitToRead()
    {
        // Safe to block here (VSTHRD002 disabled): this code runs exclusively on the dedicated worker
        // Thread (see Process / EnsureStartedLocked), where synchronous blocking is the intended
        // execution model. The worker thread has no captured SynchronizationContext, so bridging the
        // async Channel reader into a synchronous processing loop cannot deadlock.
        // Completing the writer wakes idle readers without abandoning admitted work.
#pragma warning disable VSTHRD002
        return _channel.Reader.WaitToReadAsync(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
#pragma warning restore VSTHRD002
    }

    private void ConfigureThread(Thread thread)
    {
        if (!_options.UseStaThread)
        {
            return;
        }

#if NET5_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
#else
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }
#endif

        thread.SetApartmentState(ApartmentState.STA);
    }

    private string CreateThreadName()
    {
        var name = _options.Name;
#pragma warning disable S8969
        return string.IsNullOrWhiteSpace(name)
            ? $"{typeof(TSession).Name} Execution Worker"
            : name!;
#pragma warning restore S8969
    }

    private void FailPendingItems(Exception exception)
    {
        while (_channel.Reader.TryRead(out var workItem))
        {
            Interlocked.Decrement(ref _queueDepth);
            if (workItem.CancellationToken.IsCancellationRequested)
            {
                workItem.TrySetCanceled();
            }
            else
            {
                workItem.TrySetException(exception);
            }
        }
    }

    private void ThrowIfDisposed()
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, TypeName);
#else
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(TypeName);
        }
#endif
    }

    private void DisposeSession()
    {
        var session = Volatile.Read(ref _session);
        if (session is null)
        {
            return;
        }

        Volatile.Write(ref _session, value: null);
        Interlocked.Exchange(ref _operationsProcessed, 0);
        _sessionFactory.DisposeSession(session);
    }

    private sealed class ExecutionWorkItem<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions options,
        CancellationToken cancellationToken) : IExecutionWorkItem<TSession>
    {
        private readonly Func<TSession, CancellationToken, TResult> _action = action;
        private readonly TaskCompletionSource<TResult> _completionSource = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private TResult? _result;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public ExecutionRequestOptions Options { get; } = options;

        public ActivityContext ParentContext { get; } = Activity.Current?.Context ?? default;

        public Task<TResult> Task => _completionSource.Task;

        public void Execute(TSession session)
        {
            _result = _action(session, CancellationToken);
        }

        public void TrySetResult()
        {
            _completionSource.TrySetResult(_result!);
        }

        public void TrySetException(Exception exception)
        {
            _completionSource.TrySetException(exception);
        }

        public void TrySetCanceled()
        {
            _completionSource.TrySetCanceled(CancellationToken);
        }
    }
}
