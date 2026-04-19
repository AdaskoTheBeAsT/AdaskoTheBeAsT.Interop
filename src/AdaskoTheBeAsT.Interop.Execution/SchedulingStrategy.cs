namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Policy used by <see cref="ExecutionWorkerPool{TSession}"/> to pick which worker
/// receives the next submitted work item.
/// </summary>
public enum SchedulingStrategy
{
    /// <summary>
    /// Dispatches the work item to the worker with the lowest current queue depth.
    /// Ties are broken deterministically using a shared rolling index so workloads
    /// spread evenly across the pool. This is the default.
    /// </summary>
    LeastQueued = 0,

    /// <summary>
    /// Dispatches work items in a strict round-robin order across the pool. Faulted
    /// workers are skipped.
    /// </summary>
    RoundRobin = 1,
}
