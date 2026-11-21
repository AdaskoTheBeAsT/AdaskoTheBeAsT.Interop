using System.Globalization;
using AdaskoTheBeAsT.Interop.Execution;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting;

/// <summary>
/// <see cref="IHealthCheck"/> reporting readiness and operational status for an
/// <see cref="IExecutionWorker{TSession}"/> using <see cref="IExecutionWorker{TSession}.GetSnapshot"/>.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public sealed class ExecutionWorkerHealthCheck<TSession> : IHealthCheck
    where TSession : class
{
    private readonly IExecutionWorker<TSession> _worker;
    private readonly ExecutionHealthCheckOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerHealthCheck{TSession}"/> class.
    /// </summary>
    /// <param name="worker">The worker to inspect.</param>
    /// <param name="options">Optional health-check thresholds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="worker"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> violates its invariants.</exception>
    public ExecutionWorkerHealthCheck(
        IExecutionWorker<TSession> worker,
        ExecutionHealthCheckOptions? options = null)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _options = (options ?? ExecutionHealthCheckOptions.Default).Snapshot();
        _options.Validate();
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<HealthCheckResult>(cancellationToken);
        }

        var snapshot = _worker.GetSnapshot();
        var data = BuildData(snapshot);
        var failureStatus = context?.Registration?.FailureStatus ?? HealthStatus.Unhealthy;

        if (snapshot.IsFaulted && !snapshot.IsInitialized)
        {
            return Task.FromResult(new HealthCheckResult(
                failureStatus,
                "Execution worker encountered a startup fault.",
                snapshot.Fault,
                data));
        }

        if (snapshot.IsDisposed)
        {
            return Task.FromResult(new HealthCheckResult(
                failureStatus,
                "Execution worker is disposed.",
                exception: null,
                data));
        }

        if (snapshot.IsFaulted)
        {
            return Task.FromResult(new HealthCheckResult(
                failureStatus,
                "Execution worker has faulted.",
                snapshot.Fault,
                data));
        }

        if (!snapshot.IsInitialized)
        {
            return Task.FromResult(new HealthCheckResult(
                failureStatus,
                "Execution worker startup is not ready.",
                exception: null,
                data));
        }

        if (_options.QueueDepthWarningThreshold is { } threshold && snapshot.QueueDepth > threshold)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Execution worker queue depth ({0}) exceeds warning threshold ({1}).",
                    snapshot.QueueDepth,
                    threshold),
                exception: null,
                data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Execution worker is healthy.", data));
    }

    private static Dictionary<string, object> BuildData(ExecutionWorkerSnapshot snapshot)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["isInitialized"] = snapshot.IsInitialized,
            ["isFaulted"] = snapshot.IsFaulted,
            ["queueDepth"] = snapshot.QueueDepth,
            ["isDisposed"] = snapshot.IsDisposed,
        };

        if (snapshot.Name is not null)
        {
            data["name"] = snapshot.Name;
        }

        return data;
    }
}
