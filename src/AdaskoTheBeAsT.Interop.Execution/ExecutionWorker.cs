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
        _options = options ?? ExecutionWorkerOptions.Default;
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

        ThrowIfDisposed();
        EnsureInitialized();
        ThrowIfFaulted();

        var effectiveOptions = options ?? ExecutionRequestOptions.Default;
        var workItem = new ExecutionWorkItem<TResult>(action, effectiveOptions, cancellationToken);
        if (!_channel.Writer.TryWrite(workItem))
        {
            return Task.FromException<TResult>(new ObjectDisposedException(TypeName));
        }

        Interlocked.Increment(ref _queueDepth);
        return workItem.Task;
    }

    /// <summary>
    /// Zero-allocation hot-path equivalent of
    /// <see cref="ExecuteAsync(Action{TSession, CancellationToken}, ExecutionRequestOptions?, CancellationToken)"/>
    /// backed by a pooled <see cref="System.Threading.Tasks.Sources.IValueTaskSource"/>.
    /// </summary>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when <paramref name="action"/> finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <remarks>
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

        ThrowIfDisposed();
        EnsureInitialized();
        ThrowIfFaulted();

        var effectiveOptions = options ?? ExecutionRequestOptions.Default;
        var workItem = PooledVoidExecutionWorkItem<TSession>.Rent(action, effectiveOptions, cancellationToken);
        if (!_channel.Writer.TryWrite(workItem))
        {
            // Rent consumed a pooled instance; release it back to the pool via
            // the explicit TrySet/GetResult cycle so we do not leak a slot.
            workItem.TrySetException(new ObjectDisposedException(TypeName));
            return new ValueTask(workItem, workItem.Version);
        }

        Interlocked.Increment(ref _queueDepth);
        return new ValueTask(workItem, workItem.Version);
    }

    /// <summary>
    /// Zero-allocation hot-path equivalent of
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

        ThrowIfDisposed();
        EnsureInitialized();
        ThrowIfFaulted();

        var effectiveOptions = options ?? ExecutionRequestOptions.Default;
        var workItem = PooledValueExecutionWorkItem<TSession, TResult>.Rent(action, effectiveOptions, cancellationToken);
        if (!_channel.Writer.TryWrite(workItem))
        {
            workItem.TrySetException(new ObjectDisposedException(TypeName));
            return new ValueTask<TResult>(workItem, workItem.Version);
        }

        Interlocked.Increment(ref _queueDepth);
        return new ValueTask<TResult>(workItem, workItem.Version);
    }

    /// <summary>
    /// Asynchronously completes the work queue, cancels pending items, disposes
    /// the session on the worker thread, and waits for the worker thread exit.
    /// Idempotent.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes when the worker thread exits.</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return default;
        }

        _diagnostics.UnregisterWorker(_diagnosticsRegistration);

        _channel.Writer.TryComplete();

        // VSTHRD103 / MA0042 disabled: Cancel() is the correct primitive here —
        // DisposeAsync is a non-async method returning ValueTask, and cancellation is
        // a fire-and-forget signal to the worker thread. CancelAsync would only change
        // where registered callbacks execute; the cancellation effect is synchronous either way.
#pragma warning disable VSTHRD103, MA0042
        try
        {
            _workerCancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException exception)
        {
            GC.KeepAlive(exception);
        }
#pragma warning restore VSTHRD103, MA0042

        Thread? workerThread;
        lock (_syncRoot)
        {
            workerThread = _workerThread;
        }

        if (workerThread is null)
        {
            _workerCancellationTokenSource.Dispose();
            return default;
        }

        if (ReferenceEquals(workerThread, Thread.CurrentThread))
        {
            // Reentrant dispose from inside the worker's own delegate: we cannot await
            // our own thread exit without deadlocking. The worker will still run its
            // finally block (which disposes the session and the CTS) once the current
            // delegate unwinds, because the channel is now completed and the CTS is
            // cancelled. Callers awaiting DisposeAsync from elsewhere will see the
            // exit TCS signal when that happens.
            return default;
        }

        // Safe to return a task produced outside this method (VSTHRD003 disabled):
        // _workerExitCompletionSource is the worker's own exit TaskCompletionSource,
        // signaled exclusively by ExecutionWorker.Process on the dedicated worker
        // Thread. Awaiting it from DisposeAsync is the designed contract for the
        // IAsyncDisposable surface. The TCS is built with RunContinuationsAsynchronously
        // so continuations do not inline back onto the worker thread.
#pragma warning disable VSTHRD003
        return new ValueTask(_workerExitCompletionSource.Task);
