using System.Globalization;

namespace AdaskoTheBeAsT.Interop.Execution;

public sealed class ExecutionWorkerPool<TSession> : IDisposable
    where TSession : class
{
    private readonly ExecutionWorker<TSession>[] _workers;
    private int _disposeState;
    private int _nextWorkerIndex = -1;

    public ExecutionWorkerPool(
        Func<int, IExecutionSessionFactory<TSession>> sessionFactoryFactory,
        ExecutionWorkerPoolOptions options)
    {
        if (sessionFactoryFactory is null)
        {
            throw new ArgumentNullException(nameof(sessionFactoryFactory));
        }

        _ = options ?? throw new ArgumentNullException(nameof(options));
        _workers = CreateWorkers(sessionFactoryFactory, options);
    }

    public int WorkerCount => _workers.Length;

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

    public Task ExecuteAsync(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return SelectWorker().ExecuteAsync(action, options, cancellationToken);
    }

    public Task<TResult> ExecuteAsync<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return SelectWorker().ExecuteAsync(action, options, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        foreach (var worker in _workers)
        {
            worker.Dispose();
        }
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
                    options.MaxOperationsPerSession));
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

    private Task[] StartAllWorkers(CancellationToken cancellationToken)
    {
        var startupTasks = new Task[_workers.Length];
        for (var workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
        {
            startupTasks[workerIndex] = _workers[workerIndex].InitializeAsync(cancellationToken);
        }

        return startupTasks;
    }

    private ExecutionWorker<TSession> SelectWorker()
    {
        ThrowIfDisposed();

        var workerIndex = unchecked(Interlocked.Increment(ref _nextWorkerIndex) & int.MaxValue);
        return _workers[workerIndex % _workers.Length];
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(ExecutionWorkerPool<>));
        }
    }

    private void CleanupFailedInitialization()
    {
        Interlocked.Exchange(ref _disposeState, 1);

        foreach (var worker in _workers)
        {
            ExecutionHelpers.TryIgnore(worker.Dispose);
        }
    }
}
