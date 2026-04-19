namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Fan-out pool of <see cref="IExecutionWorker{TSession}"/> instances. Each
/// pool worker owns a private session and its own work queue; submitted work
/// items are dispatched to workers according to the configured
/// <see cref="SchedulingStrategy"/>.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public interface IExecutionWorkerPool<TSession> : IDisposable, IAsyncDisposable
    where TSession : class
{
    /// <summary>
    /// Raised when any pool worker enters a terminal faulted state. Fires once
    /// per worker, with the originating worker name in
    /// <see cref="WorkerFaultedEventArgs.WorkerName"/>.
    /// </summary>
    event EventHandler<WorkerFaultedEventArgs>? WorkerFaulted;

    /// <summary>Gets the number of workers owned by the pool.</summary>
    int WorkerCount { get; }

    /// <summary>Gets the sum of <see cref="IExecutionWorker{TSession}.QueueDepth"/> across all pool workers.</summary>
    int QueueDepth { get; }

    /// <summary>Gets a value indicating whether at least one worker in the pool has faulted.</summary>
    bool IsAnyFaulted { get; }

    /// <summary>
    /// Gets an index-aligned snapshot of each worker's fault exception, or
    /// <see langword="null"/> where the worker is healthy.
    /// </summary>
    IReadOnlyList<Exception?> WorkerFaults { get; }

    /// <summary>
    /// Gets the pool display name configured via
    /// <see cref="ExecutionWorkerPoolOptions.Name"/>, or <see langword="null"/>
    /// when unnamed.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Captures a coherent point-in-time
    /// <see cref="ExecutionWorkerPoolSnapshot"/>. Per-worker snapshots are
    /// collected sequentially in the same call so
    /// <see cref="ExecutionWorkerPoolSnapshot.QueueDepth"/> and
    /// <see cref="ExecutionWorkerPoolSnapshot.IsAnyFaulted"/> are consistent
    /// with the per-worker detail.
    /// </summary>
    /// <returns>A <see cref="ExecutionWorkerPoolSnapshot"/> describing the current state of the pool.</returns>
    ExecutionWorkerPoolSnapshot GetSnapshot();

    /// <summary>
    /// Eagerly starts every worker's dedicated thread and creates their
    /// sessions. Optional; otherwise workers start lazily on first dispatch.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Starts every worker concurrently and awaits their initialization in
    /// parallel.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels startup across all workers.</param>
    /// <returns>A task that completes when every worker is ready to accept work.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a void work item for execution on a scheduled pool worker.
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
    /// Enqueues a value-returning work item for execution on a scheduled pool
    /// worker.
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
