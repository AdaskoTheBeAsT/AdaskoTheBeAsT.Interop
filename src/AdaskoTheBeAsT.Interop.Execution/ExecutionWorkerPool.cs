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

        try
        {
            foreach (var worker in _workers)
            {
                worker.Initialize();
            }
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
