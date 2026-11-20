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
    private static readonly IReadOnlyList<ExecutionWorkerSnapshot> EmptyWorkers = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerPoolSnapshot"/> struct.
    /// </summary>
    /// <param name="name">Pool display name, or <see langword="null"/> when unnamed.</param>
    /// <param name="workers">Index-aligned per-worker snapshots.</param>
    public ExecutionWorkerPoolSnapshot(
        string? name,
        IReadOnlyList<ExecutionWorkerSnapshot> workers)
        : this(name, workers, isDisposed: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerPoolSnapshot"/> struct
    /// including pool disposal state.
    /// </summary>
    /// <param name="name">Pool display name, or <see langword="null"/> when unnamed.</param>
    /// <param name="workers">Index-aligned per-worker snapshots.</param>
    /// <param name="isDisposed">Whether disposal has been requested on the pool.</param>
    public ExecutionWorkerPoolSnapshot(
        string? name,
        IReadOnlyList<ExecutionWorkerSnapshot> workers,
        bool isDisposed)
    {
        Name = name;
        Workers = workers ?? EmptyWorkers;

        var totalQueueDepth = 0;
        var initializedCount = 0;
        var faultedCount = 0;
        var anyDisposed = isDisposed;
        for (var workerIndex = 0; workerIndex < Workers.Count; workerIndex++)
        {
            var workerSnapshot = Workers[workerIndex];
            totalQueueDepth += workerSnapshot.QueueDepth;
            if (workerSnapshot.IsInitialized)
            {
                initializedCount++;
            }

            if (workerSnapshot.IsFaulted)
            {
                faultedCount++;
            }

            if (workerSnapshot.IsDisposed)
            {
                anyDisposed = true;
            }
        }

        QueueDepth = totalQueueDepth;
        InitializedWorkerCount = initializedCount;
        FaultedWorkerCount = faultedCount;
        IsAnyFaulted = faultedCount > 0;
        IsDisposed = anyDisposed;
    }

    /// <summary>Gets the pool display name, or <see langword="null"/> when unnamed.</summary>
    public string? Name { get; }

    /// <summary>Gets the number of workers owned by the pool.</summary>
    public int WorkerCount => Workers.Count;

    /// <summary>Gets the sum of <see cref="ExecutionWorkerSnapshot.QueueDepth"/> across all workers.</summary>
    public int QueueDepth { get; }

    /// <summary>Gets the number of workers whose initial session startup completed.</summary>
    public int InitializedWorkerCount { get; }

    /// <summary>Gets the number of workers in a terminal faulted state at capture time.</summary>
    public int FaultedWorkerCount { get; }

    /// <summary>Gets a value indicating whether any worker in the pool had faulted at capture time.</summary>
    public bool IsAnyFaulted { get; }

    /// <summary>
    /// Gets a value indicating whether every worker in the pool had completed
    /// initial session startup at capture time.
    /// </summary>
    public bool IsInitialized => Workers.Count > 0 && InitializedWorkerCount == Workers.Count;

    /// <summary>
    /// Gets a value indicating whether disposal had been requested on the pool
    /// (or any owned worker) at capture time.
    /// </summary>
    public bool IsDisposed { get; }

    /// <summary>Gets index-aligned per-worker snapshots captured atomically with the aggregate counters.</summary>
    public IReadOnlyList<ExecutionWorkerSnapshot> Workers { get; }
}
#pragma warning restore CA1815, S1210, MA0102