#pragma warning restore VSTHRD003
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
            // MA0040 suppressed: passing _workerCancellationTokenSource.Token to Wait
            // would make the wait throw OperationCanceledException as soon as dispose's
            // own cancellation signal fires (which we triggered above on line 304),
            // defeating the timeout-bounded sync-over-async pattern.
            completed = disposeTask.Wait(timeout);
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
            startupCompletionSource.TrySetResult(null);
        }
        catch (Exception exception)
        {
            fatalException = exception;
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

            _channel.Writer.TryComplete(fatalException);
            FailPendingItems(fatalException ?? new ObjectDisposedException(TypeName));
            _workerCancellationTokenSource.Dispose();

            // Signal worker-thread exit to any awaiter of DisposeAsync. We always
            // complete with a successful result so DisposeAsync does not resurface
            // terminal session/create/dispose failures (those remain observable via
            // ThrowIfFaulted / the upcoming IsFaulted / WorkerFaulted surface); this
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

#if NET8_0_OR_GREATER
        throw fatalFailure.SourceException;
#else
        fatalFailure.Throw();
#endif
    }

    internal void SetFatalFailure(ExceptionDispatchInfo fatalFailure)
    {
        // Dispatch the WorkerFaulted event BEFORE exposing the fault through
        // the _fatalFailure field. This establishes a happens-before chain so
        // any external observer that sees IsFaulted == true has also seen the
        // event handlers run — closing the race where a test (or a dashboard)
        // could poll IsFaulted, find it true, and assert on subscriber state
        // that the worker thread had not yet notified. Handlers receive the
        // exception through WorkerFaultedEventArgs; they do not need
        // IsFaulted / Fault inside their scope.
        RaiseFaultedOnce(fatalFailure.SourceException);

        // Volatile-semantics write (the field is declared volatile) publishes
        // the fault after the event ordering barrier above.
        _fatalFailure = fatalFailure;
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

                ConfigureThread(thread);

                _workerThread = thread;
                _startupTask = startupCompletionSource.Task;
                _initialized = true;
                thread.Start(startupCompletionSource);
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
            }
        }
    }

    private void ProcessWorkItem(IExecutionWorkItem<TSession> workItem)
    {
        if (workItem.CancellationToken.IsCancellationRequested)
        {
            workItem.TrySetCanceled();
            RecordOperationOutcome(ExecutionDiagnosticNames.OutcomeCancelled);
            return;
        }

        var workerName = _options.Name;
        var activity = _diagnostics.ActivitySource.StartActivity(
            ExecutionDiagnosticNames.ActivityExecute,
            ActivityKind.Internal);
        activity?.SetTag(ExecutionDiagnosticNames.TagWorkerName, workerName);

        try
        {
            try
            {
                var session = EnsureSessionCreated(workItem.CancellationToken);
                workItem.Execute(session);
                var operationsProcessed = Interlocked.Increment(ref _operationsProcessed);

                if (_options.MaxOperationsPerSession > 0 &&
                    operationsProcessed >= _options.MaxOperationsPerSession)
                {
                    DisposeSession();
                    RecordSessionRecycle(ExecutionDiagnosticNames.RecycleMaxOperations);
                }

                activity?.SetStatus(ActivityStatusCode.Ok);
                RecordOperationOutcome(ExecutionDiagnosticNames.OutcomeSuccess);
            }
            catch (OperationCanceledException) when (workItem.CancellationToken.IsCancellationRequested)
            {
                workItem.TrySetCanceled();
                activity?.SetStatus(ActivityStatusCode.Ok);
                RecordOperationOutcome(ExecutionDiagnosticNames.OutcomeCancelled);
            }
            catch (Exception exception)
            {
                workItem.TrySetException(exception);
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

                if (workItem.Options.RecycleSessionOnFailure)
                {
                    DisposeSession();
                    RecordSessionRecycle(ExecutionDiagnosticNames.RecycleFailure);
                }

                RecordOperationOutcome(ExecutionDiagnosticNames.OutcomeFaulted);
            }
        }
        finally
        {
            activity?.Dispose();
        }
    }

    private void RecordOperationOutcome(string outcome)
    {
        _diagnostics.OperationsCounter.Add(
            1,
            new KeyValuePair<string, object?>(ExecutionDiagnosticNames.TagWorkerName, _options.Name),
            new KeyValuePair<string, object?>(ExecutionDiagnosticNames.TagOutcome, outcome));
    }

    private void RecordSessionRecycle(string reason)
    {
        _diagnostics.SessionRecyclesCounter.Add(
            1,
            new KeyValuePair<string, object?>(ExecutionDiagnosticNames.TagWorkerName, _options.Name),
            new KeyValuePair<string, object?>(ExecutionDiagnosticNames.TagRecycleReason, reason));
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
        // async Channel reader into a synchronous processing loop cannot deadlock. Cancellation is
        // honored via _workerCancellationTokenSource which Dispose() signals on shutdown.
#pragma warning disable VSTHRD002
        return _channel.Reader.WaitToReadAsync(_workerCancellationTokenSource.Token)
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
        return string.IsNullOrWhiteSpace(name)
            ? $"{typeof(TSession).Name} Execution Worker"
            : name!;
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

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public ExecutionRequestOptions Options { get; } = options;

        public Task<TResult> Task => _completionSource.Task;

        public void Execute(TSession session)
        {
            var result = _action(session, CancellationToken);
            _completionSource.TrySetResult(result);
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
