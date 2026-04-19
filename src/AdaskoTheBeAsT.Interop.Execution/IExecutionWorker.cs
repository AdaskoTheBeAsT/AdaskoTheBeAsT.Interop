namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Serializes work items onto a single dedicated <see cref="System.Threading.Thread"/>
/// that owns exclusive access to a <typeparamref name="TSession"/> instance.
/// Designed for wrapping STA COM/native sessions safely from async code across
/// .NET Framework and modern .NET.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public interface IExecutionWorker<TSession> : IDisposable, IAsyncDisposable
    where TSession : class
{
    /// <summary>
    /// Raised once when the worker enters a terminal faulted state. Handlers
    /// are invoked on a thread-pool thread.
    /// </summary>
    event EventHandler<WorkerFaultedEventArgs>? WorkerFaulted;

    /// <summary>
    /// Gets a value indicating whether the worker has faulted. Once
    /// <see langword="true"/>, all further <c>ExecuteAsync</c> calls throw the
    /// fault exception synchronously.
    /// </summary>
    bool IsFaulted { get; }

    /// <summary>Gets the fault exception, or <see langword="null"/> if not faulted.</summary>
    Exception? Fault { get; }

    /// <summary>
    /// Gets the instantaneous number of work items queued for processing
    /// (excluding the item currently executing).
    /// </summary>
    int QueueDepth { get; }

    /// <summary>
    /// Gets the worker display name configured via
    /// <see cref="ExecutionWorkerOptions.Name"/>, or <see langword="null"/>
    /// when unnamed.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Captures a coherent point-in-time <see cref="ExecutionWorkerSnapshot"/>
    /// of this worker. All fields on the returned snapshot are read within the
    /// same call so dashboards and health checks observe a consistent view
    /// across <see cref="QueueDepth"/>, <see cref="IsFaulted"/>, and
    /// <see cref="Fault"/>.
    /// </summary>
    /// <returns>A <see cref="ExecutionWorkerSnapshot"/> describing the current state of the worker.</returns>
    ExecutionWorkerSnapshot GetSnapshot();

    /// <summary>
    /// Eagerly starts the dedicated worker thread and creates the session.
    /// Optional; otherwise the worker starts lazily on first <c>ExecuteAsync</c>.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Eagerly starts the dedicated worker thread and creates the session
    /// asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the startup.</param>
    /// <returns>A task that completes when the worker is ready to accept work.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a void work item for execution on the worker thread.
    /// </summary>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning (<see cref="ExecutionRequestOptions.RecycleSessionOnFailure"/>).</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A task that completes when <paramref name="action"/> finishes.</returns>
    Task ExecuteAsync(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a value-returning work item for execution on the worker thread.
    /// </summary>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="action">Callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning (<see cref="ExecutionRequestOptions.RecycleSessionOnFailure"/>).</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A task that produces the value returned by <paramref name="action"/>.</returns>
    Task<TResult> ExecuteAsync<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}
