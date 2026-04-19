namespace AdaskoTheBeAsT.Interop.Execution;

#pragma warning disable CA1815, S1210, MA0102

/// <summary>
/// Immutable point-in-time view of a single
/// <see cref="IExecutionWorker{TSession}"/>. Produced by
/// <see cref="IExecutionWorker{TSession}.GetSnapshot"/>; intended for
/// health-check endpoints, dashboards, and aggregate pool reporting where a
/// coherent snapshot is preferable to several racing property reads.
/// </summary>
/// <remarks>
/// <para>
/// Each field is captured in the same call, so the reader never observes a
/// mix of values from two different moments (for example <c>QueueDepth</c>
/// from before a submission together with <c>IsFaulted</c> from after it).
/// </para>
/// <para>
/// Initializes a new instance of the <see cref="ExecutionWorkerSnapshot"/> struct.
/// </para>
/// </remarks>
/// <param name="name">Worker display name, or <see langword="null"/> when unnamed.</param>
/// <param name="queueDepth">Instantaneous queue depth at the time of capture.</param>
/// <param name="isFaulted">Whether the worker had faulted at the time of capture.</param>
/// <param name="fault">The fault exception, or <see langword="null"/> when healthy.</param>
public readonly struct ExecutionWorkerSnapshot(
    string? name,
    int queueDepth,
    bool isFaulted,
    Exception? fault)
{
    /// <summary>Gets the worker display name, or <see langword="null"/> when unnamed.</summary>
    public string? Name { get; } = name;

    /// <summary>Gets the instantaneous queue depth captured with this snapshot.</summary>
    public int QueueDepth { get; } = queueDepth;

    /// <summary>Gets a value indicating whether the worker had faulted at capture time.</summary>
    public bool IsFaulted { get; } = isFaulted;

    /// <summary>Gets the fault exception captured with this snapshot, or <see langword="null"/> when healthy.</summary>
    public Exception? Fault { get; } = fault;
}
#pragma warning restore CA1815, S1210, MA0102
