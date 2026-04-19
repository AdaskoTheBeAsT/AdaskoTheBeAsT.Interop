namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Pluggable worker-selection policy invoked by
/// <see cref="ExecutionWorkerPool{TSession}"/> for every submitted work item.
/// Implementations may maintain per-instance scheduling state and MUST be
/// thread-safe: <see cref="SelectWorker"/> is called concurrently from every
/// submitting thread.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public interface IWorkerScheduler<TSession>
    where TSession : class
{
    /// <summary>
    /// Returns the worker that should receive the next submission.
    /// </summary>
    /// <param name="workers">Stable snapshot of the pool's workers. The list
    /// order, length, and element identities are fixed for the lifetime of the
    /// pool.</param>
    /// <returns>The selected worker. Implementations SHOULD skip
    /// <see cref="IExecutionWorker{TSession}.IsFaulted"/> workers when at least
    /// one healthy worker exists; if every worker is faulted, returning any of
    /// them is acceptable so the caller gets a deterministic exception from
    /// <c>ExecuteAsync</c> rather than silent blocking.</returns>
    IExecutionWorker<TSession> SelectWorker(IReadOnlyList<IExecutionWorker<TSession>> workers);
}
