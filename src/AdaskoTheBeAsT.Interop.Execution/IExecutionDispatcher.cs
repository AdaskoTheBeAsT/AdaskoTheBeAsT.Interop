namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Common contract for dispatching synchronous work items to a dedicated
/// <typeparamref name="TSession"/> execution target, implemented by both
/// <see cref="IExecutionWorker{TSession}"/> and <see cref="IExecutionWorkerPool{TSession}"/>.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public interface IExecutionDispatcher<TSession> : IDisposable, IAsyncDisposable
    where TSession : class
{
    /// <summary>
    /// Raised when an underlying worker enters a terminal faulted state.
    /// Handlers are invoked on a thread-pool thread.
    /// </summary>
    event EventHandler<WorkerFaultedEventArgs>? WorkerFaulted;

    /// <summary>
    /// Gets the instantaneous number of admitted work items waiting for startup
    /// or processing (excluding any item currently executing).
    /// </summary>
    int QueueDepth { get; }

    /// <summary>
    /// Gets the configured display name, or <see langword="null"/> when unnamed.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Eagerly starts the underlying worker thread(s) and creates their session(s).
    /// Optional; otherwise workers start lazily on first dispatch.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Eagerly starts the underlying worker thread(s) and creates their session(s)
    /// asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancels this caller's wait without stopping shared startup.</param>
    /// <returns>A task that completes when the dispatcher is ready to accept work.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a void work item for execution on a worker thread.
    /// </summary>
    /// <param name="action">Synchronous callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning (<see cref="ExecutionRequestOptions.RecycleSessionOnFailure"/>).</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A task that completes after the delegate and any required session recycling.</returns>
    Task ExecuteAsync(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a value-returning work item for execution on a worker thread.
    /// </summary>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="action">Synchronous callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning (<see cref="ExecutionRequestOptions.RecycleSessionOnFailure"/>).</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>A task producing the delegate result after any required session recycling.</returns>
    Task<TResult> ExecuteAsync<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pooled <see cref="ValueTask"/> equivalent of
    /// <see cref="ExecuteAsync(Action{TSession, CancellationToken}, ExecutionRequestOptions?, CancellationToken)"/>
    /// backed by a pooled <see cref="System.Threading.Tasks.Sources.IValueTaskSource"/>.
    /// </summary>
    /// <param name="action">Synchronous callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning (<see cref="ExecutionRequestOptions.RecycleSessionOnFailure"/>).</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that completes after the delegate and any required session recycling.
    /// </returns>
    /// <remarks>
    /// The returned <see cref="ValueTask"/> MUST be observed (awaited or <c>AsTask()</c>'d)
    /// exactly once, as required by the framework spec — the underlying source is recycled
    /// on first observation.
    /// </remarks>
    ValueTask ExecuteValueAsync(
        Action<TSession, CancellationToken> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pooled <see cref="ValueTask{TResult}"/> equivalent of
    /// <see cref="ExecuteAsync{TResult}(Func{TSession, CancellationToken, TResult}, ExecutionRequestOptions?, CancellationToken)"/>
    /// backed by a pooled <see cref="System.Threading.Tasks.Sources.IValueTaskSource{TResult}"/>.
    /// </summary>
    /// <typeparam name="TResult">The result type returned by <paramref name="action"/>.</typeparam>
    /// <param name="action">Synchronous callback invoked with the session and the effective cancellation token.</param>
    /// <param name="options">Optional per-call tuning (<see cref="ExecutionRequestOptions.RecycleSessionOnFailure"/>).</param>
    /// <param name="cancellationToken">Token observed during enqueue and during execution.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> producing the delegate result after any required session recycling.
    /// </returns>
    /// <remarks>
    /// The returned <see cref="ValueTask{TResult}"/> MUST be observed (awaited or <c>AsTask()</c>'d)
    /// exactly once, as required by the framework spec — the underlying source is recycled
    /// on first observation.
    /// </remarks>
    ValueTask<TResult> ExecuteValueAsync<TResult>(
        Func<TSession, CancellationToken, TResult> action,
        ExecutionRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}
