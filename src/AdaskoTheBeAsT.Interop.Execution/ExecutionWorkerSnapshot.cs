namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Immutable point-in-time view of a single
/// <see cref="IExecutionWorker{TSession}"/>. Produced by
/// <see cref="IExecutionWorker{TSession}.GetSnapshot"/>; intended for
/// health-check endpoints, dashboards, and aggregate pool reporting where a
/// coherent snapshot is preferable to several racing property reads.
/// </summary>
/// <remarks>
/// Each field is captured in the same call, so the reader never observes a
/// mix of values from two different moments (for example <c>QueueDepth</c>
/// from before a submission together with <c>IsFaulted</c> from after it).
/// </remarks>
#pragma warning disable CA1815, S1210, MA0102
public readonly struct ExecutionWorkerSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerSnapshot"/> struct.
    /// </summary>
    /// <param name="name">Worker display name, or <see langword="null"/> when unnamed.</param>
    /// <param name="queueDepth">Instantaneous queue depth at the time of capture.</param>
    /// <param name="isFaulted">Whether the worker had faulted at the time of capture.</param>
    /// <param name="fault">The fault exception, or <see langword="null"/> when healthy.</param>
    public ExecutionWorkerSnapshot(
        string? name,
        int queueDepth,
        bool isFaulted,
        Exception? fault)
    {
        Name = name;
        QueueDepth = queueDepth;
        IsFaulted = isFaulted;
        Fault = fault;
    }

    /// <summary>Gets the worker display name, or <see langword="null"/> when unnamed.</summary>
    public string? Name { get; }

    /// <summary>Gets the instantaneous queue depth captured with this snapshot.</summary>
    public int QueueDepth { get; }

    /// <summary>Gets a value indicating whether the worker had faulted at capture time.</summary>
    public bool IsFaulted { get; }

    /// <summary>Gets the fault exception captured with this snapshot, or <see langword="null"/> when healthy.</summary>
    public Exception? Fault { get; }
}
#pragma warning restore CA1815, S1210, MA0102
