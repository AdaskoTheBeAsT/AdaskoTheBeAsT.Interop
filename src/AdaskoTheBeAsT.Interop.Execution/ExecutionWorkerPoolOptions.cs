namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Tuning surface for <see cref="ExecutionWorkerPool{TSession}"/>. Bindable via
/// <c>IOptions&lt;ExecutionWorkerPoolOptions&gt;</c> while retaining a
/// positional constructor for plain <see langword="new"/> usage.
/// </summary>
public sealed class ExecutionWorkerPoolOptions
{
    /// <summary>
    /// Initializes a new instance with default values. Intended for
    /// <c>Microsoft.Extensions.Options</c>-style configuration where properties
    /// are assigned after construction.
    /// </summary>
    public ExecutionWorkerPoolOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance with explicit values. The mandatory worker
    /// count is the only positional parameter; the rest are optional.
    /// </summary>
    /// <param name="workerCount">Number of dedicated worker threads. Must be positive.</param>
    /// <param name="name">Optional pool name propagated to per-worker telemetry.</param>
    /// <param name="useStaThread">When <see langword="true"/> and the current
    /// OS is Windows, every worker thread is marked
    /// <see cref="System.Threading.ApartmentState.STA"/>.</param>
    /// <param name="maxOperationsPerSession">Session is recycled after this
    /// many operations. <c>0</c> (default) means unlimited.</param>
    /// <param name="disposeTimeout">Upper bound applied by the synchronous
    /// <see cref="ExecutionWorkerPool{TSession}.Dispose"/>. Defaults to
    /// <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="schedulingStrategy">Policy used to pick which worker
    /// receives the next submitted work item when no explicit
    /// <see cref="IWorkerScheduler{TSession}"/> is supplied to the pool
    /// constructor.</param>
    /// <param name="diagnostics">Optional scope propagated to every pool
    /// worker. When <see langword="null"/> (default),
    /// <see cref="ExecutionDiagnostics.Shared"/> is used.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument violates the
    /// options invariants enforced by <see cref="Validate"/>.</exception>
    public ExecutionWorkerPoolOptions(
        int workerCount,
        string? name = null,
        bool useStaThread = false,
        int maxOperationsPerSession = 0,
        TimeSpan? disposeTimeout = null,
        SchedulingStrategy schedulingStrategy = SchedulingStrategy.LeastQueued,
        ExecutionDiagnostics? diagnostics = null)
    {
        WorkerCount = workerCount;
        Name = name;
        UseStaThread = useStaThread;
        MaxOperationsPerSession = maxOperationsPerSession;
        DisposeTimeout = disposeTimeout ?? Timeout.InfiniteTimeSpan;
        SchedulingStrategy = schedulingStrategy;
        Diagnostics = diagnostics;
        Validate();
    }

    /// <summary>Gets or sets the number of dedicated worker threads. Must be positive.</summary>
    public int WorkerCount { get; set; } = 1;

    /// <summary>Gets or sets the optional pool name propagated to per-worker telemetry.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every worker thread is marked
    /// <see cref="System.Threading.ApartmentState.STA"/>. Honoured only on
    /// Windows.
    /// </summary>
    public bool UseStaThread { get; set; }

    /// <summary>
    /// Gets or sets the number of operations after which each worker's session
    /// is recycled. <c>0</c> means unlimited; must not be negative.
    /// </summary>
    public int MaxOperationsPerSession { get; set; }

    /// <summary>
    /// Gets or sets the bound applied by synchronous <c>Dispose</c>. Defaults
    /// to <see cref="Timeout.InfiniteTimeSpan"/>. Async <c>DisposeAsync</c>
    /// always waits for full drain.
    /// </summary>
    public TimeSpan DisposeTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>Gets or sets the scheduling policy used to pick which worker receives the next submitted work item.</summary>
    public SchedulingStrategy SchedulingStrategy { get; set; } = SchedulingStrategy.LeastQueued;

    /// <summary>
    /// Gets or sets the diagnostics scope propagated to every pool worker.
    /// When <see langword="null"/> (the default) every worker uses
    /// <see cref="ExecutionDiagnostics.Shared"/>. Supply a custom scope to
    /// isolate the pool's telemetry from the rest of the process.
    /// </summary>
    public ExecutionDiagnostics? Diagnostics { get; set; }

    // S3928 / MA0015 / S3236 disabled: Validate() validates the instance's
    // public properties (it has no parameters). The paramName argument surfaces
    // the offending property to callers, matching the ArgumentOutOfRangeException
    // convention applied by the positional ctor so both initialisation paths
    // behave identically.
#pragma warning disable S3928, MA0015, S3236
    internal void Validate()
    {
        if (WorkerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(WorkerCount));
        }

        if (MaxOperationsPerSession < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOperationsPerSession));
        }

        if (DisposeTimeout < TimeSpan.Zero && DisposeTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(DisposeTimeout));
        }

        if (!Enum.IsDefined(typeof(SchedulingStrategy), SchedulingStrategy))
        {
            throw new ArgumentOutOfRangeException(nameof(SchedulingStrategy));
        }
    }
#pragma warning restore S3928, MA0015, S3236
}
