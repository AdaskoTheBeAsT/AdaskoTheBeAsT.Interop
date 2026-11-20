namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Immutable per-session metadata passed to
/// <see cref="IContextualExecutionSessionFactory{TSession}"/> during both
/// session creation and session disposal.
/// </summary>
/// <remarks>
/// Both ends of a single session's lifetime observe the same
/// <see cref="WorkerIndex"/>, <see cref="WorkerCount"/>,
/// <see cref="WorkerName"/>, <see cref="SessionGeneration"/>, and
/// <see cref="PreviousRecycleReason"/> on the owning dedicated worker thread.
/// <see cref="SessionGeneration"/> starts at <c>0</c> per worker slot and
/// increments only after a session has been successfully created.
/// </remarks>
public readonly record struct SessionLifecycleContext
{
    /// <summary>
    /// Gets the zero-based worker slot index (<c>0</c> for standalone workers,
    /// <c>0..WorkerCount-1</c> for pool workers).
    /// </summary>
    public int WorkerIndex { get; init; }

    /// <summary>
    /// Gets the total number of worker slots (<c>1</c> for standalone workers,
    /// <see cref="ExecutionWorkerPoolOptions.WorkerCount"/> for pool workers).
    /// </summary>
    public int WorkerCount { get; init; }

    /// <summary>
    /// Gets the configured worker display name, or <see langword="null"/> when unnamed.
    /// </summary>
    public string? WorkerName { get; init; }

    /// <summary>
    /// Gets the zero-based generation counter of this session within its worker slot.
    /// </summary>
    public long SessionGeneration { get; init; }

    /// <summary>
    /// Gets the reason the immediately preceding session in this slot was
    /// recycled, or <see langword="null"/> for generation <c>0</c>.
    /// </summary>
    public SessionRecycleReason? PreviousRecycleReason { get; init; }

    /// <summary>
    /// Gets the <see cref="Thread.ManagedThreadId"/> of the dedicated worker
    /// thread invoking the factory.
    /// </summary>
    public int ManagedThreadId { get; init; }
}
