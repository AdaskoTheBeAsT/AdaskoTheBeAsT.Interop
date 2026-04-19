using AdaskoTheBeAsT.Interop.Execution;
using Microsoft.Extensions.Hosting;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting;

/// <summary>
/// <see cref="IHostedService"/> wrapper that drives
/// <see cref="IExecutionWorker{TSession}.InitializeAsync"/> on startup and
/// <c>DisposeAsync</c> on shutdown so the worker thread drains before the host
/// exits.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
/// <remarks>
/// Initializes a new instance of the <see cref="ExecutionWorkerHostedService{TSession}"/> class.
/// </remarks>
/// <param name="worker">The worker to drive. Must not be <see langword="null"/>.</param>
/// <exception cref="ArgumentNullException"><paramref name="worker"/> is <see langword="null"/>.</exception>
public sealed class ExecutionWorkerHostedService<TSession>(IExecutionWorker<TSession> worker) : IHostedService
    where TSession : class
{
    private readonly IExecutionWorker<TSession> _worker = worker ?? throw new ArgumentNullException(nameof(worker));

    /// <summary>Starts the underlying worker.</summary>
    /// <param name="cancellationToken">Cancellation token forwarded to <c>InitializeAsync</c>.</param>
    /// <returns>A task that completes when the worker is ready to accept work.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _worker.InitializeAsync(cancellationToken);
    }

    /// <summary>Stops the underlying worker by awaiting <c>DisposeAsync</c>.</summary>
    /// <param name="cancellationToken">Currently unused; shutdown always waits for full drain.</param>
    /// <returns>A task that completes once the worker thread exits.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        // IDISP007 disabled: this hosted service intentionally drives graceful
        // shutdown of the injected worker from StopAsync so the dedicated worker
        // Thread drains before the host completes. The DI container will also
        // dispose the singleton at container teardown; DisposeAsync is idempotent
        // (guarded by Interlocked.Exchange) so the second call is a no-op.
#pragma warning disable IDISP007
        await _worker.DisposeAsync().ConfigureAwait(false);
#pragma warning restore IDISP007
    }
}
