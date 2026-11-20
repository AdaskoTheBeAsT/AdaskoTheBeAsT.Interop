using System.Globalization;
using AdaskoTheBeAsT.Interop.Execution;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AdaskoTheBeAsT.Interop.Execution.Hosting;

/// <summary>
/// <see cref="IHealthCheck"/> reporting readiness and operational status for an
/// <see cref="IExecutionWorkerPool{TSession}"/> using <see cref="IExecutionWorkerPool{TSession}.GetSnapshot"/>.
/// </summary>
/// <typeparam name="TSession">The session type exposed to submitted work items.</typeparam>
public sealed class ExecutionWorkerPoolHealthCheck<TSession> : IHealthCheck
    where TSession : class
{
    private readonly IExecutionWorkerPool<TSession> _pool;
    private readonly ExecutionHealthCheckOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionWorkerPoolHealthCheck{TSession}"/> class.
    /// </summary>
    /// <param name="pool">The worker pool to inspect.</param>
    /// <param name="options">Optional health-check thresholds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> violates its invariants.</exception>
    public ExecutionWorkerPoolHealthCheck(
        IExecutionWorkerPool<TSession> pool,
        ExecutionHealthCheckOptions? options = null)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
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

        var snapshot = _pool.GetSnapshot();
        var data = BuildData(snapshot);
        var failureStatus = context?.Registration?.FailureStatus ?? HealthStatus.Unhealthy;

        if (TryCreateUnhealthyResult(snapshot, failureStatus, data, out var unhealthyResult))
        {
            return Task.FromResult(unhealthyResult);
        }

        return Task.FromResult(EvaluateOperationalHealth(snapshot, _options.QueueDepthWarningThreshold, data));
    }

    private static bool TryCreateUnhealthyResult(
        ExecutionWorkerPoolSnapshot snapshot,
        HealthStatus failureStatus,
        IReadOnlyDictionary<string, object> data,
        out HealthCheckResult result)
    {
        if (TryFindStartupFault(snapshot.Workers, out var startupFault))
        {
            result = new HealthCheckResult(
                failureStatus,
                "Execution worker pool encountered a startup fault.",
                startupFault,
                data);
            return true;
        }

        if (snapshot.IsDisposed)
        {
            result = new HealthCheckResult(
                failureStatus,
                "Execution worker pool is disposed.",
                exception: null,
                data);
            return true;
        }

        if (snapshot.WorkerCount == 0 || snapshot.FaultedWorkerCount >= snapshot.WorkerCount)
        {
            result = new HealthCheckResult(
                failureStatus,
                "All execution workers in the pool have faulted.",
                FindFirstFault(snapshot.Workers),
                data);
            return true;
        }

        if (snapshot.InitializedWorkerCount < snapshot.WorkerCount)
        {
            result = new HealthCheckResult(
                failureStatus,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Execution worker pool startup is not ready ({0}/{1} workers initialized).",
                    snapshot.InitializedWorkerCount,
                    snapshot.WorkerCount),
                exception: null,
                data);
            return true;
        }

        result = default;
        return false;
    }

    private static HealthCheckResult EvaluateOperationalHealth(
        ExecutionWorkerPoolSnapshot snapshot,
        int? queueDepthWarningThreshold,
        IReadOnlyDictionary<string, object> data)
    {
        if (snapshot.FaultedWorkerCount > 0)
        {
            return HealthCheckResult.Degraded(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Execution worker pool has partial worker loss ({0}/{1} workers faulted).",
                    snapshot.FaultedWorkerCount,
                    snapshot.WorkerCount),
                FindFirstFault(snapshot.Workers),
                data);
        }

        if (queueDepthWarningThreshold is { } threshold && snapshot.QueueDepth > threshold)
        {
            return HealthCheckResult.Degraded(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Execution worker pool queue depth ({0}) exceeds warning threshold ({1}).",
                    snapshot.QueueDepth,
                    threshold),
                exception: null,
                data);
        }

        return HealthCheckResult.Healthy("Execution worker pool is healthy.", data);
    }

    private static bool TryFindStartupFault(
        IReadOnlyList<ExecutionWorkerSnapshot> workers,
        out Exception? fault)
    {
        for (var index = 0; index < workers.Count; index++)
        {
            var worker = workers[index];
            if (worker.IsFaulted && !worker.IsInitialized)
            {
                fault = worker.Fault;
                return true;
            }
        }

        fault = null;
        return false;
    }

    private static Exception? FindFirstFault(IReadOnlyList<ExecutionWorkerSnapshot> workers)
    {
        for (var index = 0; index < workers.Count; index++)
        {
            if (workers[index].Fault is { } fault)
            {
                return fault;
            }
        }

        return null;
    }

    private static Dictionary<string, object> BuildData(ExecutionWorkerPoolSnapshot snapshot)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["workerCount"] = snapshot.WorkerCount,
            ["initializedWorkerCount"] = snapshot.InitializedWorkerCount,
            ["faultedWorkerCount"] = snapshot.FaultedWorkerCount,
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
