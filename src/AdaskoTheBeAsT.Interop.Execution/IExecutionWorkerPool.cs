namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Fan-out pool of <see cref="IExecutionWorker{TSession}"/> instances. Each
/// pool worker owns a private session and its own work queue; submitted work
/// items are dispatched to workers according to the configured
/// <see cref="SchedulingStrategy"/>.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public interface IExecutionWorkerPool<TSession> : IExecutionDispatcher<TSession>
    where TSession : class
{
    /// <summary>Gets the number of workers owned by the pool.</summary>
    int WorkerCount { get; }

    /// <summary>Gets a value indicating whether at least one worker in the pool has faulted.</summary>
    bool IsAnyFaulted { get; }

    /// <summary>
    /// Gets an index-aligned snapshot of each worker's fault exception, or
    /// <see langword="null"/> where the worker is healthy.
    /// </summary>
    IReadOnlyList<Exception?> WorkerFaults { get; }

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
}
