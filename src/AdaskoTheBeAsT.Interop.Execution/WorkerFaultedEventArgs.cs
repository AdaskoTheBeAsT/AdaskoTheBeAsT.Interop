namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Event payload published by <see cref="IExecutionWorker{TSession}.WorkerFaulted"/>
/// and <see cref="IExecutionWorkerPool{TSession}.WorkerFaulted"/> when a worker
/// enters a terminal faulted state (session creation/dispose failure or worker
/// thread crash).
/// </summary>
public sealed class WorkerFaultedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerFaultedEventArgs"/> class.
    /// </summary>
    /// <param name="exception">The exception that caused the worker to fault.</param>
    /// <param name="workerName">Optional worker name copied from
    /// <see cref="ExecutionWorkerOptions.Name"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public WorkerFaultedEventArgs(Exception exception, string? workerName)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        WorkerName = workerName;
        FaultedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the exception that caused the worker to fault.</summary>
    public Exception Exception { get; }

    /// <summary>Gets the worker name, if configured on the originating options.</summary>
    public string? WorkerName { get; }

    /// <summary>Gets the UTC timestamp captured when the fault was observed.</summary>
    public DateTimeOffset FaultedAtUtc { get; }
}
