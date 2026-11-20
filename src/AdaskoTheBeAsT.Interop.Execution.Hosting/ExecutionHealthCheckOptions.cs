namespace AdaskoTheBeAsT.Interop.Execution.Hosting;

/// <summary>
/// Configuration options for <see cref="ExecutionWorkerHealthCheck{TSession}"/>
/// and <see cref="ExecutionWorkerPoolHealthCheck{TSession}"/>.
/// </summary>
public sealed class ExecutionHealthCheckOptions
{
    /// <summary>
    /// Gets a fresh default <see cref="ExecutionHealthCheckOptions"/> instance.
    /// </summary>
    public static ExecutionHealthCheckOptions Default => new();

    /// <summary>
    /// Gets or sets an optional queue-depth warning threshold. When configured,
    /// health degrades to
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>
    /// whenever the snapshot <c>QueueDepth</c> strictly exceeds this value.
    /// Must not be negative.
    /// </summary>
    public int? QueueDepthWarningThreshold { get; set; }

    internal ExecutionHealthCheckOptions Snapshot() =>
        new()
        {
            QueueDepthWarningThreshold = QueueDepthWarningThreshold,
        };

#pragma warning disable S3928, MA0015, S3236
    internal void Validate()
    {
        if (QueueDepthWarningThreshold is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(QueueDepthWarningThreshold));
        }
    }
#pragma warning restore S3928, MA0015, S3236
}
