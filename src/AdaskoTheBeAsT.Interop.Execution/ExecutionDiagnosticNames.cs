namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Stable, public diagnostic identifiers emitted by
/// <see cref="ExecutionWorker{TSession}"/> and
/// <see cref="ExecutionWorkerPool{TSession}"/> via
/// <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Telemetry consumers (OpenTelemetry collectors, custom <c>ActivityListener</c>s,
/// <c>MeterListener</c>s, log enrichers) should reference these constants
/// instead of hard-coding strings so that forward-compatible renames remain
/// source-compatible. Values are immutable across a major version.
/// </para>
/// </remarks>
public static class ExecutionDiagnosticNames
{
    /// <summary>
    /// Gets the shared <see cref="System.Diagnostics.ActivitySource.Name"/> /
    /// <see cref="System.Diagnostics.Metrics.Meter.Name"/> used by the
    /// execution worker infrastructure. Subscribe to this name to receive
    /// activities and metrics from <see cref="ExecutionWorker{TSession}"/>
    /// and <see cref="ExecutionWorkerPool{TSession}"/>.
    /// </summary>
    public const string SourceName = "AdaskoTheBeAsT.Interop.Execution";

    /// <summary>Activity / span name for a single work item execution.</summary>
    public const string ActivityExecute = "ExecutionWorker.Execute";

    /// <summary>Metric name for the counter tracking processed work items.</summary>
    public const string MetricOperations = "execution.worker.operations";

    /// <summary>Metric name for the counter tracking session recycles.</summary>
    public const string MetricSessionRecycles = "execution.worker.session_recycles";

    /// <summary>Metric name for the observable gauge reporting per-worker queue depth.</summary>
    public const string MetricQueueDepth = "execution.worker.queue_depth";

    /// <summary>Tag key carrying the worker name on activities and metrics.</summary>
    public const string TagWorkerName = "worker.name";

    /// <summary>Tag key carrying the operation outcome on the operations counter.</summary>
    public const string TagOutcome = "outcome";

    /// <summary>Tag key carrying the reason on the session-recycles counter.</summary>
    public const string TagRecycleReason = "reason";

    /// <summary>
    /// Outcome tag value indicating the work item completed without throwing.
    /// </summary>
    public const string OutcomeSuccess = "success";

    /// <summary>
    /// Outcome tag value indicating the work item threw an unhandled exception
    /// (other than a requested cancellation).
    /// </summary>
    public const string OutcomeFaulted = "faulted";

    /// <summary>
    /// Outcome tag value indicating the work item observed its caller's
    /// cancellation token before or during execution.
    /// </summary>
    public const string OutcomeCancelled = "cancelled";

    /// <summary>
    /// Recycle-reason tag value indicating the session was replaced because
    /// <see cref="ExecutionWorkerOptions.MaxOperationsPerSession"/> was reached.
    /// </summary>
    public const string RecycleMaxOperations = "max_operations";

    /// <summary>
    /// Recycle-reason tag value indicating the session was replaced because a
    /// work item with
    /// <see cref="ExecutionRequestOptions.RecycleSessionOnFailure"/>
    /// threw an exception.
    /// </summary>
    public const string RecycleFailure = "failure";
}
