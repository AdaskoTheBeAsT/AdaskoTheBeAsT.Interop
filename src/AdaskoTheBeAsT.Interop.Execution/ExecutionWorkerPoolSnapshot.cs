namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Immutable point-in-time view of an
/// <see cref="IExecutionWorkerPool{TSession}"/>. Produced by
/// <see cref="IExecutionWorkerPool{TSession}.GetSnapshot"/>; pairs aggregate
/// counters with an index-aligned array of per-worker
/// <see cref="ExecutionWorkerSnapshot"/> values so a single reader observes a
/// coherent view of the whole pool.
/// </summary>
/// <remarks>
/// Aggregate fields (<see cref="QueueDepth"/>, <see cref="IsAnyFaulted"/>) are
/// derived from the same per-worker snapshots exposed by
/// <see cref="Workers"/>, so the aggregate totals cannot drift out of sync
/// with the per-worker view.
/// </remarks>
#pragma warning disable CA1815, S1210, MA0102
public readonly struct ExecutionWorkerPoolSnapshot
{
    private static readonly IReadOnlyList<ExecutionWorkerSnapshot> EmptyWorkers = Array.Empty<ExecutionWorkerSnapshot>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerPoolSnapshot"/> struct.
    /// </summary>
    /// <param name="name">Pool display name, or <see langword="null"/> when unnamed.</param>
    /// <param name="workers">Index-aligned per-worker snapshots.</param>
    public ExecutionWorkerPoolSnapshot(
        string? name,
        IReadOnlyList<ExecutionWorkerSnapshot> workers)
    {
        Name = name;
        Workers = workers ?? EmptyWorkers;

        var totalQueueDepth = 0;
        var anyFaulted = false;
        for (var workerIndex = 0; workerIndex < Workers.Count; workerIndex++)
        {
            var workerSnapshot = Workers[workerIndex];
            totalQueueDepth += workerSnapshot.QueueDepth;
            if (workerSnapshot.IsFaulted)
            {
                anyFaulted = true;
            }
        }

        QueueDepth = totalQueueDepth;
        IsAnyFaulted = anyFaulted;
    }

    /// <summary>Gets the pool display name, or <see langword="null"/> when unnamed.</summary>
    public string? Name { get; }

    /// <summary>Gets the number of workers owned by the pool.</summary>
    public int WorkerCount => Workers.Count;

    /// <summary>Gets the sum of <see cref="ExecutionWorkerSnapshot.QueueDepth"/> across all workers.</summary>
    public int QueueDepth { get; }

    /// <summary>Gets a value indicating whether any worker in the pool had faulted at capture time.</summary>
    public bool IsAnyFaulted { get; }

    /// <summary>Gets index-aligned per-worker snapshots captured atomically with the aggregate counters.</summary>
    public IReadOnlyList<ExecutionWorkerSnapshot> Workers { get; }
}
#pragma warning restore CA1815, S1210, MA0102
