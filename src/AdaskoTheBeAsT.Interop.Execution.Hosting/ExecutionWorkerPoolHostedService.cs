using AdaskoTheBeAsT.Interop.Execution;
using Microsoft.Extensions.Hosting;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting;

/// <summary>
/// <see cref="IHostedService"/> wrapper that drives
/// <see cref="IExecutionWorkerPool{TSession}.InitializeAsync"/> on startup and
/// <c>DisposeAsync</c> on shutdown so every pool worker drains before the host
/// exits.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public sealed class ExecutionWorkerPoolHostedService<TSession> : IHostedService
    where TSession : class
{
    private readonly IExecutionWorkerPool<TSession> _pool;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerPoolHostedService{TSession}"/> class.
    /// </summary>
    /// <param name="pool">The pool to drive. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    public ExecutionWorkerPoolHostedService(IExecutionWorkerPool<TSession> pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <summary>Starts every worker in the pool concurrently.</summary>
    /// <param name="cancellationToken">Cancellation token forwarded to <c>InitializeAsync</c>.</param>
    /// <returns>A task that completes when every worker is ready to accept work.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _pool.InitializeAsync(cancellationToken);
    }

    /// <summary>Stops every worker by awaiting pool <c>DisposeAsync</c>.</summary>
    /// <param name="cancellationToken">Currently unused; shutdown always waits for full drain.</param>
    /// <returns>A task that completes once every worker thread exits.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        // IDISP007 disabled: this hosted service intentionally drives graceful
        // shutdown of the injected pool from StopAsync so every worker Thread
        // drains before the host completes. The DI container will also dispose
        // the singleton at container teardown; DisposeAsync is idempotent
        // (guarded by Interlocked.Exchange) so the second call is a no-op.
#pragma warning disable IDISP007
        await _pool.DisposeAsync().ConfigureAwait(false);
#pragma warning restore IDISP007
    }
}
