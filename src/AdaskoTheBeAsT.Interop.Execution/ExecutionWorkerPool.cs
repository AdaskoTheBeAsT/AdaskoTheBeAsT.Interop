using System.Globalization;

namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Default <see cref="IExecutionWorkerPool{TSession}"/> implementation. Each
/// pool worker owns a dedicated <see cref="Thread"/> and a private work queue;
/// <see cref="SchedulingStrategy"/> selects which worker receives each submission.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public sealed class ExecutionWorkerPool<TSession> : IExecutionWorkerPool<TSession>
    where TSession : class
{
    private static readonly string TypeName = typeof(ExecutionWorkerPool<TSession>).Name;

    private readonly ExecutionWorker<TSession>[] _workers;
    private readonly IReadOnlyList<IExecutionWorker<TSession>> _workerView;
    private readonly IWorkerScheduler<TSession> _scheduler;
    private readonly ExecutionWorkerPoolOptions _options;
    private readonly TimeSpan _disposeTimeout;

    // _disposeLock guards the single-shot initialisation of _disposeTask. The shared-task
    // design closes a gap flagged by PR review: the sync Dispose() path must bound the wait
    // with DisposeTimeout, while the async DisposeAsync() path must await full drain — but
    // both paths must observe exactly the same in-flight operation so that a caller who
    // first times out Dispose() and then later awaits DisposeAsync() actually observes the
    // drain instead of an instant no-op. _disposeState is retained purely as the "disposal
    // has been requested" flag so new submissions are rejected regardless of which path
    // started the drain (and so MarkDisposedForTesting keeps working).
    // System.Threading.Lock on net9+ / object on older TFMs mirrors the established pattern
    // used by ExecutionWorker._syncRoot.
#if NET9_0_OR_GREATER
    private readonly Lock _disposeLock = new();
#else
    private readonly object _disposeLock = new();
#endif
    private int _disposeState;
    private Task? _disposeTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerPool{TSession}"/>
    /// class. Every pool worker shares the supplied
    /// <see cref="IExecutionSessionFactory{TSession}"/>.
    /// </summary>
    /// <param name="sessionFactory">Shared factory used by every worker to
    /// create and dispose its session. The factory must be safe to call from
    /// each worker's dedicated thread.</param>
    /// <param name="options">Pool configuration. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sessionFactory"/>
    /// or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/>
    /// violates its invariants.</exception>
    public ExecutionWorkerPool(
        IExecutionSessionFactory<TSession> sessionFactory,
        ExecutionWorkerPoolOptions options)
        : this(CreateSharedFactoryProvider(sessionFactory), options, scheduler: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerPool{TSession}"/>
    /// class with an explicit <see cref="IWorkerScheduler{TSession}"/>. Every
    /// pool worker shares the supplied
    /// <see cref="IExecutionSessionFactory{TSession}"/>.
    /// </summary>
    /// <param name="sessionFactory">Shared factory used by every worker to
    /// create and dispose its session.</param>
    /// <param name="options">Pool configuration. Must not be <see langword="null"/>.</param>
    /// <param name="scheduler">Custom scheduler used to pick a worker for each
    /// submission. When <see langword="null"/>, a built-in scheduler is
    /// resolved from <see cref="ExecutionWorkerPoolOptions.SchedulingStrategy"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sessionFactory"/>
    /// or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/>
    /// violates its invariants.</exception>
    public ExecutionWorkerPool(
        IExecutionSessionFactory<TSession> sessionFactory,
        ExecutionWorkerPoolOptions options,
        IWorkerScheduler<TSession>? scheduler)
        : this(CreateSharedFactoryProvider(sessionFactory), options, scheduler)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerPool{TSession}"/> class.
    /// </summary>
    /// <param name="sessionFactoryFactory">Callback invoked per worker (with
    /// the worker index) that returns the <see cref="IExecutionSessionFactory{TSession}"/>
    /// to use for that worker.</param>
    /// <param name="options">Pool configuration. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sessionFactoryFactory"/>
    /// or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/>
    /// violates its invariants.</exception>
    public ExecutionWorkerPool(
        Func<int, IExecutionSessionFactory<TSession>> sessionFactoryFactory,
        ExecutionWorkerPoolOptions options)
        : this(sessionFactoryFactory, options, scheduler: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerPool{TSession}"/>
    /// class with an explicit <see cref="IWorkerScheduler{TSession}"/>.
    /// </summary>
    /// <param name="sessionFactoryFactory">Callback invoked per worker (with
    /// the worker index) that returns the <see cref="IExecutionSessionFactory{TSession}"/>
    /// to use for that worker.</param>
    /// <param name="options">Pool configuration. Must not be <see langword="null"/>.</param>
    /// <param name="scheduler">Custom scheduler used to pick a worker for each
    /// submission. When <see langword="null"/>, a built-in scheduler is
    /// resolved from <see cref="ExecutionWorkerPoolOptions.SchedulingStrategy"/>
    /// (<see cref="LeastQueuedWorkerScheduler{TSession}"/> or
    /// <see cref="RoundRobinWorkerScheduler{TSession}"/>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="sessionFactoryFactory"/>
    /// or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/>
    /// violates its invariants.</exception>
    public ExecutionWorkerPool(
        Func<int, IExecutionSessionFactory<TSession>> sessionFactoryFactory,
        ExecutionWorkerPoolOptions options,
        IWorkerScheduler<TSession>? scheduler)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sessionFactoryFactory);
#else
        if (sessionFactoryFactory is null)
        {
            throw new ArgumentNullException(nameof(sessionFactoryFactory));
        }
#endif

        _ = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        _options = options;
        _workers = CreateWorkers(sessionFactoryFactory, options);
        _workerView = _workers;
        _disposeTimeout = options.DisposeTimeout;
        _scheduler = scheduler ?? ResolveBuiltInScheduler(options.SchedulingStrategy);

        foreach (var worker in _workers)
        {
            worker.WorkerFaulted += OnWorkerFaulted;
        }
    }

    /// <inheritdoc />
    public event EventHandler<WorkerFaultedEventArgs>? WorkerFaulted;

    /// <inheritdoc />
    public int WorkerCount => _workers.Length;

    /// <inheritdoc />
    public int QueueDepth
    {
        get
        {
            var total = 0;
            foreach (var worker in _workers)
            {
                total += worker.QueueDepth;
            }

            return total;
        }
    }

    /// <inheritdoc />
    public bool IsAnyFaulted
    {
        get
        {
            foreach (var worker in _workers)
            {
                if (worker.IsFaulted)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Exception?> WorkerFaults
    {
        get
        {
            var faults = new Exception?[_workers.Length];
            for (var workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
            {
                faults[workerIndex] = _workers[workerIndex].Fault;
            }

            return faults;
        }
    }

    /// <inheritdoc />
    public string? Name => _options.Name;

    /// <inheritdoc />
    public ExecutionWorkerPoolSnapshot GetSnapshot()
    {
        var workerSnapshots = new ExecutionWorkerSnapshot[_workers.Length];
        for (var workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
        {
            workerSnapshots[workerIndex] = _workers[workerIndex].GetSnapshot();
        }

        return new ExecutionWorkerPoolSnapshot(_options.Name, workerSnapshots);
    }

    /// <inheritdoc />
    public void Initialize()
    {
        ThrowIfDisposed();

        var startupTasks = StartAllWorkers(CancellationToken.None);

        try
        {
            // Safe to block here (VSTHRD002 disabled): each worker's startup Task is signaled from
            // its own dedicated worker Thread via a RunContinuationsAsynchronously TCS, so no caller
            // SynchronizationContext is captured and no sync-over-async deadlock is possible.
            // Task.WaitAll is used instead of a foreach+Wait so all worker sessions are created in
            // parallel on their respective threads, keeping pool startup time O(max(CreateSession))
            // rather than O(sum(CreateSession)).
#pragma warning disable VSTHRD002
            Task.WaitAll(startupTasks);
#pragma warning restore VSTHRD002
        }
        catch
        {
            CleanupFailedInitialization();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var startupTasks = StartAllWorkers(cancellationToken);

        try
        {
            // Parallel startup: each worker's CreateSession runs on its own dedicated thread
            // concurrently, so overall pool init time is O(max(CreateSession)) instead of
            // O(sum(CreateSession)) as the sequential foreach+await pattern would give.
            await Task.WhenAll(startupTasks).ConfigureAwait(false);
        }
        catch
        {
            CleanupFailedInitialization();
            throw;
        }
    }

    /// <inheritdoc />
    public Task ExecuteAsync(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return SelectWorker().ExecuteAsync(action, options, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteAsync<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return SelectWorker().ExecuteAsync(action, options, cancellationToken);
    }

    /// <summary>
    /// Zero-allocation hot-path equivalent of
    /// <see cref="ExecuteAsync(Action{TSession, CancellationToken}, ExecutionRequestOptions?, CancellationToken)"/>.
    /// Routes to a scheduled worker and uses the per-worker pooled
    /// <see cref="System.Threading.Tasks.Sources.IValueTaskSource"/>.
    /// </summary>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when <paramref name="action"/> finishes.</returns>
    public ValueTask ExecuteValueAsync(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return SelectConcreteWorker().ExecuteValueAsync(action, options, cancellationToken);
    }

    /// <summary>
    /// Zero-allocation hot-path equivalent of
    /// <see cref="ExecuteAsync{TResult}(Func{TSession, CancellationToken, TResult}, ExecutionRequestOptions?, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning.</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> producing the delegate result.</returns>
    public ValueTask<TResult> ExecuteValueAsync<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return SelectConcreteWorker().ExecuteValueAsync(action, options, cancellationToken);
    }

    /// <summary>
    /// Asynchronously disposes every worker in parallel. Idempotent; every call awaits the
    /// same in-flight drain created on the first invocation (via <see cref="Dispose"/> or
    /// <see cref="DisposeAsync"/>), so callers are guaranteed to observe drain completion —
    /// including the case where a prior synchronous <see cref="Dispose"/> returned early on
    /// <see cref="ExecutionWorkerPoolOptions.DisposeTimeout"/>.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes once every worker thread exits.</returns>
    public ValueTask DisposeAsync()
    {
        return new ValueTask(EnsureDisposeStartedAsync());
    }

    /// <summary>
    /// Synchronous disposal bounded by
    /// <see cref="ExecutionWorkerPoolOptions.DisposeTimeout"/>. Prefer
    /// <see cref="DisposeAsync"/> whenever practical. A timeout abandons the wait but does
    /// NOT cancel the underlying drain; a later <see cref="DisposeAsync"/> call will await
    /// the same Task and observe full completion.
    /// </summary>
    public void Dispose()
    {
        var disposeTask = EnsureDisposeStartedAsync();
        var timeout = _disposeTimeout;

        // Safe sync-over-async (VSTHRD002 disabled): the shared _disposeTask awaits each
        // worker's DisposeAsync, which in turn awaits a RunContinuationsAsynchronously TCS
        // signaled from its dedicated worker Thread. No caller SynchronizationContext is
        // captured, so GetAwaiter().GetResult() cannot deadlock. Task.Wait(timeout) bounds
        // the wait by ExecutionWorkerPoolOptions.DisposeTimeout (default
        // Timeout.InfiniteTimeSpan); on completion GetAwaiter().GetResult() rethrows any
        // DisposeAsync fault unwrapped instead of wrapped in AggregateException.
#pragma warning disable VSTHRD002
        bool completed;
        try
        {
            completed = disposeTask.Wait(timeout);
        }
        catch (AggregateException)
        {
            // Task.Wait throws AggregateException on faulted completion within the
            // timeout. Fall through so GetAwaiter().GetResult() rethrows the original
            // exception unwrapped.
            completed = true;
        }

        if (!completed)
        {
            // Timeout guard: abandon the wait rather than block the caller indefinitely
            // if one or more workers fail to drain within DisposeTimeout. The background
            // drain continues; a later await on DisposeAsync() observes the same Task.
            return;
        }

        disposeTask.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    internal void MarkDisposedForTesting()
    {
        Interlocked.Exchange(ref _disposeState, 1);
    }

    internal void SetWorkerThreadForTesting(int workerIndex, Thread workerThread)
    {
        _ = workerThread ?? throw new ArgumentNullException(nameof(workerThread));
        _workers[workerIndex].SetWorkerThreadForTesting(workerThread);
    }

    private static Func<int, IExecutionSessionFactory<TSession>> CreateSharedFactoryProvider(
        IExecutionSessionFactory<TSession> sessionFactory)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sessionFactory);
#else
        if (sessionFactory is null)
        {
            throw new ArgumentNullException(nameof(sessionFactory));
        }
#endif

        var sharedFactory = sessionFactory;
        return _ => sharedFactory;
    }

    private static ExecutionWorker<TSession>[] CreateWorkers(
        Func<int, IExecutionSessionFactory<TSession>> sessionFactoryFactory,
        ExecutionWorkerPoolOptions options)
    {
        var factoryProvider = sessionFactoryFactory;
        var workers = new ExecutionWorker<TSession>[options.WorkerCount];

        for (var workerIndex = 0; workerIndex < workers.Length; workerIndex++)
        {
            var sessionFactory = factoryProvider(workerIndex)
                ?? throw new InvalidOperationException("The session factory factory returned null.");

            workers[workerIndex] = new ExecutionWorker<TSession>(
                sessionFactory,
                new ExecutionWorkerOptions(
                    CreateWorkerName(options.Name, workerIndex),
                    options.UseStaThread,
                    options.MaxOperationsPerSession,
                    options.DisposeTimeout,
                    options.Diagnostics));
        }

        return workers;
    }

    private static string? CreateWorkerName(string? poolName, int workerIndex)
    {
        if (string.IsNullOrWhiteSpace(poolName))
        {
            return null;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0} #{1}", poolName, workerIndex + 1);
    }

    private static IWorkerScheduler<TSession> ResolveBuiltInScheduler(SchedulingStrategy strategy)
    {
        return strategy switch
        {
            SchedulingStrategy.RoundRobin => new RoundRobinWorkerScheduler<TSession>(),
            _ => new LeastQueuedWorkerScheduler<TSession>(),
        };
    }

    private Task EnsureDisposeStartedAsync()
    {
        var existing = Volatile.Read(ref _disposeTask);
        if (existing is not null)
        {
            return existing;
        }

        lock (_disposeLock)
        {
            existing = _disposeTask;
            if (existing is null)
            {
                // Flag "disposal requested" BEFORE kicking off the drain so ThrowIfDisposed
                // / submission-path checks immediately start rejecting new work, even before
                // the first await inside DisposeCoreAsync runs.
                Volatile.Write(ref _disposeState, 1);
                existing = DisposeCoreAsync();
                Volatile.Write(ref _disposeTask, existing);
            }
        }

        return existing;
    }

    private Task DisposeCoreAsync()
    {
        // Dispose workers in parallel: each worker's DisposeAsync completes independently
        // once its dedicated thread finishes draining. Returning Task.WhenAll directly
        // keeps overall pool shutdown time O(max(worker drain)) instead of
        // O(sum(worker drain)) and avoids an extra async state-machine allocation
        // (AsyncFixer01 — only a single expression is awaited).
        var disposeTasks = new Task[_workers.Length];
        for (var workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
        {
            var worker = _workers[workerIndex];
            worker.WorkerFaulted -= OnWorkerFaulted;
            disposeTasks[workerIndex] = worker.DisposeAsync().AsTask();
        }

        return Task.WhenAll(disposeTasks);
    }

    private Task[] StartAllWorkers(CancellationToken cancellationToken)
    {
        var startupTasks = new Task[_workers.Length];
        for (var workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
        {
            startupTasks[workerIndex] = _workers[workerIndex].InitializeAsync(cancellationToken);
        }

        return startupTasks;
    }

    private IExecutionWorker<TSession> SelectWorker()
    {
        ThrowIfDisposed();

        var selected = _scheduler.SelectWorker(_workerView)
            ?? throw new InvalidOperationException("The worker scheduler returned null.");
        return selected;
    }

    private ExecutionWorker<TSession> SelectConcreteWorker()
    {
        ThrowIfDisposed();

        var selected = _scheduler.SelectWorker(_workerView)
            ?? throw new InvalidOperationException("The worker scheduler returned null.");

        // Built-in schedulers always return one of the pool's own workers.
        // Custom schedulers are contractually required to do the same — if a
        // caller wires a scheduler that returns a foreign IExecutionWorker,
        // the zero-alloc ExecuteValueAsync hot path cannot dispatch through
        // it because the IValueTaskSource pool is keyed to ExecutionWorker<T>,
        // so we fail loudly rather than fall back to a slower Task wrapper.
        if (selected is ExecutionWorker<TSession> concrete)
        {
            return concrete;
        }

        throw new InvalidOperationException(
            "The worker scheduler returned a worker that is not owned by this pool.");
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

    private void CleanupFailedInitialization()
    {
        Interlocked.Exchange(ref _disposeState, 1);

        foreach (var worker in _workers)
        {
            worker.WorkerFaulted -= OnWorkerFaulted;
            ExecutionHelpers.TryIgnore(worker.Dispose);
        }
    }

    private void OnWorkerFaulted(object? sender, WorkerFaultedEventArgs e)
    {
        var handler = WorkerFaulted;
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            var typedSubscriber = (EventHandler<WorkerFaultedEventArgs>)subscriber;
            ExecutionHelpers.TryIgnore(() => typedSubscriber(this, e));
        }
    }
}
